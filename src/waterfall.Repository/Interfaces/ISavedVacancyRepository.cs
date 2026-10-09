// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Models.SavedVacancies;

namespace Waterfall.Repository.Interfaces;

/// <summary>
/// Інтерфейс репозиторію збережених вакансій («лайків» / закладок).
/// </summary>
public interface ISavedVacancyRepository
{
    /// <summary>
    /// Отримати перелік усіх збережених вакансій користувача.
    /// </summary>
    /// <param name="onlyActive">Якщо true — повертає тільки збережені вакансії, що залишаються активними. Якщо false — повертає всі, включаючи закриті.</param>
    /// <param name="ct">Токен скасування операції.</param>
    Task<IReadOnlyList<SavedVacancyDto>> GetAllAsync(bool onlyActive = true, CancellationToken ct = default);
    Task<SavedVacancyDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SavedVacancyDto> SaveAsync(SaveVacancyRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsForVacancyAsync(Guid vacancyId, CancellationToken ct = default);
}
