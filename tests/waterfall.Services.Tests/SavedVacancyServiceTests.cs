// AI: модульні тести сервісу закладок (SavedVacancyService, інвалідація кешу, Lab 06); перевірено: запуск dotnet test, самоперевірка автора (Денис Сидоренко, §8)

using Waterfall.Repository.Entities;
using Waterfall.Services.Models.SavedVacancies;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Tests;

/// <summary>
/// Модульні тести для SavedVacancyService:
/// - Управління закладками користувача («лайки»)
/// - Автоматична інвалідація кешу вакансії при додаванні/видаленні закладок
/// - Мапінг сутностей у SavedVacancyDto
/// </summary>
public sealed class SavedVacancyServiceTests
{
    private readonly FakeSavedVacancyRepository _savedRepo = new();
    private readonly FakeVacancyCacheService _cacheService = new();
    private readonly FakeLogger<SavedVacancyService> _logger = new();
    private readonly SavedVacancyService _service;

    public SavedVacancyServiceTests()
    {
        _service = new SavedVacancyService(_savedRepo, _cacheService, _logger);
    }

    private static VacancyDB CreateVacancy(string title = "Lead Architect")
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new VacancyDB
        {
            Id = Guid.NewGuid(),
            ExternalId = "ext-saved",
            Title = title,
            Company = "ArcCo",
            Description = "Design systems",
            Category = "Architecture",
            Location = "Kyiv",
            Currency = "USD",
            Source = "Djinni",
            SourceUrl = "https://example.com/1",
            SkillsJson = "[\"Architecture\"]",
            PublishedAt = now,
            FirstSeenAt = now,
            LastSeenAt = now,
            IsActive = true
        };
    }

    [Fact]
    public async Task GetAllAsync_ReturnsMappedDtosWithNestedVacancy()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        VacancyDB vac = CreateVacancy("Cloud Architect");
        SavedVacancyDB entity = new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            VacancyId = vac.Id,
            Vacancy = vac,
            Notes = "Important note",
            SavedAt = DateTimeOffset.UtcNow
        };

        _savedRepo.SavedVacancies.Add(entity);

        // Act
        IReadOnlyList<SavedVacancyDto> results = await _service.GetAllAsync(userId);

        // Assert
        Assert.Single(results);
        Assert.Equal(entity.Id, results[0].Id);
        Assert.Equal(vac.Id, results[0].VacancyId);
        Assert.Equal("Important note", results[0].Notes);
        Assert.NotNull(results[0].Vacancy);
        Assert.Equal("Cloud Architect", results[0].Vacancy!.Title);
        Assert.True(results[0].Vacancy!.IsSaved);
    }

    [Fact]
    public async Task GetByIdAsync_Found_ReturnsMappedDto()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid savedId = Guid.NewGuid();
        SavedVacancyDB entity = new()
        {
            Id = savedId,
            UserId = userId,
            VacancyId = Guid.NewGuid(),
            Notes = "Interview scheduled",
            SavedAt = DateTimeOffset.UtcNow
        };
        _savedRepo.SavedVacancies.Add(entity);

        // Act
        SavedVacancyDto? result = await _service.GetByIdAsync(savedId, userId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(savedId, result.Id);
        Assert.Equal("Interview scheduled", result.Notes);
    }

    [Fact]
    public async Task SaveAsync_SavesRecord_InvalidatesVacancyCache_AndReturnsDto()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid vacancyId = Guid.NewGuid();
        SaveVacancyRequest request = new()
        {
            VacancyId = vacancyId,
            Notes = "  Follow up next Monday  "
        };

        // Act
        SavedVacancyDto result = await _service.SaveAsync(request, userId);

        // Assert
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(vacancyId, result.VacancyId);
        Assert.Equal("Follow up next Monday", result.Notes);
        Assert.Single(_savedRepo.SavedVacancies);

        // Cache for this vacancy must be invalidated
        Assert.Contains(vacancyId, _cacheService.InvalidateVacancyCalls);
    }

    [Fact]
    public async Task DeleteAsync_Existing_RemovesRecord_InvalidatesVacancyCache_AndReturnsTrue()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid savedId = Guid.NewGuid();
        Guid vacancyId = Guid.NewGuid();

        SavedVacancyDB entity = new()
        {
            Id = savedId,
            UserId = userId,
            VacancyId = vacancyId
        };
        _savedRepo.SavedVacancies.Add(entity);

        // Act
        bool result = await _service.DeleteAsync(savedId, userId);

        // Assert
        Assert.True(result);
        Assert.Contains(savedId, _savedRepo.DeleteCalls);
        Assert.Contains(vacancyId, _cacheService.InvalidateVacancyCalls);
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_ReturnsFalse_AndDoesNotInvalidateCache()
    {
        // Arrange & Act
        bool result = await _service.DeleteAsync(Guid.NewGuid(), Guid.NewGuid());

        // Assert
        Assert.False(result);
        Assert.Empty(_cacheService.InvalidateVacancyCalls);
    }
}
