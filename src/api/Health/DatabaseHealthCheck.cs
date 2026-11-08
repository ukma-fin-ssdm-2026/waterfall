// AI: Health Check для перевірки з'єднання з базою даних (Lab 9 Bonus, §3.4)
// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Waterfall.Repository.Data;

namespace Waterfall.Api.Health;

/// <summary>
/// Перевірка здоров'я підключення до реляційної бази даних (SQLite / MSSQL).
/// Виконує швидкий запит CanConnectAsync із таймаутом, щоб не блокувати probe-перевірки.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly WaterfallDbContext _dbContext;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(WaterfallDbContext dbContext, ILogger<DatabaseHealthCheck> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            bool canConnect = await _dbContext.Database.CanConnectAsync(linkedCts.Token);
            if (canConnect)
            {
                return HealthCheckResult.Healthy("Підключення до бази даних активне та відповідає.");
            }

            _logger.LogWarning("База даних не відповідає на запит CanConnect.");
            return HealthCheckResult.Unhealthy("База даних недоступна (CanConnect повернув false).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Помилка перевірки здоров'я бази даних.");
            return HealthCheckResult.Unhealthy($"Збій підключення до бази даних: {ex.Message}", ex);
        }
    }
}

