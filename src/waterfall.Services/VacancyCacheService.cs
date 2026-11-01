// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: ключ кешу враховує всі фільтри й сортування; перевірено: тести VacancyCacheServiceTests

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Waterfall.Repository.Entities;
using Waterfall.Services.Models.Common;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services;

/// <summary>
/// Реалізація сервісу кешування каталогу вакансій на базі IMemoryCache (In-Memory).
/// Реалізує патерн Cache-Aside, автоматичну інвалідацію та режим Stale-Cache (ADR-003, REL-001, PERF-001).
/// </summary>
public class VacancyCacheService : IVacancyCacheService
{
    private readonly IMemoryCache _memoryCache;
    private readonly TimeSpan _defaultTtl = TimeSpan.FromMinutes(10);
    private readonly TimeSpan _staleMaxAge = TimeSpan.FromHours(12);

    private CancellationTokenSource _catalogCts = new();
    private readonly object _ctsLock = new();

    private PagedResult<VacancyDto>? _staleCatalog;
    private DateTimeOffset? _lastRefreshedAt;

    private long _hits;
    private long _misses;

    public VacancyCacheService(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
    }

    public DateTimeOffset? LastCatalogRefreshedAt => _lastRefreshedAt;

    public Task<PagedResult<VacancyDto>?> GetPagedVacanciesAsync(VacancyFilterParameters filter, CancellationToken ct = default)
    {
        string key = BuildFilterKey(filter);

        if (_memoryCache.TryGetValue(key, out PagedResult<VacancyDto>? result) && result != null)
        {
            Interlocked.Increment(ref _hits);
            return Task.FromResult<PagedResult<VacancyDto>?>(result);
        }

        Interlocked.Increment(ref _misses);
        return Task.FromResult<PagedResult<VacancyDto>?>(null);
    }

    public Task SetPagedVacanciesAsync(
        VacancyFilterParameters filter,
        PagedResult<VacancyDto> result,
        TimeSpan? ttl = null,
        CancellationToken ct = default)
    {
        string key = BuildFilterKey(filter);
        TimeSpan expiry = ttl ?? _defaultTtl;

        CancellationToken token;
        lock (_ctsLock)
        {
            token = _catalogCts.Token;
        }

        MemoryCacheEntryOptions options = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromMinutes(2))
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(15))
            .AddExpirationToken(new CancellationChangeToken(token));

        _memoryCache.Set(key, result, options);

        if (IsPlainFirstPage(filter))
        {
            _staleCatalog = result;
            _lastRefreshedAt = DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }

    public Task<VacancyDto?> GetVacancyByIdAsync(Guid id, CancellationToken ct = default)
    {
        string key = $"vacancy:detail:{id}";
        if (_memoryCache.TryGetValue(key, out VacancyDto? vacancy) && vacancy != null)
        {
            Interlocked.Increment(ref _hits);
            return Task.FromResult<VacancyDto?>(vacancy);
        }

        Interlocked.Increment(ref _misses);
        return Task.FromResult<VacancyDto?>(null);
    }

    public Task SetVacancyByIdAsync(Guid id, VacancyDto vacancy, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        string key = $"vacancy:detail:{id}";
        TimeSpan expiry = ttl ?? _defaultTtl;

        _memoryCache.Set(key, vacancy, expiry);
        return Task.CompletedTask;
    }

    public Task InvalidateCatalogAsync(CancellationToken ct = default)
    {
        lock (_ctsLock)
        {
            _catalogCts.Cancel();
            _catalogCts.Dispose();
            _catalogCts = new CancellationTokenSource();
        }

        return Task.CompletedTask;
    }

    public async Task InvalidateVacancyAsync(Guid id, CancellationToken ct = default)
    {
        _memoryCache.Remove($"vacancy:detail:{id}");
        await InvalidateCatalogAsync(ct);
    }

    public PagedResult<VacancyDto>? GetStaleCatalog()
    {
        if (_staleCatalog != null && _lastRefreshedAt.HasValue)
        {
            TimeSpan age = DateTimeOffset.UtcNow - _lastRefreshedAt.Value;
            if (age <= _staleMaxAge)
            {
                return _staleCatalog;
            }
        }

        return null;
    }

    public (long Hits, long Misses) GetMetrics()
    {
        return (Interlocked.Read(ref _hits), Interlocked.Read(ref _misses));
    }

    /// <summary>
    /// Кешувати як Stale-Cache можна лише «чисту» першу сторінку без фільтрів: інакше застарілий результат
    /// одного запиту видавався б за результат іншого (REL-001).
    /// </summary>
    private static bool IsPlainFirstPage(VacancyFilterParameters filter) =>
        filter.Page == 1
        && filter.OnlyActive
        && string.IsNullOrWhiteSpace(filter.Search)
        && string.IsNullOrWhiteSpace(filter.Category)
        && string.IsNullOrWhiteSpace(filter.Experience)
        && string.IsNullOrWhiteSpace(filter.Location)
        && string.IsNullOrWhiteSpace(filter.CategoryGroup)
        && filter.WorkFormat is null or WorkFormat.None
        && filter.MaxExperienceMonths is null
        && filter.Skills is null or { Count: 0 }
        && filter.MinSalary is null
        && filter.MaxSalary is null
        && filter.Source is null or JobSource.Unknown;

    // Ключ враховує кожне поле фільтра й сортування: інакше «за спаданням» віддавалося б із кешу «за зростанням»
    private static string BuildFilterKey(VacancyFilterParameters f)
    {
        string skills = f.Skills is { Count: > 0 } terms ? string.Join(",", terms) : string.Empty;
        return $"catalog:p={f.Page}:ps={f.PageSize}:act={f.OnlyActive}" +
               $":q={f.Search?.Trim().ToLowerInvariant()}:cat={f.Category?.Trim().ToLowerInvariant()}" +
               $":exp={f.Experience?.Trim().ToLowerInvariant()}:min={f.MinSalary}:max={f.MaxSalary}:src={f.Source}" +
               $":wf={(int?)f.WorkFormat}:maxexp={f.MaxExperienceMonths}:cg={f.CategoryGroup?.Trim()}" +
               $":loc={f.Location?.Trim().ToLowerInvariant()}:sk={skills}" +
               $":sb={f.SortBy?.Trim().ToLowerInvariant()}:so={f.SortOrder?.Trim().ToLowerInvariant()}";
    }
}
