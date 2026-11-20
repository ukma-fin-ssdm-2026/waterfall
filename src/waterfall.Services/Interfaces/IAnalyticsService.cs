// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Services.Models.Analytics;

namespace Waterfall.Services.Interfaces;

/// <summary>
/// Сервіс агрегації ринкової аналітики вакансій для аналітиків та роботодавців (FR-003, FR-008).
/// </summary>
public interface IAnalyticsService
{
    Task<MarketAnalyticsDto> GetMarketAnalyticsAsync(
        string? category = null,
        int months = 3,
        CancellationToken ct = default);
}
