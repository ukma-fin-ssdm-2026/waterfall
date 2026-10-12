// AI: тестові заглушки для фонового збору вакансій (issue #62); перевірено: запуск dotnet test, самоперевірка автора (§8)

using System.Text.Json;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services;
using Waterfall.Services.Ingestion;
using Waterfall.Services.Models.Common;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Ingestion.Tests;

/// <summary>Годинник з керованим поточним часом.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    public ManualTimeProvider(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Репозиторій у пам'яті. Повертає й зберігає копії, як EF Core з AsNoTracking,
/// щоб тести не проходили випадково через спільні посилання на об'єкти.
/// </summary>
internal sealed class FakeVacancyRepository : IVacancyRepository
{
    private readonly List<VacancyDB> _items = [];

    /// <summary>Збереження вакансії з цим зовнішнім id завершиться винятком, як при збої БД.</summary>
    public string? FailOnExternalId { get; set; }

    public IReadOnlyList<VacancyDB> Items => _items.Select(Copy).ToList();

    public Task<(IReadOnlyList<VacancyDB> Items, int TotalCount)> GetAllAsync(
        string? search = null, string? category = null, string? experience = null, string? location = null,
        string? schedule = null, decimal? minSalary = null, decimal? maxSalary = null, string? source = null,
        bool onlyActive = true, string? sortBy = null, string? sortOrder = null, int page = 1, int pageSize = 10,
        CancellationToken ct = default) =>
        Task.FromResult<(IReadOnlyList<VacancyDB>, int)>((Items, _items.Count));

    public Task<VacancyDB?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_items.Where(v => v.Id == id).Select(Copy).FirstOrDefault());

    public Task<VacancyDB?> GetBySourceAndExternalIdAsync(string source, string externalId, CancellationToken ct = default) =>
        Task.FromResult(_items.Where(v => v.Source == source && v.ExternalId == externalId).Select(Copy).FirstOrDefault());

    public Task<VacancyDB> CreateAsync(VacancyDB entity, CancellationToken ct = default)
    {
        ThrowIfConfiguredToFail(entity);
        _items.Add(Copy(entity));
        return Task.FromResult(entity);
    }

    public Task<VacancyDB?> UpdateAsync(VacancyDB entity, CancellationToken ct = default)
    {
        ThrowIfConfiguredToFail(entity);
        int index = _items.FindIndex(v => v.Id == entity.Id);
        if (index < 0)
        {
            return Task.FromResult<VacancyDB?>(null);
        }

        _items[index] = Copy(entity);
        return Task.FromResult<VacancyDB?>(entity);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_items.RemoveAll(v => v.Id == id) > 0);

    private void ThrowIfConfiguredToFail(VacancyDB entity)
    {
        if (FailOnExternalId is not null && entity.ExternalId == FailOnExternalId)
        {
            throw new InvalidOperationException("Імітація збою БД");
        }
    }

    private static VacancyDB Copy(VacancyDB entity) =>
        JsonSerializer.Deserialize<VacancyDB>(JsonSerializer.Serialize(entity))!;
}

/// <summary>Кеш, що лише рахує скидання.</summary>
internal sealed class FakeVacancyCacheService : IVacancyCacheService
{
    public int CatalogInvalidations { get; private set; }

    public List<Guid> InvalidatedVacancies { get; } = [];

    public DateTimeOffset? LastCatalogRefreshedAt => null;

    public Task<PagedResult<VacancyDto>?> GetPagedVacanciesAsync(VacancyFilterParameters filter, CancellationToken ct = default) =>
        Task.FromResult<PagedResult<VacancyDto>?>(null);

    public Task SetPagedVacanciesAsync(VacancyFilterParameters filter, PagedResult<VacancyDto> result, TimeSpan? ttl = null, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<VacancyDto?> GetVacancyByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult<VacancyDto?>(null);

    public Task SetVacancyByIdAsync(Guid id, VacancyDto vacancy, TimeSpan? ttl = null, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task InvalidateCatalogAsync(CancellationToken ct = default)
    {
        CatalogInvalidations++;
        return Task.CompletedTask;
    }

    public Task InvalidateVacancyAsync(Guid id, CancellationToken ct = default)
    {
        InvalidatedVacancies.Add(id);
        return Task.CompletedTask;
    }

    public PagedResult<VacancyDto>? GetStaleCatalog() => null;

    public (long Hits, long Misses) GetMetrics() => (0, 0);
}

/// <summary>Джерело, що віддає заздалегідь підготовлені документи.</summary>
internal sealed class InMemoryRawSource : IRawVacancySource
{
    private readonly List<RawVacancyDocument> _documents;

    public InMemoryRawSource(params RawVacancyDocument[] documents) => _documents = [.. documents];

    public Task<IReadOnlyList<RawVacancyDocument>> ReadDocumentsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RawVacancyDocument>>(_documents);
}

/// <summary>Побудова сирих даних для тестів.</summary>
internal static class RawData
{
    public static string DjinniDocument(params object[] jobs) =>
        JsonSerializer.Serialize(new { source = "djinni", jobs });

    public static object Job(string id, string title = "Senior .NET Developer", string? url = null) => new
    {
        id,
        url = url ?? $"https://djinni.co/jobs/{id}-developer/",
        title,
        company = "Kvitka Software",
        salary = "$3500-4500",
        location = "Kyiv, Remote",
        posted = "2 дні тому",
        experience = "5 років досвіду",
        employment = "Full Remote",
        category = ".NET",
        descriptionHtml = "<p>Розробка платіжного сервісу на .NET.</p>",
        tags = "C#, .NET; PostgreSQL"
    };

    public static RawVacancy Raw(
        string? title = "Senior .NET Developer",
        string? url = "https://djinni.co/jobs/720001-senior-net-developer/",
        string? id = "720001",
        string? description = "<p>Опис вакансії</p>",
        string source = "djinni",
        string? category = ".NET",
        string? salary = "$3500-4500",
        string? posted = "2 дні тому",
        string? experience = "5 років досвіду",
        string? tags = "C#, .NET",
        string? location = "Kyiv, Remote") => new()
        {
            Source = source,
            ExternalId = id,
            Url = url,
            Title = title,
            Company = "Kvitka Software",
            Salary = salary,
            Location = location,
            Posted = posted,
            Experience = experience,
            Employment = "Full Remote",
            Category = category,
            DescriptionHtml = description,
            Tags = tags
        };
}
