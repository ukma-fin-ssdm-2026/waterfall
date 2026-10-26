// AI: фоновий збір вакансій з djinni.co, work.ua та JSON-дампів (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests, ручний запуск API проти справжніх djinni.co і work.ua, самоперевірка автора (§8)

using System.Text.Json;
using Microsoft.Extensions.Options;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Api.Workers;

/// <summary>
/// Job Parser Service: раз на <see cref="IngestionOptions.Interval"/> завантажує найсвіжіші вакансії з djinni.co і work.ua
/// (а якщо задано каталог — ще й JSON-дампи) та передає їх на нормалізацію й збереження
/// (<see cref="IVacancyIngestionService.ProcessRawVacanciesAsync"/>).
/// Збій сайту чи одного файлу записується в журнал і не зупиняє ні інші джерела, ні API.
/// </summary>
public class JobParserService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IngestionOptions _options;
    private readonly string? _directory;
    private readonly ILogger<JobParserService> _logger;
    private readonly List<SiteCrawler> _sites;

    public JobParserService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IOptions<IngestionOptions> options,
        IHostEnvironment environment,
        ILogger<JobParserService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _sites =
        [
            new SiteCrawler(new DjinniParser(), _options.DjinniPages, _options.RequestDelay, httpClientFactory, logger),
            new SiteCrawler(new WorkUaParser(), _options.WorkUaPages, _options.RequestDelay, httpClientFactory, logger)
        ];

        // Відносний шлях рахуємо від каталогу проєкту API (src/api); порожній шлях вимикає дампи
        _directory = string.IsNullOrWhiteSpace(_options.RawDataDirectory)
            ? null
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, _options.RawDataDirectory));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Фоновий збір вакансій вимкнено (Ingestion:Enabled = false).");
            return;
        }

        _logger.LogInformation(
            "Фоновий збір вакансій: djinni.co — {DjinniPages} стор., work.ua — {WorkUaPages} стор., каталог з дампами — {Directory}, інтервал {Interval}.",
            _options.DjinniPages, _options.WorkUaPages, _directory ?? "вимкнено", _options.Interval);

        using PeriodicTimer timer = new(_options.Interval);
        try
        {
            do
            {
                // Сайти обходимо по черзі: так до кожного йде не більше одного запиту за раз
                foreach (SiteCrawler site in _sites.Where(site => site.Enabled))
                {
                    await IngestAsync(site.Name, () => site.CrawlAsync(stoppingToken), stoppingToken);
                }

                await ParseAllFilesAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатна зупинка застосунку
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
    public bool Enabled { get; set; }
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Скільки перших сторінок djinni.co/jobs/ читати за раз (по 15 вакансій); 0 — не звертатися до сайту.
    /// Для нових вакансій додатково читається їхня сторінка, тож перший збір після запуску — до 16 запитів на сторінку списку.
    /// </summary>
    public int DjinniPages { get; set; } = 3;

    /// <summary>Скільки перших сторінок усіх вакансій work.ua читати за раз (по 14 вакансій); 0 — не звертатися до сайту.</summary>
    public int WorkUaPages { get; set; } = 3;

    /// <summary>Пауза перед кожним запитом до сайту.</summary>
    public TimeSpan RequestDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Каталог з JSON-дампами відносно src/api; якщо не задано, дампи не читаються.</summary>
    public string? RawDataDirectory { get; set; }
}

