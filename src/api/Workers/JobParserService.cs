// AI: фоновий збір вакансій з djinni.co, work.ua та JSON-дампів (issue #62), увімкнення й вимкнення без перезапуску API; перевірено: юніт-тести tests/waterfall.Ingestion.Tests, ручний запуск API проти справжніх djinni.co і work.ua, ручне перемикання Ingestion:Enabled у файлі на запущеному API, самоперевірка автора (§8)

using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Api.Workers;

/// <summary>
/// Job Parser Service: раз на <see cref="IngestionOptions.Interval"/> завантажує найсвіжіші вакансії з djinni.co, work.ua, robota.ua і jobs.dou.ua
/// (а якщо задано каталог — ще й JSON-дампи) та передає їх на нормалізацію й збереження
/// (<see cref="IVacancyIngestionService.ProcessRawVacanciesAsync"/>).
/// Збій сайту чи одного файлу записується в журнал і не зупиняє ні інші джерела, ні API.
/// Збір вмикається й вимикається без перезапуску API: служба стежить за Ingestion:Enabled в appsettings.json.
/// </summary>
public class JobParserService : BackgroundService
{
    private const string EnabledKey = "Ingestion:Enabled";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IngestionOptions _options;
    private readonly string? _directory;
    private readonly ILogger<JobParserService> _logger;
    private readonly List<SiteCrawler> _sites;
    private readonly IConfiguration? _configuration;
    private readonly IDisposable? _settingsWatch;

    // Будить службу, коли Enabled змінився: вона чекає або на ввімкнення, або на наступний збір
    private readonly SemaphoreSlim _settingsChanged = new(0, 1);
    private volatile bool _enabled;

    public JobParserService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IOptions<IngestionOptions> options,
        IHostEnvironment environment,
        ILogger<JobParserService> logger,
        IBrowserFetcher? browserFetcher = null,
        IConfiguration? configuration = null)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _configuration = configuration;
        _enabled = _options.Enabled || _options.EnableOnStart;

        // Без конфігурації (як у частині тестів) стан задається лише на старті
        if (configuration is not null)
        {
            _settingsWatch = ChangeToken.OnChange(configuration.GetReloadToken, ReadEnabledSetting);
        }

        _sites =
        [
            new SiteCrawler(new DjinniParser(), _options.DjinniPages, _options.RequestDelay, httpClientFactory, logger),
            new SiteCrawler(new WorkUaParser(), _options.WorkUaPages, _options.RequestDelay, httpClientFactory, logger, browserFetcher: browserFetcher),
            new SiteCrawler(new RobotaUaParser(), _options.RobotaUaPages, _options.RequestDelay, httpClientFactory, logger, browserFetcher: browserFetcher),
            new SiteCrawler(new DouParser(), _options.DouPages, _options.RequestDelay, httpClientFactory, logger)
        ];

        // Відносний шлях рахуємо від каталогу проєкту API (src/api); порожній шлях вимикає дампи
        _directory = string.IsNullOrWhiteSpace(_options.RawDataDirectory)
            ? null
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, _options.RawDataDirectory));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled && _configuration is null)
        {
            _logger.LogInformation("Фоновий збір вакансій вимкнено (Ingestion:Enabled = false).");
            return;
        }

        _logger.LogInformation(
            "Фоновий збір вакансій: djinni.co — {DjinniPages} стор., work.ua — {WorkUaPages} стор., robota.ua — {RobotaUaPages} стор., jobs.dou.ua — {DouPages} стор., каталог з дампами — {Directory}, інтервал {Interval}.",
            _options.DjinniPages, _options.WorkUaPages, _options.RobotaUaPages, _options.DouPages, _directory ?? "вимкнено", _options.Interval);

        try
        {
            while (true)
            {
                if (!_enabled)
                {
                    _logger.LogInformation("Фоновий збір вакансій вимкнено (Ingestion:Enabled = false). Щоб увімкнути без перезапуску, поставте true в appsettings.json.");
                    while (!_enabled)
                    {
                        await _settingsChanged.WaitAsync(Timeout.InfiniteTimeSpan, stoppingToken);
                    }
                }

                await CollectOnceAsync(stoppingToken);

                // Наступний збір — через Interval або одразу після вимкнення, щоб не чекати його до кінця
                await _settingsChanged.WaitAsync(_options.Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатна зупинка застосунку
        }
    }

    public override void Dispose()
    {
        _settingsWatch?.Dispose();
        _settingsChanged.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task CollectOnceAsync(CancellationToken ct)
    {
        // Сайти обходимо по черзі: так до кожного йде не більше одного запиту за раз.
        // Вимкнений посеред збору — до решти сайтів і дампів уже не йдемо
        foreach (SiteCrawler site in _sites.Where(site => site.Enabled))
        {
            if (!_enabled)
            {
                return;
            }

            await IngestAsync(site.Name, () => site.CrawlAsync(ct), ct);
        }

        if (_enabled)
        {
            await ParseAllFilesAsync(ct);
        }
    }

    /// <summary>
    /// appsettings.json збережено (вручну чи з екрана «Системні налаштування»): беремо з нього Enabled без перезапуску API.
    /// Решта параметрів збору (сторінки, інтервал, пауза) діє з наступного запуску.
    /// </summary>
    private void ReadEnabledSetting()
    {
        bool enabled;
        try
        {
            enabled = _configuration!.GetValue<bool>(EnabledKey);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Ingestion:Enabled має бути true або false ({Error}), тож фоновий збір лишається як був.", ex.Message);
            return;
        }

        // Файл зберігають і з інших причин, а подія про зміну іноді приходить двічі: реагуємо лише на справжню зміну
        if (enabled == _enabled)
        {
            return;
        }

        _enabled = enabled;
        _logger.LogInformation(enabled
            ? "Фоновий збір вакансій увімкнено без перезапуску API (Ingestion:Enabled = true)."
            : "Фоновий збір вакансій вимкнено без перезапуску API (Ingestion:Enabled = false).");

        try
        {
            _settingsChanged.Release();
        }
        catch (Exception ex) when (ex is SemaphoreFullException or ObjectDisposedException)
        {
            // Служба вже розбуджена або зупиняється
        }
    }

    /// <summary>Розбирає дамп Djinni формату { "source": "djinni", "jobs": [ ... ] } (див. data/raw/README.md).</summary>
    public static List<RawVacancy> ParseDump(string json)
    {
        DjinniDump dump = JsonSerializer.Deserialize<DjinniDump>(json, JsonOptions)
            ?? throw new JsonException("Файл порожній.");

        if (!string.Equals(dump.Source, "djinni", StringComparison.OrdinalIgnoreCase))
        {
            throw new JsonException($"Очікувався дамп Djinni, а в файлі source = \"{dump.Source}\".");
        }

        return (dump.Jobs ?? []).Select(job => job with { Source = JobSource.Djinni }).ToList();
    }

    private async Task ParseAllFilesAsync(CancellationToken ct)
    {
        if (_directory is null)
        {
            return;
        }

        if (!Directory.Exists(_directory))
        {
            _logger.LogWarning("Каталог з дампами {Directory} не знайдено.", _directory);
            return;
        }

        foreach (string file in Directory.GetFiles(_directory, "*.json").Order(StringComparer.Ordinal))
        {
            await IngestAsync(Path.GetFileName(file), async () => ParseDump(await File.ReadAllTextAsync(file, ct)), ct);
        }
    }

    /// <summary>Отримує сирі вакансії з одного джерела й передає їх на нормалізацію. Помилку лише записує в журнал.</summary>
    private async Task IngestAsync(string source, Func<Task<List<RawVacancy>>> load, CancellationToken ct)
    {
        try
        {
            List<RawVacancy> rawItems = await load();
            if (rawItems.Count == 0)
            {
                return;
            }

            // Репозиторій працює через DbContext, якому потрібен окремий scope
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            IVacancyIngestionService ingestion = scope.ServiceProvider.GetRequiredService<IVacancyIngestionService>();
            await ingestion.ProcessRawVacanciesAsync(rawItems, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Необроблений виняток у BackgroundService зупинив би весь API, тож лише записуємо його
            _logger.LogError(ex, "Не вдалося обробити {Source}.", source);
        }
    }

    private sealed record DjinniDump(string? Source, List<RawVacancy>? Jobs);
}

/// <summary>Налаштування із секції Ingestion в appsettings.json.</summary>
public class IngestionOptions
{
    /// <summary>Чи збирати вакансії. За замовчуванням ні; зміна в appsettings.json діє без перезапуску API.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Увімкнути збір на старті, не змінюючи appsettings.json: <c>dotnet run --project src/api -- --Ingestion:EnableOnStart=true</c>.
    /// Під час роботи збір і далі вмикає та вимикає <see cref="Enabled"/>.
    /// </summary>
    public bool EnableOnStart { get; set; }

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Скільки перших сторінок djinni.co/jobs/ читати за раз (по 15 вакансій); 0 — не звертатися до сайту.
    /// Для нових вакансій додатково читається їхня сторінка, тож перший збір після запуску — до 16 запитів на сторінку списку.
    /// </summary>
    public int DjinniPages { get; set; } = 3;

    /// <summary>Скільки перших сторінок усіх вакансій work.ua читати за раз (по 14 вакансій); 0 — не звертатися до сайту.</summary>
    public int WorkUaPages { get; set; } = 3;

    /// <summary>Скільки перших сторінок вакансій robota.ua читати за раз; 0 — не звертатися до сайту.</summary>
    public int RobotaUaPages { get; set; } = 1;

    /// <summary>RSS-стрічка jobs.dou.ua — одна сторінка з 50 найсвіжішими вакансіями: 1 — читати, 0 — не звертатися до сайту.</summary>
    public int DouPages { get; set; } = 1;

    /// <summary>Пауза перед кожним запитом до сайту.</summary>
    public TimeSpan RequestDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Каталог з JSON-дампами відносно src/api; якщо не задано, дампи не читаються.</summary>
    public string? RawDataDirectory { get; set; }
}

/// <summary>
/// Не дає налаштуваннями зробити збір нав'язливим для сайтів (THR-11, PERF-003): з надто частим збором
/// чи надто короткою паузою між запитами API не запуститься, а в журналі буде пояснення.
/// </summary>
public sealed class IngestionOptionsValidator : IValidateOptions<IngestionOptions>
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(10);

    // PERF-003: не більше 2 запитів на секунду до одного джерела
    public static readonly TimeSpan MinRequestDelay = TimeSpan.FromMilliseconds(500);

    // 10 сторінок — це до 160 запитів до сайту за перший збір після запуску
    public const int MaxPages = 10;

    public ValidateOptionsResult Validate(string? name, IngestionOptions options)
    {
        List<string> errors = [];
        if (options.Interval < MinInterval)
        {
            errors.Add($"Ingestion:Interval має бути не менше {MinInterval.TotalMinutes:0} хв, а задано {options.Interval:c}");
        }

        if (options.RequestDelay < MinRequestDelay)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture,
                $"Ingestion:RequestDelay має бути не менше {MinRequestDelay.TotalSeconds} с, а задано {options.RequestDelay.TotalSeconds} с"));
        }

        foreach ((string setting, int pages) in new[] { ("DjinniPages", options.DjinniPages), ("WorkUaPages", options.WorkUaPages), ("RobotaUaPages", options.RobotaUaPages), ("DouPages", options.DouPages) })
        {
            if (pages is < 0 or > MaxPages)
            {
                errors.Add($"Ingestion:{setting} має бути від 0 до {MaxPages}, а задано {pages}");
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
