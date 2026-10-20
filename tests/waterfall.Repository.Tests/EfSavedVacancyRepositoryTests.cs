// AI: модульні тести репозиторію збережених вакансій (EfSavedVacancyRepository, Lab 06); перевірено: запуск dotnet test, самоперевірка автора (Денис Сидоренко, §8)

using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Repositories;

namespace Waterfall.Repository.Tests;

/// <summary>
/// Модульні тести для EfSavedVacancyRepository («лайки» / закладки)
/// на базі ізольованої in-memory SQLite.
/// </summary>
public sealed class EfSavedVacancyRepositoryTests : IDisposable
{
    private readonly SqliteTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static VacancyDB CreateVacancy(string title = "Backend Engineer", bool isActive = true)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new VacancyDB
        {
            Id = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = title,
            Company = "TopCompany",
            Description = "Great job opportunity",
            Category = "Backend",
            Location = "Kyiv",
            SalaryMin = 2000,
            SalaryMax = 3000,
            Currency = "USD",
            Source = "Djinni",
            SourceUrl = "https://example.com/job",
            SkillsJson = "[\"C#\"]",
            PublishedAt = now,
            FirstSeenAt = now,
            LastSeenAt = now,
            IsActive = isActive
        };
    }

    [Fact]
    public async Task GetAllAsync_OnlyActiveTrue_ReturnsOnlyActiveSavedVacanciesForUser()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfSavedVacancyRepository repo = new(db);

        Guid user1 = Guid.NewGuid();
        Guid user2 = Guid.NewGuid();

        VacancyDB activeVac = CreateVacancy("Active Vacancy", isActive: true);
        VacancyDB inactiveVac = CreateVacancy("Inactive Vacancy", isActive: false);
        db.Vacancies.AddRange(activeVac, inactiveVac);

        SavedVacancyDB savedActive = new()
        {
            Id = Guid.NewGuid(),
            UserId = user1,
            VacancyId = activeVac.Id,
            SavedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        SavedVacancyDB savedInactive = new()
        {
            Id = Guid.NewGuid(),
            UserId = user1,
            VacancyId = inactiveVac.Id,
            SavedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        SavedVacancyDB otherUserSaved = new()
        {
            Id = Guid.NewGuid(),
            UserId = user2,
            VacancyId = activeVac.Id,
            SavedAt = DateTimeOffset.UtcNow
        };

        db.SavedVacancies.AddRange(savedActive, savedInactive, otherUserSaved);
        await db.SaveChangesAsync();

        // Act
        IReadOnlyList<SavedVacancyDB> results = await repo.GetAllAsync(user1, onlyActive: true);

        // Assert
        Assert.Single(results);
        Assert.Equal(savedActive.Id, results[0].Id);
        Assert.NotNull(results[0].Vacancy);
        Assert.Equal("Active Vacancy", results[0].Vacancy!.Title);
    }

    [Fact]
    public async Task GetAllAsync_OnlyActiveFalse_IncludesBothActiveAndInactiveSavedVacancies()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfSavedVacancyRepository repo = new(db);

        Guid user = Guid.NewGuid();

        VacancyDB active = CreateVacancy("Active", isActive: true);
        VacancyDB inactive = CreateVacancy("Archived", isActive: false);
        db.Vacancies.AddRange(active, inactive);

        db.SavedVacancies.AddRange(
            new SavedVacancyDB { Id = Guid.NewGuid(), UserId = user, VacancyId = active.Id, SavedAt = DateTimeOffset.UtcNow.AddMinutes(-20) },
            new SavedVacancyDB { Id = Guid.NewGuid(), UserId = user, VacancyId = inactive.Id, SavedAt = DateTimeOffset.UtcNow.AddMinutes(-10) }
        );
        await db.SaveChangesAsync();

        // Act
        IReadOnlyList<SavedVacancyDB> results = await repo.GetAllAsync(user, onlyActive: false);

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetByIdAsync_FindsBySavedVacancyIdOrVacancyId_ForCorrectUser()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfSavedVacancyRepository repo = new(db);

        Guid user = Guid.NewGuid();
        VacancyDB vac = CreateVacancy();
        db.Vacancies.Add(vac);

        SavedVacancyDB saved = new()
        {
            Id = Guid.NewGuid(),
            UserId = user,
            VacancyId = vac.Id,
            Notes = "Check tomorrow"
        };
        db.SavedVacancies.Add(saved);
        await db.SaveChangesAsync();

        // Act
        SavedVacancyDB? bySavedId = await repo.GetByIdAsync(saved.Id, user);
        SavedVacancyDB? byVacancyId = await repo.GetByIdAsync(vac.Id, user);
        SavedVacancyDB? otherUserLookup = await repo.GetByIdAsync(saved.Id, Guid.NewGuid());

        // Assert
        Assert.NotNull(bySavedId);
        Assert.Equal("Check tomorrow", bySavedId.Notes);

        Assert.NotNull(byVacancyId);
        Assert.Equal(saved.Id, byVacancyId.Id);

        Assert.Null(otherUserLookup);
    }

    [Fact]
    public async Task SaveAsync_GeneratesGuidAndTimestamp_AndLinksVacancy()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfSavedVacancyRepository repo = new(db);

        Guid user = Guid.NewGuid();
        VacancyDB vac = CreateVacancy("Data Engineer");
        db.Vacancies.Add(vac);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        SavedVacancyDB newSaved = new()
        {
            Id = Guid.Empty, // Should be auto-generated
            UserId = user,
            VacancyId = vac.Id,
            Notes = "Promising stack"
        };

        // Act
        SavedVacancyDB result = await repo.SaveAsync(newSaved);

        // Assert
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.True(result.SavedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.NotNull(result.Vacancy);
        Assert.Equal("Data Engineer", result.Vacancy.Title);

        await using WaterfallDbContext verifyDb = _fixture.CreateContext();
        SavedVacancyDB? stored = await verifyDb.SavedVacancies.FindAsync(result.Id);
        Assert.NotNull(stored);
        Assert.Equal("Promising stack", stored.Notes);
    }

    [Theory]
    [InlineData(true, "Delete by SavedVacancy ID")]
    [InlineData(false, "Delete by Vacancy ID")]
    public async Task DeleteAsync_ExistingForUser_RemovesRecordAndReturnsTrue(bool useSavedId, string scenario)
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfSavedVacancyRepository repo = new(db);

        Guid user = Guid.NewGuid();
        VacancyDB vac = CreateVacancy();
        db.Vacancies.Add(vac);

        SavedVacancyDB saved = new()
        {
            Id = Guid.NewGuid(),
            UserId = user,
            VacancyId = vac.Id
        };
        db.SavedVacancies.Add(saved);
        await db.SaveChangesAsync();

        Guid targetId = useSavedId ? saved.Id : vac.Id;

        // Act
        bool result = await repo.DeleteAsync(targetId, user);

        // Assert
        Assert.True(result, $"Scenario '{scenario}' should return true.");
        await using WaterfallDbContext verifyDb = _fixture.CreateContext();
        Assert.Null(await verifyDb.SavedVacancies.FindAsync(saved.Id));
    }

    [Fact]
    public async Task DeleteAsync_WrongUserOrNonExistent_ReturnsFalse()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfSavedVacancyRepository repo = new(db);

        Guid owner = Guid.NewGuid();
        Guid otherUser = Guid.NewGuid();
        VacancyDB vac = CreateVacancy();
        db.Vacancies.Add(vac);

        SavedVacancyDB saved = new()
        {
            Id = Guid.NewGuid(),
            UserId = owner,
            VacancyId = vac.Id
        };
        db.SavedVacancies.Add(saved);
        await db.SaveChangesAsync();

        // Act
        bool wrongUserResult = await repo.DeleteAsync(saved.Id, otherUser);
        bool nonExistentResult = await repo.DeleteAsync(Guid.NewGuid(), owner);

        // Assert
        Assert.False(wrongUserResult);
        Assert.False(nonExistentResult);
    }

    [Fact]
    public async Task ExistsForVacancyAsync_ReturnsExpectedBoolean()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfSavedVacancyRepository repo = new(db);

        Guid user = Guid.NewGuid();
        VacancyDB vac1 = CreateVacancy();
        VacancyDB vac2 = CreateVacancy();
        db.Vacancies.AddRange(vac1, vac2);

        db.SavedVacancies.Add(new SavedVacancyDB
        {
            Id = Guid.NewGuid(),
            UserId = user,
            VacancyId = vac1.Id
        });
        await db.SaveChangesAsync();

        // Act
        bool existsForVac1 = await repo.ExistsForVacancyAsync(vac1.Id, user);
        bool existsForVac2 = await repo.ExistsForVacancyAsync(vac2.Id, user);
        bool nullUserCheck = await repo.ExistsForVacancyAsync(vac1.Id, null);
        bool otherUserCheck = await repo.ExistsForVacancyAsync(vac1.Id, Guid.NewGuid());

        // Assert
        Assert.True(existsForVac1);
        Assert.False(existsForVac2);
        Assert.False(nullUserCheck);
        Assert.False(otherUserCheck);
    }

    [Fact]
    public async Task GetSavedVacancyIdsAsync_ReturnsOnlyUserSavedIdsFromGivenCollection()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfSavedVacancyRepository repo = new(db);

        Guid user = Guid.NewGuid();
        Guid otherUser = Guid.NewGuid();

        VacancyDB vac1 = CreateVacancy();
        VacancyDB vac2 = CreateVacancy();
        VacancyDB vac3 = CreateVacancy();
        db.Vacancies.AddRange(vac1, vac2, vac3);

        db.SavedVacancies.AddRange(
            new SavedVacancyDB { Id = Guid.NewGuid(), UserId = user, VacancyId = vac1.Id },
            new SavedVacancyDB { Id = Guid.NewGuid(), UserId = otherUser, VacancyId = vac2.Id }
        );
        await db.SaveChangesAsync();

        // Act - empty input list edge case
        HashSet<Guid> emptyResult = await repo.GetSavedVacancyIdsAsync([], user);

        // Act - query [vac1, vac2, vac3]
        HashSet<Guid> foundResult = await repo.GetSavedVacancyIdsAsync([vac1.Id, vac2.Id, vac3.Id], user);

        // Assert
        Assert.Empty(emptyResult);
        Assert.Single(foundResult);
        Assert.Contains(vac1.Id, foundResult);
        Assert.DoesNotContain(vac2.Id, foundResult);
        Assert.DoesNotContain(vac3.Id, foundResult);
    }
}
