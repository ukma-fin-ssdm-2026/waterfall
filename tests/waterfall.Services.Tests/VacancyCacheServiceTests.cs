// AI: модульні тести сервісу кешування (VacancyCacheService, Cache-Aside, Stale-Cache, Lab 06); перевірено: запуск dotnet test, самоперевірка автора (Денис Сидоренко, §8)

using Microsoft.Extensions.Caching.Memory;
using Waterfall.Services.Models.Common;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Tests;

/// <summary>
/// Модульні тести для VacancyCacheService (Cache-Aside, інвалідація та Stale-Cache режим).
/// Працює зі справжнім екземпляром IMemoryCache.
/// </summary>
public sealed class VacancyCacheServiceTests : IDisposable
{
    private readonly IMemoryCache _memoryCache;
    private readonly VacancyCacheService _cacheService;

    public VacancyCacheServiceTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _cacheService = new VacancyCacheService(_memoryCache);
    }

    public void Dispose()
    {
        _memoryCache.Dispose();
    }

    private static VacancyDto CreateSampleDto(string title = ".NET Senior Dev", Guid? id = null)
    {
        return new VacancyDto
        {
            Id = id ?? Guid.NewGuid(),
            Title = title,
            Company = "BigTech",
            Description = "Exciting opportunity",
            Category = ".NET",
            Location = "Kyiv",
            SalaryMin = 4000,
            SalaryMax = 6000,
            Currency = "USD",
            Source = JobSource.Djinni,
            SourceUrl = "https://djinni.co/jobs/1",
            PublishedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };
    }

    [Fact]
    public async Task GetPagedVacanciesAsync_OnCacheMiss_IncrementsMissesAndReturnsNull()
    {
        // Arrange
        VacancyFilterParameters filter = new() { Page = 1, PageSize = 10 };

        // Act
        PagedResult<VacancyDto>? result = await _cacheService.GetPagedVacanciesAsync(filter);
        (long hits, long misses) = _cacheService.GetMetrics();

        // Assert
        Assert.Null(result);
        Assert.Equal(0, hits);
        Assert.Equal(1, misses);
    }

    [Fact]
    public async Task SetPagedVacanciesAsync_ThenGet_ReturnsCachedAndIncrementsHits()
    {
        // Arrange
        VacancyFilterParameters filter = new() { Page = 1, PageSize = 10, Search = "developer" };
        VacancyDto sample = CreateSampleDto();
        PagedResult<VacancyDto> paged = new()
        {
            Items = [sample],
            TotalCount = 1,
            Page = 1,
            PageSize = 10
        };

        // Act
        await _cacheService.SetPagedVacanciesAsync(filter, paged);
        PagedResult<VacancyDto>? retrieved = await _cacheService.GetPagedVacanciesAsync(filter);
        (long hits, long misses) = _cacheService.GetMetrics();

        // Assert
        Assert.NotNull(retrieved);
        Assert.Single(retrieved.Items);
        Assert.Equal(sample.Id, retrieved.Items[0].Id);
        Assert.Equal(1, hits);
        Assert.Equal(0, misses);
    }

    [Fact]
    public async Task SetPagedVacanciesAsync_DefaultCatalogPage1_CapturesStaleCatalog()
    {
        // Arrange
        VacancyFilterParameters defaultFilter = new()
        {
            Page = 1,
            PageSize = 20,
            Search = null,
            Category = null,
            OnlyActive = true
        };
        PagedResult<VacancyDto> paged = new()
        {
            Items = [CreateSampleDto()],
            TotalCount = 1,
            Page = 1,
            PageSize = 20
        };

        // Act
        await _cacheService.SetPagedVacanciesAsync(defaultFilter, paged);
        PagedResult<VacancyDto>? stale = _cacheService.GetStaleCatalog();

        // Assert
        Assert.NotNull(stale);
        Assert.Single(stale.Items);
        Assert.NotNull(_cacheService.LastCatalogRefreshedAt);
        Assert.True(_cacheService.LastCatalogRefreshedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task SetPagedVacanciesAsync_FilteredSearch_DoesNotOverwriteStaleCatalog()
    {
        // Arrange
        VacancyFilterParameters searchFilter = new()
        {
            Page = 1,
            PageSize = 20,
            Search = "Java",
            OnlyActive = true
        };
        PagedResult<VacancyDto> paged = new()
        {
            Items = [CreateSampleDto("Java Developer")],
            TotalCount = 1,
            Page = 1,
            PageSize = 20
        };

        // Act
        await _cacheService.SetPagedVacanciesAsync(searchFilter, paged);
        PagedResult<VacancyDto>? stale = _cacheService.GetStaleCatalog();

        // Assert - search results must not replace the baseline catalog for Stale-Cache (REL-001)
        Assert.Null(stale);
    }

    [Fact]
    public async Task InvalidateCatalogAsync_EvictsPagedVacancies()
    {
        // Arrange
        VacancyFilterParameters filter = new() { Page = 1, PageSize = 10 };
        PagedResult<VacancyDto> paged = new()
        {
            Items = [CreateSampleDto()],
            TotalCount = 1,
            Page = 1,
            PageSize = 10
        };

        await _cacheService.SetPagedVacanciesAsync(filter, paged);

        // Act
        await _cacheService.InvalidateCatalogAsync();
        PagedResult<VacancyDto>? afterInvalidation = await _cacheService.GetPagedVacanciesAsync(filter);

        // Assert
        Assert.Null(afterInvalidation);
    }

    [Fact]
    public async Task GetVacancyByIdAsync_MissThenHit_TracksMetricsAccurately()
    {
        // Arrange
        Guid id = Guid.NewGuid();
        VacancyDto dto = CreateSampleDto(id: id);

        // Act 1: Miss
        VacancyDto? miss = await _cacheService.GetVacancyByIdAsync(id);
        Assert.Null(miss);

        // Act 2: Set
        await _cacheService.SetVacancyByIdAsync(id, dto);

        // Act 3: Hit
        VacancyDto? hit = await _cacheService.GetVacancyByIdAsync(id);
        Assert.NotNull(hit);
        Assert.Equal(id, hit.Id);

        (long hits, long misses) = _cacheService.GetMetrics();
        Assert.Equal(1, hits);
        Assert.Equal(1, misses);
    }

    [Fact]
    public async Task InvalidateVacancyAsync_EvictsBothVacancyDetailAndCatalogPages()
    {
        // Arrange
        Guid id = Guid.NewGuid();
        VacancyDto dto = CreateSampleDto(id: id);

        VacancyFilterParameters filter = new() { Page = 1, PageSize = 10 };
        PagedResult<VacancyDto> paged = new()
        {
            Items = [dto],
            TotalCount = 1,
            Page = 1,
            PageSize = 10
        };

        await _cacheService.SetVacancyByIdAsync(id, dto);
        await _cacheService.SetPagedVacanciesAsync(filter, paged);

        // Act
        await _cacheService.InvalidateVacancyAsync(id);

        VacancyDto? detailAfter = await _cacheService.GetVacancyByIdAsync(id);
        PagedResult<VacancyDto>? pagedAfter = await _cacheService.GetPagedVacanciesAsync(filter);

        // Assert
        Assert.Null(detailAfter);
        Assert.Null(pagedAfter);
    }
}
