// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Entities;

namespace Waterfall.Repository.Interfaces;

/// <summary>
/// Репозиторій історії пошукових запитів (FR-007) на рівні БД, сутність SearchQueryDB.
/// Читання лише за userId власника (THR-12).
/// </summary>
public interface ISearchQueryRepository
{
    Task AddAsync(SearchQueryDB entity, CancellationToken ct = default);

    /// <summary>Останні <paramref name="limit"/> запитів користувача в порядку спадання часу запиту.</summary>
    Task<IReadOnlyList<SearchQueryDB>> GetRecentAsync(Guid userId, int limit, CancellationToken ct = default);

    /// <summary>Останній за часом запит користувача; застосовується для запобігання запису послідовних повторів того самого запиту.</summary>
    Task<SearchQueryDB?> GetLatestAsync(Guid userId, CancellationToken ct = default);

    Task<int> GetCountAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Видаляє всі пошукові запити користувача. Повертає true, якщо видалено хоча б один запис.</summary>
    Task<bool> DeleteAllForUserAsync(Guid userId, CancellationToken ct = default);
}
