// AI: моки та тестові дублери репозиторіїв (RepositoryMocks, FakeVacancyRepository, FakeSavedVacancyRepository, Lab 06); перевірено: запуск dotnet test, самоперевірка автора (Денис Сидоренко, §8)

using Microsoft.Extensions.Logging;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Common;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Tests;

/// <summary>
/// Моки та тестові дублери (RepositoryMocks) для ізольованого тестування сервісного рівня.
/// Дозволяють тестувати Cache-Aside, Stale-Cache (REL-001) та бізнес-правила в ізоляції від БД.
/// </summary>
public sealed class FakeLogger<T> : ILogger<T>
{
    private readonly List<string> _logs = [];
    public IReadOnlyList<string> Logs => _logs;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _logs.Add($"[{logLevel}] {formatter(state, exception)}");
    }
}

public sealed class FakeVacancyRepository : IVacancyRepository
{
    public List<VacancyDB> Vacancies { get; } = [];
    public bool ShouldThrowOnGetAll { get; set; }
    public List<string?> GetAllSearchCalls { get; } = [];
    public List<string?> GetAllSourceCalls { get; } = [];
    public List<VacancyDB> CreatedVacancies { get; } = [];
    public List<VacancyDB> UpdatedVacancies { get; } = [];
    public List<Guid> DeletedIds { get; } = [];

    public Task<(IReadOnlyList<VacancyDB> Items, int TotalCount)> GetAllAsync(
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
        CancellationToken ct = default)
    {
        if (ShouldThrowOnGetAll)
        {
            throw new InvalidOperationException("Simulated database failure for Stale-Cache testing.");
        }

        GetAllSearchCalls.Add(search);
        GetAllSourceCalls.Add(source);
        IReadOnlyList<VacancyDB> items = Vacancies
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult((items, Vacancies.Count));
    }

    public Task<VacancyDB?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        VacancyDB? match = Vacancies.FirstOrDefault(v => v.Id == id);
        return Task.FromResult(match);
    }

    public Task<VacancyDB> CreateAsync(VacancyDB entity, CancellationToken ct = default)
    {
        Vacancies.Add(entity);
        CreatedVacancies.Add(entity);
        return Task.FromResult(entity);
    }

    public Task<VacancyDB?> UpdateAsync(VacancyDB entity, CancellationToken ct = default)
    {
        int index = Vacancies.FindIndex(v => v.Id == entity.Id);
        if (index == -1)
        {
            return Task.FromResult<VacancyDB?>(null);
        }

        Vacancies[index] = entity;
        UpdatedVacancies.Add(entity);
        return Task.FromResult<VacancyDB?>(entity);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        int removed = Vacancies.RemoveAll(v => v.Id == id);
        if (removed > 0)
        {
            DeletedIds.Add(id);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<(int Created, int Updated)> BulkUpsertAsync(IReadOnlyList<VacancyDB> vacancies, CancellationToken ct = default)
    {
        return Task.FromResult((vacancies.Count, 0));
    }
}

public sealed class FakeSavedVacancyRepository : ISavedVacancyRepository
{
    public List<SavedVacancyDB> SavedVacancies { get; } = [];
    public List<Guid> ExistsCalls { get; } = [];
    public List<Guid> DeleteCalls { get; } = [];

    public Task<IReadOnlyList<SavedVacancyDB>> GetAllAsync(Guid userId, bool onlyActive = true, CancellationToken ct = default)
    {
        IReadOnlyList<SavedVacancyDB> matches = SavedVacancies
            .Where(s => s.UserId == userId && (!onlyActive || s.Vacancy == null || s.Vacancy.IsActive))
            .ToList();
        return Task.FromResult(matches);
    }

    public Task<SavedVacancyDB?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        SavedVacancyDB? match = SavedVacancies.FirstOrDefault(s => (s.Id == id || s.VacancyId == id) && s.UserId == userId);
        return Task.FromResult(match);
    }

    public Task<SavedVacancyDB> SaveAsync(SavedVacancyDB entity, CancellationToken ct = default)
    {
        SavedVacancies.Add(entity);
        return Task.FromResult(entity);
    }

    public Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        DeleteCalls.Add(id);
        int removed = SavedVacancies.RemoveAll(s => (s.Id == id || s.VacancyId == id) && s.UserId == userId);
        return Task.FromResult(removed > 0);
    }

    public Task<bool> ExistsForVacancyAsync(Guid vacancyId, Guid? userId, CancellationToken ct = default)
    {
        ExistsCalls.Add(vacancyId);
        if (userId == null) return Task.FromResult(false);
        bool exists = SavedVacancies.Any(s => s.VacancyId == vacancyId && s.UserId == userId.Value);
        return Task.FromResult(exists);
    }

    public Task<HashSet<Guid>> GetSavedVacancyIdsAsync(IEnumerable<Guid> vacancyIds, Guid userId, CancellationToken ct = default)
    {
        HashSet<Guid> set = vacancyIds.ToHashSet();
        HashSet<Guid> found = SavedVacancies
            .Where(s => s.UserId == userId && set.Contains(s.VacancyId))
            .Select(s => s.VacancyId)
            .ToHashSet();
        return Task.FromResult(found);
    }
}

public sealed class FakeVacancyCacheService : IVacancyCacheService
{
    public Dictionary<string, PagedResult<VacancyDto>> PagedCache { get; } = new();
    public Dictionary<Guid, VacancyDto> DetailCache { get; } = new();
    public PagedResult<VacancyDto>? StaleCatalogToReturn { get; set; }
    public DateTimeOffset? LastCatalogRefreshedAt { get; set; }
    public int InvalidateCatalogCallCount { get; private set; }
    public List<Guid> InvalidateVacancyCalls { get; } = [];
    public long Hits { get; set; }
    public long Misses { get; set; }

    public Task<PagedResult<VacancyDto>?> GetPagedVacanciesAsync(VacancyFilterParameters filter, CancellationToken ct = default)
    {
        string key = $"p={filter.Page}:ps={filter.PageSize}:q={filter.Search}:src={filter.Source}";
        if (PagedCache.TryGetValue(key, out PagedResult<VacancyDto>? result))
        {
            Hits++;
            return Task.FromResult<PagedResult<VacancyDto>?>(result);
        }

        Misses++;
        return Task.FromResult<PagedResult<VacancyDto>?>(null);
    }

    public Task SetPagedVacanciesAsync(VacancyFilterParameters filter, PagedResult<VacancyDto> result, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        string key = $"p={filter.Page}:ps={filter.PageSize}:q={filter.Search}:src={filter.Source}";
        PagedCache[key] = result;
        return Task.CompletedTask;
    }

    public Task<VacancyDto?> GetVacancyByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (DetailCache.TryGetValue(id, out VacancyDto? vac))
        {
            Hits++;
            return Task.FromResult<VacancyDto?>(vac);
        }

        Misses++;
        return Task.FromResult<VacancyDto?>(null);
    }

    public Task SetVacancyByIdAsync(Guid id, VacancyDto vacancy, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        DetailCache[id] = vacancy;
        return Task.CompletedTask;
    }

    public Task InvalidateCatalogAsync(CancellationToken ct = default)
    {
        InvalidateCatalogCallCount++;
        PagedCache.Clear();
        return Task.CompletedTask;
    }

    public Task InvalidateVacancyAsync(Guid id, CancellationToken ct = default)
    {
        InvalidateVacancyCalls.Add(id);
        DetailCache.Remove(id);
        InvalidateCatalogCallCount++;
        PagedCache.Clear();
        return Task.CompletedTask;
    }

    public PagedResult<VacancyDto>? GetStaleCatalog() => StaleCatalogToReturn;

    public (long Hits, long Misses) GetMetrics() => (Hits, Misses);
}

public sealed class FakeSearchQueryRepository : ISearchQueryRepository
{
    public List<SearchQueryDB> Queries { get; } = [];
    public List<string> AddedQueryTexts { get; } = [];

    public Task AddAsync(SearchQueryDB entity, CancellationToken ct = default)
    {
        if (entity.Id == Guid.Empty)
        {
            entity.Id = Guid.NewGuid();
        }

        Queries.Add(entity);
        AddedQueryTexts.Add(entity.QueryText);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SearchQueryDB>> GetRecentAsync(Guid userId, int limit, CancellationToken ct = default)
    {
        IReadOnlyList<SearchQueryDB> items = Queries
            .Where(q => q.UserId == userId)
            .OrderByDescending(q => q.SearchedAt)
            .Take(Math.Max(0, limit))
            .ToList();
        return Task.FromResult(items);
    }

    public Task<SearchQueryDB?> GetLatestAsync(Guid userId, CancellationToken ct = default)
    {
        SearchQueryDB? latest = Queries
            .Where(q => q.UserId == userId)
            .OrderByDescending(q => q.SearchedAt)
            .FirstOrDefault();
        return Task.FromResult(latest);
    }

    public Task<int> GetCountAsync(Guid userId, CancellationToken ct = default)
    {
        return Task.FromResult(Queries.Count(q => q.UserId == userId));
    }

    public Task<bool> DeleteAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        int removed = Queries.RemoveAll(q => q.UserId == userId);
        return Task.FromResult(removed > 0);
    }
}

public sealed class FakeVacancyViewRepository : IVacancyViewRepository
{
    public List<VacancyViewDB> Views { get; } = [];
    public bool RecentViewExistsToReturn { get; set; }

    public Task AddAsync(VacancyViewDB entity, CancellationToken ct = default)
    {
        if (entity.Id == Guid.Empty)
        {
            entity.Id = Guid.NewGuid();
        }

        Views.Add(entity);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsRecentAsync(Guid userId, Guid vacancyId, TimeSpan within, CancellationToken ct = default)
    {
        if (RecentViewExistsToReturn)
        {
            return Task.FromResult(true);
        }

        bool exists = Views.Any(v => v.UserId == userId && v.VacancyId == vacancyId);
        return Task.FromResult(exists);
    }

    public Task<IReadOnlyList<VacancyViewDB>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        IReadOnlyList<VacancyViewDB> items = Views
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.ViewedAt)
            .ToList();
        return Task.FromResult(items);
    }
}

public sealed class FakeUserRepository : IUserRepository
{
    public List<UserDB> Users { get; } = [];

    public Task<UserDB?> GetByEmailAsync(string normalizedEmail, CancellationToken ct = default)
    {
        UserDB? match = Users.FirstOrDefault(u => u.Email.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match);
    }

    public Task<UserDB?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        UserDB? match = Users.FirstOrDefault(u => u.Id == id);
        return Task.FromResult(match);
    }

    public Task<UserDB> CreateAsync(UserDB entity, CancellationToken ct = default)
    {
        Users.Add(entity);
        return Task.FromResult(entity);
    }

    public Task UpdateAsync(UserDB entity, CancellationToken ct = default)
    {
        int index = Users.FindIndex(u => u.Id == entity.Id);
        if (index >= 0)
        {
            Users[index] = entity;
        }

        return Task.CompletedTask;
    }
}
