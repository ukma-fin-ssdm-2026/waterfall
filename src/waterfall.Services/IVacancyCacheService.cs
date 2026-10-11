// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Services.Models.Common;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services;

/// <summary>
/// Інтерфейс сервісу In-Memory кешування вакансій за патерном Cache-Aside.
/// Відповідає вимогам docs/architecture/decisions/ADR-003-cache-aside.md та PERF-001 / REL-001.
/// </summary>
public interface IVacancyCacheService
{
    Task<PagedResult<VacancyDto>?> GetPagedVacanciesAsync(VacancyFilterParameters filter, CancellationToken ct = default);
    Task SetPagedVacanciesAsync(VacancyFilterParameters filter, PagedResult<VacancyDto> result, TimeSpan? ttl = null, CancellationToken ct = default);

    Task<VacancyDto?> GetVacancyByIdAsync(Guid id, CancellationToken ct = default);
    Task SetVacancyByIdAsync(Guid id, VacancyDto vacancy, TimeSpan? ttl = null, CancellationToken ct = default);

    Task InvalidateCatalogAsync(CancellationToken ct = default);
    Task InvalidateVacancyAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Мітка часу останнього успішного оновлення кешу (REL-001).
    /// </summary>
    DateTimeOffset? LastCatalogRefreshedAt { get; }

    /// <summary>
    /// Отримати дані застарілого кешу (Stale-Cache) до 12 годин у разі збою джерела чи БД (REL-001).
    /// </summary>
    PagedResult<VacancyDto>? GetStaleCatalog();

    /// <summary>
    /// Показники ефективності кешування (Hit count, Miss count).
    /// </summary>
    (long Hits, long Misses) GetMetrics();
}
