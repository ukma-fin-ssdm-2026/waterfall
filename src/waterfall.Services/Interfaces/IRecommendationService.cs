// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Interfaces;

/// <summary>
/// Сервіс персоналізованих рекомендацій вакансій (FR-006, US-004)
/// на основі збережених вакансій та історії пошукових запитів шукача.
/// </summary>
public interface IRecommendationService
{
    /// <summary>
    /// Підібрати персоналізовані рекомендації для користувача <paramref name="userId"/>.
    /// Вже збережені вакансії обов'язково виключаються; якщо збережених вакансій немає —
    /// повертається порожній список із <see cref="RecommendationsResultDto.SavedCount"/> = 0.
    /// </summary>
    Task<RecommendationsResultDto> GetRecommendationsAsync(Guid userId, int limit = 20, CancellationToken ct = default);
}
