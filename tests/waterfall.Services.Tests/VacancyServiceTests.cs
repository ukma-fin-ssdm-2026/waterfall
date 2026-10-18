// AI: модульні тести бізнес-сервісу вакансій (VacancyService, REL-001, PERF-001, Lab 06); перевірено: запуск dotnet test, самоперевірка автора (Денис Сидоренко, §8)

using Waterfall.Repository.Entities;
using Waterfall.Services.Models.Common;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Tests;

/// <summary>
/// Модульні тести для VacancyService:
/// - Патерн Cache-Aside (PERF-001)
/// - Режим Stale-Cache при збої БД (REL-001)
/// - Ізоляція персонального стану IsSaved між користувачами
/// - CRUD операції та мапінг контрактів
/// </summary>
public sealed class VacancyServiceTests
{
    private readonly FakeVacancyRepository _vacancyRepo = new();
    private readonly FakeSavedVacancyRepository _savedRepo = new();
    private readonly FakeVacancyCacheService _cacheService = new();
    private readonly FakeLogger<VacancyService> _logger = new();
    private readonly VacancyService _service;

    public VacancyServiceTests()
    {
        _service = new VacancyService(_vacancyRepo, _savedRepo, _cacheService, _logger);
    }

    private static VacancyDB CreateEntity(
        string title = "Backend Engineer",
        string source = "Djinni",
        string? skillsJson = "[\"C#\", \".NET\"]")
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new VacancyDB
        {
            Id = Guid.NewGuid(),
            ExternalId = "ext-1",
            Title = title,
            Company = "TechCo",
            Description = "Great team",
            Category = "Backend",
            Location = "Kyiv",
            SalaryMin = 2500,
            SalaryMax = 4000,
            Currency = "USD",
            Source = source,
            SourceUrl = "https://example.com/job/1",
            SkillsJson = skillsJson ?? "[]",
            PublishedAt = now,
            FirstSeenAt = now,
            LastSeenAt = now,
            IsActive = true
        };
    }

    [Fact]
    public async Task GetAllAsync_AnonymousUser_OnCacheHit_ReturnsCachedWithoutRepoCall()
    {
        // Arrange
        VacancyFilterParameters filter = new() { Page = 1, PageSize = 10 };
        VacancyDto cachedDto = new()
        {
            Id = Guid.NewGuid(),
            Title = "Cached Vacancy",
            Company = "CacheCorp",
            Description = "From cache",
            Category = ".NET",
            Location = "Kyiv",
            Currency = "USD",
            PublishedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };
        PagedResult<VacancyDto> cachedResult = new()
        {
            Items = [cachedDto],
            TotalCount = 1,
            Page = 1,
            PageSize = 10
        };

        await _cacheService.SetPagedVacanciesAsync(filter, cachedResult);

        // Act
        PagedResult<VacancyDto> result = await _service.GetAllAsync(filter, userId: null);

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("Cached Vacancy", result.Items[0].Title);
        Assert.Empty(_vacancyRepo.GetAllSearchCalls); // Repository was never queried
    }

    [Fact]
    public async Task GetAllAsync_AnonymousUser_OnCacheMiss_QueriesRepoAndStoresInCache()
    {
        // Arrange
        VacancyFilterParameters filter = new() { Page = 1, PageSize = 10, Search = "Engineer" };
        VacancyDB entity = CreateEntity("Software Engineer");
        _vacancyRepo.Vacancies.Add(entity);

        // Act
        PagedResult<VacancyDto> result = await _service.GetAllAsync(filter, userId: null);

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("Software Engineer", result.Items[0].Title);
        Assert.False(result.Items[0].IsSaved);

        // Verify result was stored in cache
        PagedResult<VacancyDto>? cached = await _cacheService.GetPagedVacanciesAsync(filter);
        Assert.NotNull(cached);
        Assert.Single(cached.Items);
        Assert.Equal(entity.Id, cached.Items[0].Id);
    }

    [Fact]
    public async Task GetAllAsync_AuthenticatedUser_BypassesCacheAndEnrichesIsSavedFlags()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        VacancyFilterParameters filter = new() { Page = 1, PageSize = 10 };

        VacancyDB vac1 = CreateEntity("Vacancy 1");
        VacancyDB vac2 = CreateEntity("Vacancy 2");
        _vacancyRepo.Vacancies.AddRange(vac1, vac2);

        // User has saved vac1
        _savedRepo.SavedVacancies.Add(new SavedVacancyDB
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            VacancyId = vac1.Id
        });

        // Act
        PagedResult<VacancyDto> result = await _service.GetAllAsync(filter, userId: userId);

        // Assert
        Assert.Equal(2, result.Items.Count);

        VacancyDto? item1 = result.Items.FirstOrDefault(i => i.Id == vac1.Id);
        VacancyDto? item2 = result.Items.FirstOrDefault(i => i.Id == vac2.Id);

        Assert.NotNull(item1);
        Assert.True(item1.IsSaved);

        Assert.NotNull(item2);
        Assert.False(item2.IsSaved);

        // Cache must NOT be populated with authenticated personal flags
        Assert.Empty(_cacheService.PagedCache);
    }

    [Fact]
    public async Task GetAllAsync_DatabaseFailure_ReturnsStaleCatalogIfAvailable()
    {
        // Arrange
        VacancyFilterParameters filter = new() { Page = 1, PageSize = 10 };
        _vacancyRepo.ShouldThrowOnGetAll = true;

        PagedResult<VacancyDto> stale = new()
        {
            Items = [new VacancyDto { Id = Guid.NewGuid(), Title = "Stale Fallback Vacancy", Company = "StaleCo", Description = "Desc", Category = "IT", Location = "UA", Currency = "USD", PublishedAt = DateTimeOffset.UtcNow }],
            TotalCount = 1,
            Page = 1,
            PageSize = 10
        };
        _cacheService.StaleCatalogToReturn = stale;

        // Act
        PagedResult<VacancyDto> result = await _service.GetAllAsync(filter, userId: null);

        // Assert - REL-001 resilience fallback
        Assert.NotNull(result);
        Assert.Single(result.Items);
        Assert.Equal("Stale Fallback Vacancy", result.Items[0].Title);
        Assert.Contains(_logger.Logs, l => l.Contains("Stale-Cache"));
    }

    [Fact]
    public async Task GetAllAsync_DatabaseFailure_RethrowsIfStaleCatalogUnavailable()
    {
        // Arrange
        VacancyFilterParameters filter = new() { Page = 1, PageSize = 10 };
        _vacancyRepo.ShouldThrowOnGetAll = true;
        _cacheService.StaleCatalogToReturn = null;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetAllAsync(filter, userId: null));
    }

    [Fact]
    public async Task GetAllAsync_SourceFilter_TranslatesEnumCorrectly()
    {
        // Arrange
        VacancyFilterParameters filterWithDjinni = new() { Source = JobSource.Djinni };
        VacancyFilterParameters filterWithUnknown = new() { Source = JobSource.Unknown };

        // Act
        await _service.GetAllAsync(filterWithDjinni);
        await _service.GetAllAsync(filterWithUnknown);

        // Assert - verify Djinni passes string "Djinni" to repo, but Unknown translates to null (no filter)
        Assert.Equal(2, _vacancyRepo.GetAllSourceCalls.Count);
        Assert.Equal("Djinni", _vacancyRepo.GetAllSourceCalls[0]);
        Assert.Null(_vacancyRepo.GetAllSourceCalls[1]);
    }

    [Fact]
    public async Task GetByIdAsync_AnonymousUser_ReturnsCachedOrQueriesRepo()
    {
        // Arrange
        Guid id = Guid.NewGuid();
        VacancyDB entity = CreateEntity("Details Engineer");
        entity.Id = id;
        _vacancyRepo.Vacancies.Add(entity);

        // Act 1: Miss queries repo and caches
        VacancyDto? result1 = await _service.GetByIdAsync(id, userId: null);

        Assert.NotNull(result1);
        Assert.Equal("Details Engineer", result1.Title);
        Assert.False(result1.IsSaved);

        // Verify stored in detail cache
        VacancyDto? cached = await _cacheService.GetVacancyByIdAsync(id);
        Assert.NotNull(cached);

        // Act 2: Hit returns from cache even if deleted from repo
        _vacancyRepo.Vacancies.Clear();
        VacancyDto? result2 = await _service.GetByIdAsync(id, userId: null);
        Assert.NotNull(result2);
        Assert.Equal("Details Engineer", result2.Title);
    }

    [Fact]
    public async Task GetByIdAsync_AuthenticatedUser_ChecksExistsAndDoesNotCache()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid id = Guid.NewGuid();
        VacancyDB entity = CreateEntity("Saved Detail");
        entity.Id = id;
        _vacancyRepo.Vacancies.Add(entity);

        _savedRepo.SavedVacancies.Add(new SavedVacancyDB
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            VacancyId = id
        });

        // Act
        VacancyDto? result = await _service.GetByIdAsync(id, userId: userId);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSaved);
        Assert.Empty(_cacheService.DetailCache); // Must not cache personal DTO
    }

    [Fact]
    public async Task CreateAsync_TrimsFields_SavesToRepo_AndInvalidatesCatalog()
    {
        // Arrange
        CreateVacancyRequest request = new()
        {
            Title = "  Fullstack Developer  ",
            Company = "  Acme Corp  ",
            Description = "  Great role  ",
            Category = "  Web  ",
            Location = "  Kyiv  ",
            Currency = "usd",
            Source = JobSource.Dou,
            SourceUrl = "  https://jobs.dou.ua/1  ",
            Skills = ["React", "Node.js"]
        };

        // Act
        VacancyDto result = await _service.CreateAsync(request);

        // Assert
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Fullstack Developer", result.Title);
        Assert.Equal("Acme Corp", result.Company);
        Assert.Equal("USD", result.Currency);
        Assert.Equal(JobSource.Dou, result.Source);
        Assert.Equal(2, result.Skills.Count);
        Assert.Equal(1, _cacheService.InvalidateCatalogCallCount);
        Assert.Single(_vacancyRepo.CreatedVacancies);
    }

    [Fact]
    public async Task UpdateAsync_ExistingVacancy_UpdatesFields_AndInvalidatesVacancyCache()
    {
        // Arrange
        Guid id = Guid.NewGuid();
        VacancyDB entity = CreateEntity("Old Title");
        entity.Id = id;
        _vacancyRepo.Vacancies.Add(entity);

        UpdateVacancyRequest request = new()
        {
            Title = "Updated Title",
            SalaryMin = 5000,
            Skills = ["Golang", "Kubernetes"]
        };

        // Act
        VacancyDto? updated = await _service.UpdateAsync(id, request);

        // Assert
        Assert.NotNull(updated);
        Assert.Equal("Updated Title", updated.Title);
        Assert.Equal(5000, updated.SalaryMin);
        Assert.Contains(id, _cacheService.InvalidateVacancyCalls);
        Assert.Single(_vacancyRepo.UpdatedVacancies);
    }

    [Fact]
    public async Task UpdateAsync_NonExistentVacancy_ReturnsNull()
    {
        // Arrange & Act
        VacancyDto? result = await _service.UpdateAsync(Guid.NewGuid(), new UpdateVacancyRequest());

        // Assert
        Assert.Null(result);
        Assert.Empty(_cacheService.InvalidateVacancyCalls);
    }

    [Fact]
    public async Task DeleteAsync_ExistingVacancy_DeletesFromRepo_AndInvalidatesCache()
    {
        // Arrange
        Guid id = Guid.NewGuid();
        VacancyDB entity = CreateEntity();
        entity.Id = id;
        _vacancyRepo.Vacancies.Add(entity);

        // Act
        bool result = await _service.DeleteAsync(id);

        // Assert
        Assert.True(result);
        Assert.Contains(id, _vacancyRepo.DeletedIds);
        Assert.Contains(id, _cacheService.InvalidateVacancyCalls);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentVacancy_ReturnsFalse_AndDoesNotInvalidateCache()
    {
        // Arrange & Act
        bool result = await _service.DeleteAsync(Guid.NewGuid());

        // Assert
        Assert.False(result);
        Assert.Empty(_cacheService.InvalidateVacancyCalls);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("invalid json {[", 0)]
    [InlineData("[\"C#\", \"Azure\"]", 2)]
    public void MapToDto_HandlesSkillsJsonRobustly(string? json, int expectedCount)
    {
        // Arrange
        VacancyDB entity = CreateEntity("Skill Test", skillsJson: json);

        // Act
        VacancyDto dto = VacancyService.MapToDto(entity);

        // Assert
        Assert.NotNull(dto.Skills);
        Assert.Equal(expectedCount, dto.Skills.Count);
    }

    [Theory]
    [InlineData("Djinni", JobSource.Djinni)]
    [InlineData("WorkUa", JobSource.WorkUa)]
    [InlineData("RobotaUa", JobSource.RobotaUa)]
    [InlineData("DOU", JobSource.Dou)]
    [InlineData("NonExistentPortal", JobSource.Unknown)]
    public void MapToDto_ParsesJobSourceCorrectly(string source, JobSource expectedSource)
    {
        // Arrange
        VacancyDB entity = CreateEntity(source: source);

        // Act
        VacancyDto dto = VacancyService.MapToDto(entity);

        // Assert
        Assert.Equal(expectedSource, dto.Source);
    }
}
