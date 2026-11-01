// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Repositories;

namespace Waterfall.Repository.Tests;

/// <summary>
/// Модульні тести для EfVacancyViewRepository (історія переглядів, FR-006, FR-007)
/// на базі ізольованої in-memory SQLite.
/// VacancyViewDB зовнішніми ключами пов'язана з Users і Vacancies, тому кейс висіває ці рядки.
/// </summary>
public sealed class EfVacancyViewRepositoryTests : IDisposable
{
    private readonly SqliteTestFixture _fixture = new();

    private static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(60);

    public void Dispose() => _fixture.Dispose();

    private static UserDB CreateUser(string email)
    {
        return new UserDB
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = "hash_" + email,
            Role = "JobSeeker",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static VacancyDB CreateVacancy(string title)
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
            IsActive = true
        };
    }

    private static VacancyViewDB CreateView(Guid userId, Guid vacancyId, DateTimeOffset viewedAt)
    {
        return new VacancyViewDB
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            VacancyId = vacancyId,
            ViewedAt = viewedAt
        };
    }

    [Fact]
    public async Task AddAsync_EmptyId_GeneratesGuidAndPersists()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        UserDB user = CreateUser("owner@example.com");
        VacancyDB vacancy = CreateVacancy("Senior .NET розробник");
        db.Users.Add(user);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        EfVacancyViewRepository repo = new(db);

        // Act
        await repo.AddAsync(new VacancyViewDB { UserId = user.Id, VacancyId = vacancy.Id });

        // Assert
        await using WaterfallDbContext verifyDb = _fixture.CreateContext();
        Assert.Equal(1, verifyDb.VacancyViews.Count());
        VacancyViewDB stored = verifyDb.VacancyViews.Single(v => v.UserId == user.Id);

        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.Equal(vacancy.Id, stored.VacancyId);
        Assert.True(stored.ViewedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task GetForUserAsync_ReturnsNewestFirstWithVacancies_ExcludesOtherUsers()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        UserDB user = CreateUser("owner@example.com");
        UserDB otherUser = CreateUser("other@example.com");
        VacancyDB first = CreateVacancy("Перша вакансія");
        VacancyDB second = CreateVacancy("Друга вакансія");
        VacancyDB third = CreateVacancy("Третя вакансія");
        db.Users.AddRange(user, otherUser);
        db.Vacancies.AddRange(first, second, third);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        db.VacancyViews.AddRange(
            CreateView(user.Id, second.Id, now.AddMinutes(-1)),
            CreateView(user.Id, first.Id, now.AddMinutes(-2)),
            CreateView(otherUser.Id, third.Id, now.AddMinutes(-1)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        EfVacancyViewRepository repo = new(db);

        // Act
        IReadOnlyList<VacancyViewDB> views = await repo.GetForUserAsync(user.Id);
        IReadOnlyList<VacancyViewDB> emptyHistory = await repo.GetForUserAsync(Guid.NewGuid());

        // Assert
        Assert.Equal([second.Id, first.Id], views.Select(v => v.VacancyId).ToArray());
        Assert.NotNull(views[0].Vacancy);
        Assert.Equal("Друга вакансія", views[0].Vacancy!.Title);
        Assert.Empty(emptyHistory);
    }

    [Fact]
    public async Task ExistsRecentAsync_MatchesOnlyWithinWindowForGivenUserAndVacancy()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        UserDB user = CreateUser("owner@example.com");
        UserDB otherUser = CreateUser("other@example.com");
        VacancyDB recentVacancy = CreateVacancy("Свіжа вакансія");
        VacancyDB anotherVacancy = CreateVacancy("Інша вакансія");
        db.Users.AddRange(user, otherUser);
        db.Vacancies.AddRange(recentVacancy, anotherVacancy);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        db.VacancyViews.AddRange(
            CreateView(user.Id, recentVacancy.Id, now.Add(DedupWindow.Negate() + TimeSpan.FromMinutes(10))),
            CreateView(user.Id, recentVacancy.Id, now.Add(2 * DedupWindow.Negate())),
            CreateView(otherUser.Id, recentVacancy.Id, now.AddMinutes(-5)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        EfVacancyViewRepository repo = new(db);

        // Act
        bool withinWindow = await repo.ExistsRecentAsync(user.Id, recentVacancy.Id, DedupWindow);
        bool outsideWindow = await repo.ExistsRecentAsync(user.Id, recentVacancy.Id, TimeSpan.FromMinutes(30));
        bool neverViewed = await repo.ExistsRecentAsync(user.Id, anotherVacancy.Id, DedupWindow);
        bool otherUserView = await repo.ExistsRecentAsync(Guid.NewGuid(), recentVacancy.Id, DedupWindow);

        // Assert
        Assert.True(withinWindow, "Вікну 60 хв відповідає перегляд 50 хв тому.");
        Assert.False(outsideWindow, "У вікні 30 хв немає переглядів: найсвіжіший — 50 хв тому.");
        Assert.False(neverViewed);
        Assert.False(otherUserView, "Перегляд іншого користувача не рахується.");
    }
}
