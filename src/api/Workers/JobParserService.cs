// AI: фоновий парсер JSON-дампів вакансій (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests, ручний запуск API, самоперевірка автора (§8)

using System.Text.Json;
using Microsoft.Extensions.Options;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Api.Workers;

/// <summary>
/// Job Parser Service: раз на <see cref="IngestionOptions.Interval"/> читає JSON-дампи вакансій з каталогу
/// й передає їх на нормалізацію та збереження (<see cref="IVacancyIngestionService.ProcessRawVacanciesAsync"/>).
/// Помилка в одному файлі записується в журнал і не зупиняє ні інші файли, ні API.
/// </summary>
public class JobParserService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IngestionOptions _options;
    private readonly string _directory;
    private readonly ILogger<JobParserService> _logger;

    public JobParserService(
        IServiceScopeFactory scopeFactory,
        IOptions<IngestionOptions> options,
        IHostEnvironment environment,
        ILogger<JobParserService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;

        // Відносний шлях рахуємо від каталогу проєкту API (src/api)
        _directory = Path.GetFullPath(Path.Combine(environment.ContentRootPath, _options.RawDataDirectory));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Фоновий збір вакансій вимкнено (Ingestion:Enabled = false).");
            return;
        }

        _logger.LogInformation("Фоновий збір вакансій: каталог {Directory}, інтервал {Interval}.", _directory, _options.Interval);

        using PeriodicTimer timer = new(_options.Interval);
        try
        {
            do
            {
                await ParseAllFilesAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатна зупинка застосунку
        }
    }

    private async Task ParseAllFilesAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_directory))
        {
            _logger.LogWarning("Каталог з дампами {Directory} не знайдено.", _directory);
            return;
        }

        foreach (string file in Directory.GetFiles(_directory, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                List<RawVacancy> rawItems = ParseDump(await File.ReadAllTextAsync(file, ct));

                // Репозиторій працює через DbContext, якому потрібен окремий scope
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                IVacancyIngestionService ingestion = scope.ServiceProvider.GetRequiredService<IVacancyIngestionService>();
                await ingestion.ProcessRawVacanciesAsync(rawItems, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Необроблений виняток у BackgroundService зупинив би весь API, тож лише записуємо його
                _logger.LogError(ex, "Не вдалося обробити файл {File}.", Path.GetFileName(file));
            }
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

    private sealed record DjinniDump(string? Source, List<RawVacancy>? Jobs);
}

/// <summary>Налаштування із секції Ingestion в appsettings.json.</summary>
public class IngestionOptions
{
    public bool Enabled { get; set; }
    public string RawDataDirectory { get; set; } = "../../data/raw";
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(30);
}
