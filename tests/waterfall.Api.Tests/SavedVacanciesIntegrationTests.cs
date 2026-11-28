// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Waterfall.Api.Controllers;
using Waterfall.Api.Models.Common;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Repositories;
using Waterfall.Services;
using Waterfall.Services.Models.SavedVacancies;

namespace Waterfall.Api.Tests;

/// <summary>
/// Інтеграційні тести <see cref="SavedVacanciesController"/> проти реальної SQLite БД:
/// перевірка гонки одночасного збереження (201 + 409 Conflict, один рядок у БД) та видалення за VacancyId.
/// </summary>
public sealed class SavedVacanciesIntegrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"wf_api_saved_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<WaterfallDbContext> _dbOptions;

    public SavedVacanciesIntegrationTests()
    {
        _dbOptions = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlite($"Data Source={_dbPath};Default Timeout=5")
            .Options;

        using WaterfallDbContext db = new(_dbOptions);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private async Task<(Guid UserId, Guid VacancyId)> SeedUserAndVacancyAsync()
    {
        Guid userId = Guid.NewGuid();
        Guid vacancyId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using WaterfallDbContext db = new(_dbOptions);
        db.Users.Add(new UserDB
        {
            Id = userId,
            Email = $"seeker-{userId:N}@waterfall.test",
            PasswordHash = "hash",
            Role = "JobSeeker",
            CreatedAt = now
        });
        db.Vacancies.Add(new VacancyDB
        {
            Id = vacancyId,
            ExternalId = vacancyId.ToString("N"),
            Title = "Backend Engineer",
            Company = "Waterfall",
            Description = "ASP.NET Core",
            Category = "Backend",
            Location = "Kyiv",
            Currency = "USD",
            Source = "Djinni",
            SourceUrl = $"https://example.com/jobs/{vacancyId:N}",
            SkillsJson = "[\"C#\"]",
            PublishedAt = now,
            FirstSeenAt = now,
            LastSeenAt = now,
            IsActive = true
        });
        await db.SaveChangesAsync();

        return (userId, vacancyId);
    }

    private SavedVacanciesController CreateController(WaterfallDbContext db, Guid userId)
    {
        EfVacancyRepository vacancyRepo = new(db);
        EfSavedVacancyRepository savedRepo = new(db);
        VacancyCacheService cache = new(new MemoryCache(new MemoryCacheOptions()));
        VacancyService vacancyService = new(vacancyRepo, savedRepo, cache, NullLogger<VacancyService>.Instance);
        SavedVacancyService savedService = new(savedRepo, cache, NullLogger<SavedVacancyService>.Instance);

        SavedVacanciesController controller = new(savedService, vacancyService, NullLogger<SavedVacanciesController>.Instance);
        ControllerTestContext.Setup(controller, currentUserId: userId);
        return controller;
    }

    [Fact]
    public async Task Save_ConcurrentDuplicateRequests_ReturnsOneCreatedAndOneConflictWithSingleDatabaseRow()
    {
        // Arrange
        (Guid userId, Guid vacancyId) = await SeedUserAndVacancyAsync();
        using ManualResetEventSlim startGate = new(false);

        async Task<ActionResult<SavedVacancyDto>> SendSaveRequestAsync()
        {
            await using WaterfallDbContext db = new(_dbOptions);
            SavedVacanciesController controller = CreateController(db, userId);
            startGate.Wait();
            return await controller.Save(new SaveVacancyRequest { VacancyId = vacancyId, Notes = "Keep" }, CancellationToken.None);
        }

        // Act
        Task<ActionResult<SavedVacancyDto>> firstTask = Task.Run(SendSaveRequestAsync);
        Task<ActionResult<SavedVacancyDto>> secondTask = Task.Run(SendSaveRequestAsync);
        startGate.Set();

        ActionResult<SavedVacancyDto>[] results = await Task.WhenAll(firstTask, secondTask);

        // Assert
        CreatedAtActionResult created = Assert.Single(results.Select(r => r.Result).OfType<CreatedAtActionResult>());
        SavedVacancyDto createdDto = Assert.IsType<SavedVacancyDto>(created.Value);
        Assert.Equal(vacancyId, createdDto.Id);
        Assert.Equal(vacancyId, createdDto.VacancyId);

        ConflictObjectResult conflict = Assert.Single(results.Select(r => r.Result).OfType<ConflictObjectResult>());
        ApiErrorResponse error = Assert.IsType<ApiErrorResponse>(conflict.Value);
        Assert.Equal("CONFLICT", error.Error.Code);

        await using WaterfallDbContext verifyDb = new(_dbOptions);
        int count = await verifyDb.SavedVacancies.CountAsync(s => s.UserId == userId && s.VacancyId == vacancyId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Delete_ByVacancyId_RemovesBookmarkAndSubsequentGetReturnsNotFound()
    {
        // Arrange
        (Guid userId, Guid vacancyId) = await SeedUserAndVacancyAsync();

        await using (WaterfallDbContext setupDb = new(_dbOptions))
        {
            SavedVacanciesController setupController = CreateController(setupDb, userId);
            await setupController.Save(new SaveVacancyRequest { VacancyId = vacancyId }, CancellationToken.None);
        }

        // Act
        await using WaterfallDbContext deleteDb = new(_dbOptions);
        SavedVacanciesController deleteController = CreateController(deleteDb, userId);
        IActionResult deleteResult = await deleteController.Delete(vacancyId, CancellationToken.None);
        ActionResult<SavedVacancyDto> getAfterDelete = await deleteController.GetById(vacancyId, CancellationToken.None);

        // Assert
        Assert.IsType<NoContentResult>(deleteResult);
        Assert.IsType<NotFoundObjectResult>(getAfterDelete.Result);

        await using WaterfallDbContext verifyDb = new(_dbOptions);
        Assert.False(await verifyDb.SavedVacancies.AnyAsync(s => s.UserId == userId && s.VacancyId == vacancyId));
    }
}
