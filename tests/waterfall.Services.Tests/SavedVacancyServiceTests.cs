// AI: модульні тести сервісу закладок (SavedVacancyService, інвалідація кешу, Lab 06); перевірено: запуск dotnet test, самоперевірка автора (Денис Сидоренко, §8)

using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Entities;
using Waterfall.Services.Exceptions;
using Waterfall.Services.Models.SavedVacancies;

namespace Waterfall.Services.Tests;

/// <summary>
/// Модульні тести для SavedVacancyService:
/// - Управління закладками користувача («лайки»)
/// - Автоматична інвалідація кешу вакансії при додаванні/видаленні закладок
/// - Мапінг сутностей у SavedVacancyDto (Id == VacancyId) та перетворення дублікатів на DuplicateSavedVacancyException
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
    public async Task GetAllAsync_ReturnsMappedDtosWithNestedVacancyAndVacancyIdAsId()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        VacancyDB vac = CreateVacancy("Cloud Architect");
        SavedVacancyDB entity = new()
        {
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
        Assert.Equal(vac.Id, results[0].Id);
        Assert.Equal(vac.Id, results[0].VacancyId);
        Assert.Equal("Important note", results[0].Notes);
        Assert.NotNull(results[0].Vacancy);
        Assert.Equal("Cloud Architect", results[0].Vacancy!.Title);
        Assert.True(results[0].Vacancy!.IsSaved);
    }

    [Fact]
    public async Task GetByIdAsync_Found_ReturnsMappedDtoWithVacancyIdAsId()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid vacancyId = Guid.NewGuid();
        SavedVacancyDB entity = new()
        {
            UserId = userId,
            VacancyId = vacancyId,
            Notes = "Interview scheduled",
            SavedAt = DateTimeOffset.UtcNow
        };
        _savedRepo.SavedVacancies.Add(entity);

        // Act
        SavedVacancyDto? result = await _service.GetByIdAsync(vacancyId, userId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(vacancyId, result.Id);
        Assert.Equal(vacancyId, result.VacancyId);
        Assert.Equal("Interview scheduled", result.Notes);
    }

    [Fact]
    public async Task SaveAsync_SavesRecord_InvalidatesVacancyCache_AndReturnsDtoWithVacancyId()
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
        Assert.Equal(vacancyId, result.Id);
        Assert.Equal(vacancyId, result.VacancyId);
        Assert.Equal("Follow up next Monday", result.Notes);
        Assert.Single(_savedRepo.SavedVacancies);

        // Cache for this vacancy must be invalidated
        Assert.Contains(vacancyId, _cacheService.InvalidateVacancyCalls);
    }

    [Fact]
    public async Task SaveAsync_WhenDuplicateBookmarkExists_ThrowsDuplicateSavedVacancyException()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid vacancyId = Guid.NewGuid();
        SaveVacancyRequest request = new() { VacancyId = vacancyId };
        await _service.SaveAsync(request, userId);

        // Act & Assert
        DuplicateSavedVacancyException ex = await Assert.ThrowsAsync<DuplicateSavedVacancyException>(
            () => _service.SaveAsync(request, userId));

        Assert.Equal(vacancyId, ex.VacancyId);
        Assert.Single(_savedRepo.SavedVacancies);
    }

    [Fact]
    public async Task SaveAsync_WhenNonDuplicateDbUpdateExceptionOccurs_RethrowsDbUpdateException()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid vacancyId = Guid.NewGuid();
        var repo = new ForeignKeyFailingSavedVacancyRepository();
        var service = new SavedVacancyService(repo, _cacheService, _logger);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(
            () => service.SaveAsync(new SaveVacancyRequest { VacancyId = vacancyId }, userId));
    }

    [Fact]
    public async Task DeleteAsync_Existing_RemovesRecord_InvalidatesVacancyCache_AndReturnsTrue()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        Guid vacancyId = Guid.NewGuid();

        SavedVacancyDB entity = new()
        {
            UserId = userId,
            VacancyId = vacancyId
        };
        _savedRepo.SavedVacancies.Add(entity);

        // Act
        bool result = await _service.DeleteAsync(vacancyId, userId);

        // Assert
        Assert.True(result);
        Assert.Empty(_savedRepo.SavedVacancies);
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

    [Fact]
    public async Task SaveAsync_ThrowsArgumentNullException_WhenRequestIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.SaveAsync(null!, Guid.NewGuid()));
    }

    [Fact]
    public async Task SaveAsync_ThrowsArgumentException_WhenVacancyIdOrUserIdIsEmpty()
    {
        SaveVacancyRequest emptyVacancy = new() { VacancyId = Guid.Empty };
        SaveVacancyRequest validVacancy = new() { VacancyId = Guid.NewGuid() };

        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveAsync(emptyVacancy, Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveAsync(validVacancy, Guid.Empty));
    }

    [Fact]
    public async Task DeleteAsync_ThrowsArgumentException_WhenIdsAreEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DeleteAsync(Guid.Empty, Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DeleteAsync(Guid.NewGuid(), Guid.Empty));
    }

    private sealed class ForeignKeyFailingSavedVacancyRepository : Waterfall.Repository.Interfaces.ISavedVacancyRepository
    {
        public Task<IReadOnlyList<SavedVacancyDB>> GetAllAsync(Guid userId, bool onlyActive = true, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SavedVacancyDB>>([]);

        public Task<SavedVacancyDB?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<SavedVacancyDB?>(null);

        public Task<SavedVacancyDB> SaveAsync(SavedVacancyDB entity, CancellationToken ct = default) =>
            throw new DbUpdateException("FK error", new InvalidOperationException("FOREIGN KEY constraint failed"));

        public Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<bool> ExistsForVacancyAsync(Guid vacancyId, Guid? userId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<HashSet<Guid>> GetSavedVacancyIdsAsync(IEnumerable<Guid> vacancyIds, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(new HashSet<Guid>());
    }
}
