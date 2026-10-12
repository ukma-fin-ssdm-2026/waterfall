// AI: фоновий воркер періодичного збору вакансій (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests, самоперевірка автора (§8)

using Microsoft.Extensions.Options;
using Waterfall.Services.Ingestion;

namespace Waterfall.Api.Workers;

/// <summary>
/// Фонова служба, що періодично запускає збір вакансій (<see cref="IVacancyIngestionService"/>).
/// Цикли виконуються послідовно й не накладаються; помилка одного циклу записується в журнал
/// і не зупиняє ні воркер, ні застосунок.
/// </summary>
public sealed class VacancyIngestionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IngestionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<VacancyIngestionWorker> _logger;

    public VacancyIngestionWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<IngestionOptions> options,
        TimeProvider timeProvider,
        ILogger<VacancyIngestionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Фоновий збір вакансій вимкнено (Ingestion:Enabled = false).");
            return;
        }

        _logger.LogInformation(
            "Фоновий збір вакансій запущено: інтервал {Interval}, каталог {Directory}.",
            _options.Interval,
            _options.RawDataDirectory);

        try
        {
            if (_options.InitialDelay > TimeSpan.Zero)
            {
                await Task.Delay(_options.InitialDelay, _timeProvider, stoppingToken);
            }

            using PeriodicTimer timer = new(_options.Interval, _timeProvider);
            do
            {
                await RunCycleAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатна зупинка застосунку
        }

        _logger.LogInformation("Фоновий збір вакансій зупинено.");
    }

    private async Task RunCycleAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Репозиторій і DbContext мають область видимості запиту, тож кожен цикл отримує власну
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            IVacancyIngestionService ingestion = scope.ServiceProvider.GetRequiredService<IVacancyIngestionService>();

            IngestionReport report = await ingestion.RunOnceAsync(stoppingToken);

            _logger.LogInformation(
                "Цикл збору завершено за {Duration}: документів {Documents} (з помилкою {FailedDocuments}), "
                + "записів {Items}: створено {Created}, оновлено {Updated}, пропущено {Skipped}, не збережено {Failed}.",
                report.Duration,
                report.Documents,
                report.FailedDocuments,
                report.Items,
                report.Created,
                report.Updated,
                report.Skipped,
                report.Failed);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Необроблений виняток у BackgroundService за замовчуванням зупиняє весь застосунок,
            // тож збій циклу лише записуємо й чекаємо наступного запуску
            _logger.LogError(ex, "Цикл збору вакансій завершився помилкою; наступна спроба через {Interval}.", _options.Interval);
        }
    }
}
