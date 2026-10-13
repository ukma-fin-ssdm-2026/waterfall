// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: метод BulkUpsertAsync для фонового збору вакансій (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests на SQLite, самоперевірка автора (§8)

using Waterfall.Repository.Entities;

namespace Waterfall.Repository.Interfaces;

/// <summary>
/// Інтерфейс доступу до даних вакансій на рівні реляційної БД (Clean Architecture / Repository Layer).
/// Працює виключно із сутностями бази даних VacancyDB.
/// </summary>
public interface IVacancyRepository
{
    Task<(IReadOnlyList<VacancyDB> Items, int TotalCount)> GetAllAsync(
        string? search = null,
        string? category = null,
        string? experience = null,
        string? location = null,
        string? schedule = null,
        decimal? minSalary = null,
        decimal? maxSalary = null,
        string? source = null,
        bool onlyActive = true,
        string? sortBy = null,
        string? sortOrder = null,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default);

    Task<VacancyDB?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Зберігає вакансії з фонового збору: нові додає, наявні (той самий Source і ExternalId) оновлює,
    /// не змінюючи їхні Id, дату публікації та дату першої появи.
    /// </summary>
    Task<(int Created, int Updated)> BulkUpsertAsync(IReadOnlyList<VacancyDB> vacancies, CancellationToken ct = default);

    Task<VacancyDB> CreateAsync(VacancyDB entity, CancellationToken ct = default);
    Task<VacancyDB?> UpdateAsync(VacancyDB entity, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
