// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Entities;

namespace Waterfall.Repository.Interfaces;

/// <summary>
/// Репозиторій історії переглядів вакансій (FR-006, FR-007) на рівні БД, сутність VacancyViewDB.
/// Один запис на кожен перегляд. Читання лише за userId власника (THR-12).
/// </summary>
public interface IVacancyViewRepository
{
    Task AddAsync(VacancyViewDB entity, CancellationToken ct = default);

    /// <summary>Чи переглядав користувач цю вакансію протягом <paramref name="within"/>; запобігає реєстрації повторного перегляду однієї вакансії за короткий інтервал.</summary>
    Task<bool> ExistsRecentAsync(Guid userId, Guid vacancyId, TimeSpan within, CancellationToken ct = default);

    /// <summary>Перегляди користувача з вакансіями — джерело агрегації частоти технологій/категорій.</summary>
    Task<IReadOnlyList<VacancyViewDB>> GetForUserAsync(Guid userId, CancellationToken ct = default);
}
