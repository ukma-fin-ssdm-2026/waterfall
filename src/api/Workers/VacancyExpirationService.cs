// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Interfaces;
using Waterfall.Services;

namespace Waterfall.Api.Workers;

/// <summary>
/// Фоновий воркер деактивації застарілих вакансій:
/// вакансії, опубліковані понад 30 днів тому, автоматично позначаються як неактивні (<c>IsActive = false</c>).
/// </summary>
public sealed class VacancyExpirationService : BackgroundService
{
    public static readonly TimeSpan VacancyLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IVacancyCacheService _cacheService;
    private readonly ILogger<VacancyExpirationService> _logger;
    private readonly TimeProvider _time;

    public VacancyExpirationService(
        IServiceScopeFactory scopeFactory,
        IVacancyCacheService cacheService,
        ILogger<VacancyExpirationService> logger,
        TimeProvider? time = null)
    {
        _scopeFactory = scopeFactory;
        _cacheService = cacheService;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await DeactivateOnceSafeAsync(stoppingToken);

        using PeriodicTimer timer = new(CheckInterval, _time);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await DeactivateOnceSafeAsync(stoppingToken);
        }
    }

    internal async Task<int> DeactivateOnceAsync(CancellationToken ct = default)
    {
        DateTimeOffset cutoff = _time.GetUtcNow() - VacancyLifetime;

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IVacancyRepository repository = scope.ServiceProvider.GetRequiredService<IVacancyRepository>();
        int deactivated = await repository.DeactivateExpiredAsync(cutoff, ct);

        if (deactivated > 0)
        {
            await _cacheService.InvalidateCatalogAsync(ct);
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Деактивовано {Count} застарілих вакансій (старших за 30 днів).", deactivated);
            }
        }

        return deactivated;
    }

    private async Task DeactivateOnceSafeAsync(CancellationToken ct)
    {
        try
        {
            await DeactivateOnceAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Коректне завершення при зупинці сервера.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Помилка під час деактивації застарілих вакансій.");
        }
    }
}
