// AI: модульні тести репозиторію збережених вакансій (EfSavedVacancyRepository, Lab 06); перевірено: запуск dotnet test, самоперевірка автора (Денис Сидоренко, §8)

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

    private static UserDB CreateUser(string? email = null)
    {
        return new UserDB
        {
            Id = Guid.NewGuid(),
            Email = email ?? $"{Guid.NewGuid():N}@waterfall.test",
            PasswordHash = "hash",
            Role = "JobSeeker",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

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

        UserDB user1 = CreateUser();
        UserDB user2 = CreateUser();
        db.Users.AddRange(user1, user2);

        VacancyDB activeVac = CreateVacancy("Active Vacancy", isActive: true);
        VacancyDB inactiveVac = CreateVacancy("Inactive Vacancy", isActive: false);
        db.Vacancies.AddRange(activeVac, inactiveVac);

        SavedVacancyDB savedActive = new()
        {
            UserId = user1.Id,
            VacancyId = activeVac.Id,
            SavedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        SavedVacancyDB savedInactive = new()
        {
            UserId = user1.Id,
            VacancyId = inactiveVac.Id,
            SavedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        SavedVacancyDB otherUserSaved = new()
        {
            UserId = user2.Id,
            VacancyId = activeVac.Id,
            SavedAt = DateTimeOffset.UtcNow
        };

        db.SavedVacancies.AddRange(savedActive, savedInactive, otherUserSaved);
        await db.SaveChangesAsync();

        // Act
        IReadOnlyList<SavedVacancyDB> results = await repo.GetAllAsync(user1.Id, onlyActive: true);

        // Assert
        Assert.Single(results);
        Assert.Equal(user1.Id, results[0].UserId);
        Assert.Equal(activeVac.Id, results[0].VacancyId);
        Assert.NotNull(results[0].Vacancy);
        Assert.Equal("Active Vacancy", results[0].Vacancy.Title);
    }

    [Fact]
    public async Task GetAllAsync_OnlyActiveFalse_IncludesBothActiveAndInactiveSavedVacancies()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfSavedVacancyRepository repo = new(db);

        UserDB user = CreateUser();
        db.Users.Add(user);

        VacancyDB active = CreateVacancy("Active", isActive: true);
        VacancyDB inactive = CreateVacancy("Archived", isActive: false);
        db.Vacancies.AddRange(active, inactive);

        db.SavedVacancies.AddRange(
            new SavedVacancyDB { UserId = user.Id, VacancyId = active.Id, SavedAt = DateTimeOffset.UtcNow.AddMinutes(-20) },
            new SavedVacancyDB { UserId = user.Id, VacancyId = inactive.Id, SavedAt = DateTimeOffset.UtcNow.AddMinutes(-10) }
        );
        await db.SaveChangesAsync();

        // Act
        IReadOnlyList<SavedVacancyDB> results = await repo.GetAllAsync(user.Id, onlyActive: false);

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task GetByIdAsync_FindsByVacancyId_OnlyForOwningUser()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfSavedVacancyRepository repo = new(db);

        UserDB user = CreateUser();
        db.Users.Add(user);

        VacancyDB vac = CreateVacancy();
        db.Vacancies.Add(vac);

        SavedVacancyDB saved = new()
        {
            UserId = user.Id,
            VacancyId = vac.Id,
            Notes = "Check tomorrow"
        };
        db.SavedVacancies.Add(saved);
        await db.SaveChangesAsync();

        // Act
        SavedVacancyDB? byVacancyId = await repo.GetByIdAsync(vac.Id, user.Id);
        SavedVacancyDB? otherUserLookup = await repo.GetByIdAsync(vac.Id, Guid.NewGuid());

        // Assert
        Assert.NotNull(byVacancyId);
        Assert.Equal(user.Id, byVacancyId.UserId);
        Assert.Equal(vac.Id, byVacancyId.VacancyId);
        Assert.Equal("Check tomorrow", byVacancyId.Notes);

        Assert.Null(otherUserLookup);
    }

    [Fact]
    public async Task SaveAsync_SetsTimestamp_AndLinksVacancyWithoutTrackingConflict()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfSavedVacancyRepository repo = new(db);

        UserDB user = CreateUser();
        db.Users.Add(user);

        VacancyDB vac = CreateVacancy("Data Engineer");
        db.Vacancies.Add(vac);
        await db.SaveChangesAsync();

        SavedVacancyDB newSaved = new()
        {
            UserId = user.Id,
            VacancyId = vac.Id,
            Notes = "Promising stack",
            SavedAt = default
        };

        // Act
        SavedVacancyDB result = await repo.SaveAsync(newSaved);

        // Assert
        Assert.True(result.SavedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.NotNull(result.Vacancy);
        Assert.Equal("Data Engineer", result.Vacancy.Title);

        await using WaterfallDbContext verifyDb = _fixture.CreateContext();
        SavedVacancyDB? stored = await verifyDb.SavedVacancies.FindAsync(user.Id, vac.Id);
        Assert.NotNull(stored);
        Assert.Equal("Promising stack", stored.Notes);
    }

    [Fact]
    public async Task SaveAsync_DuplicateUserAndVacancy_ThrowsDbUpdateExceptionAndKeepsSingleRow()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfSavedVacancyRepository repo = new(db);

        UserDB user = CreateUser();
        db.Users.Add(user);

        VacancyDB vac = CreateVacancy("Platform Engineer");
        db.Vacancies.Add(vac);
        await db.SaveChangesAsync();

        await repo.SaveAsync(new SavedVacancyDB { UserId = user.Id, VacancyId = vac.Id });

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            repo.SaveAsync(new SavedVacancyDB { UserId = user.Id, VacancyId = vac.Id }));

        await using WaterfallDbContext verifyDb = _fixture.CreateContext();
        int rowCount = await verifyDb.SavedVacancies.CountAsync(s => s.UserId == user.Id && s.VacancyId == vac.Id);
        Assert.Equal(1, rowCount);
    }

    [Fact]
    public async Task SaveAsync_WhenVacancyDoesNotExist_ThrowsDbUpdateException()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfSavedVacancyRepository repo = new(db);

        UserDB user = CreateUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            repo.SaveAsync(new SavedVacancyDB { UserId = user.Id, VacancyId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task SaveAsync_ConcurrentDoubleSave_ProducesSingleRowAndDeterministicFailure()
    {
        // Arrange: shared-cache file/memory SQLite з двома окремими з'єднаннями для реальної гонки
        string dbPath = Path.Combine(Path.GetTempPath(), $"wf_saved_race_{Guid.NewGuid():N}.db");
        try
        {
            DbContextOptions<WaterfallDbContext> options = new DbContextOptionsBuilder<WaterfallDbContext>()
                .UseSqlite($"Data Source={dbPath};Default Timeout=5")
                .Options;

            UserDB user = CreateUser();
            VacancyDB vac = CreateVacancy("Concurrent Job");

            await using (WaterfallDbContext setupDb = new(options))
            {
                await setupDb.Database.EnsureCreatedAsync();
                setupDb.Users.Add(user);
                setupDb.Vacancies.Add(vac);
                await setupDb.SaveChangesAsync();
            }

            using ManualResetEventSlim startGate = new(false);

            async Task<bool> AttemptSaveAsync()
            {
                await using WaterfallDbContext context = new(options);
                EfSavedVacancyRepository repo = new(context);
                startGate.Wait();
                try
                {
                    await repo.SaveAsync(new SavedVacancyDB { UserId = user.Id, VacancyId = vac.Id });
                    return true;
                }
                catch (DbUpdateException)
                {
                    return false;
                }
            }

            Task<bool> first = Task.Run(AttemptSaveAsync);
            Task<bool> second = Task.Run(AttemptSaveAsync);
            startGate.Set();

            bool[] outcomes = await Task.WhenAll(first, second);

            // Assert: рівно 1 успіх, 1 відмова та 1 запис у БД
            Assert.Equal(1, outcomes.Count(succeeded => succeeded));
            Assert.Equal(1, outcomes.Count(succeeded => !succeeded));

            await using WaterfallDbContext verifyDb = new(options);
            int savedCount = await verifyDb.SavedVacancies.CountAsync(s => s.UserId == user.Id && s.VacancyId == vac.Id);
            Assert.Equal(1, savedCount);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task DeleteAsync_ExistingForUser_RemovesBookmarkByVacancyId()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfSavedVacancyRepository repo = new(db);

        UserDB user = CreateUser();
        db.Users.Add(user);

        VacancyDB vac = CreateVacancy();
        db.Vacancies.Add(vac);

        db.SavedVacancies.Add(new SavedVacancyDB { UserId = user.Id, VacancyId = vac.Id });
        await db.SaveChangesAsync();

        // Act
        bool result = await repo.DeleteAsync(vac.Id, user.Id);

        // Assert
        Assert.True(result);
        await using WaterfallDbContext verifyDb = _fixture.CreateContext();
        Assert.Null(await verifyDb.SavedVacancies.FindAsync(user.Id, vac.Id));
    }

    [Fact]
    public async Task DeleteAsync_WrongUserOrNonExistent_ReturnsFalse()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfSavedVacancyRepository repo = new(db);

        UserDB owner = CreateUser();
        UserDB otherUser = CreateUser();
        db.Users.AddRange(owner, otherUser);

        VacancyDB vac = CreateVacancy();
        db.Vacancies.Add(vac);

        db.SavedVacancies.Add(new SavedVacancyDB { UserId = owner.Id, VacancyId = vac.Id });
        await db.SaveChangesAsync();

        // Act
        bool wrongUserResult = await repo.DeleteAsync(vac.Id, otherUser.Id);
        bool nonExistentResult = await repo.DeleteAsync(Guid.NewGuid(), owner.Id);

        // Assert
        Assert.False(wrongUserResult);
        Assert.False(nonExistentResult);
    }

    [Fact]
    public async Task DeleteUser_CascadesSavedVacanciesAndUserPersonalData()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();

        UserDB targetUser = CreateUser("delete-me@waterfall.test");
        UserDB otherUser = CreateUser("keep-me@waterfall.test");
        db.Users.AddRange(targetUser, otherUser);

        VacancyDB vacancy = CreateVacancy("Shared Vacancy");
        db.Vacancies.Add(vacancy);

        db.SavedVacancies.AddRange(
            new SavedVacancyDB { UserId = targetUser.Id, VacancyId = vacancy.Id },
            new SavedVacancyDB { UserId = otherUser.Id, VacancyId = vacancy.Id });

        db.UserPersonalData.AddRange(
            new UserPersonalDataDB { Id = Guid.NewGuid(), UserId = targetUser.Id, Address = "вул. Хрещатик, 1" },
            new UserPersonalDataDB { Id = Guid.NewGuid(), UserId = otherUser.Id, Address = "вул. Садова, 2" });

        await db.SaveChangesAsync();

        // Act
        db.Users.Remove(targetUser);
        await db.SaveChangesAsync();

        // Assert
        await using WaterfallDbContext verifyDb = _fixture.CreateContext();
        Assert.False(await verifyDb.SavedVacancies.AnyAsync(s => s.UserId == targetUser.Id));
        Assert.False(await verifyDb.UserPersonalData.AnyAsync(p => p.UserId == targetUser.Id));

        Assert.True(await verifyDb.SavedVacancies.AnyAsync(s => s.UserId == otherUser.Id && s.VacancyId == vacancy.Id));
        Assert.True(await verifyDb.UserPersonalData.AnyAsync(p => p.UserId == otherUser.Id));
        Assert.True(await verifyDb.Vacancies.AnyAsync(v => v.Id == vacancy.Id));
    }

    [Fact]
    public async Task ExistsForVacancyAsync_ReturnsExpectedBoolean()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfSavedVacancyRepository repo = new(db);

        UserDB user = CreateUser();
        db.Users.Add(user);

        VacancyDB vac1 = CreateVacancy();
        VacancyDB vac2 = CreateVacancy();
        db.Vacancies.AddRange(vac1, vac2);

        db.SavedVacancies.Add(new SavedVacancyDB { UserId = user.Id, VacancyId = vac1.Id });
        await db.SaveChangesAsync();

        // Act
        bool existsForVac1 = await repo.ExistsForVacancyAsync(vac1.Id, user.Id);
        bool existsForVac2 = await repo.ExistsForVacancyAsync(vac2.Id, user.Id);
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

        UserDB user = CreateUser();
        UserDB otherUser = CreateUser();
        db.Users.AddRange(user, otherUser);

        VacancyDB vac1 = CreateVacancy();
        VacancyDB vac2 = CreateVacancy();
        VacancyDB vac3 = CreateVacancy();
        db.Vacancies.AddRange(vac1, vac2, vac3);

        db.SavedVacancies.AddRange(
            new SavedVacancyDB { UserId = user.Id, VacancyId = vac1.Id },
            new SavedVacancyDB { UserId = otherUser.Id, VacancyId = vac2.Id }
        );
        await db.SaveChangesAsync();

        // Act - empty input list edge case
        HashSet<Guid> emptyResult = await repo.GetSavedVacancyIdsAsync([], user.Id);

        // Act - query [vac1, vac2, vac3]
        HashSet<Guid> foundResult = await repo.GetSavedVacancyIdsAsync([vac1.Id, vac2.Id, vac3.Id], user.Id);

        // Assert
        Assert.Empty(emptyResult);
        Assert.Single(foundResult);
        Assert.Contains(vac1.Id, foundResult);
        Assert.DoesNotContain(vac2.Id, foundResult);
        Assert.DoesNotContain(vac3.Id, foundResult);
    }
}
