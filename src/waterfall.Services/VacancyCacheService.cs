// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
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

        if (filter.Page == 1 && string.IsNullOrWhiteSpace(filter.Search) && string.IsNullOrWhiteSpace(filter.Category) && filter.OnlyActive)
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

    private static string BuildFilterKey(VacancyFilterParameters f)
    {
        return $"catalog:p={f.Page}:ps={f.PageSize}:act={f.OnlyActive}:q={f.Search?.Trim().ToLower()}:cat={f.Category?.Trim().ToLower()}:exp={f.Experience?.Trim().ToLower()}:min={f.MinSalary}:max={f.MaxSalary}:src={f.Source}";
    }
}
