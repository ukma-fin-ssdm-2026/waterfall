// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Entities;

namespace Waterfall.Repository.Interfaces;

/// <summary>
/// Інтерфейс репозиторію збережених вакансій («лайків» / закладок) на рівні БД.
/// Працює виключно із сутностями SavedVacancyDB.
/// </summary>
public interface ISavedVacancyRepository
{
    /// <summary>
    /// Отримати перелік усіх збережених вакансій користувача.
    /// </summary>
    /// <param name="onlyActive">Якщо true — повертає тільки збережені вакансії, що залишаються активними. Якщо false — повертає всі, включаючи закриті (архівні, максимум 1000).</param>
    /// <param name="ct">Токен скасування операції.</param>
    Task<IReadOnlyList<SavedVacancyDB>> GetAllAsync(Guid userId, bool onlyActive = true, CancellationToken ct = default);
    Task<SavedVacancyDB?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<SavedVacancyDB> SaveAsync(SavedVacancyDB entity, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<bool> ExistsForVacancyAsync(Guid vacancyId, Guid? userId, CancellationToken ct = default);
    Task<HashSet<Guid>> GetSavedVacancyIdsAsync(IEnumerable<Guid> vacancyIds, Guid userId, CancellationToken ct = default);
}
