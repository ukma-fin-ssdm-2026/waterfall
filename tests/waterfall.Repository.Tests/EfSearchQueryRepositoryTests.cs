// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Repositories;

namespace Waterfall.Repository.Tests;

/// <summary>
/// Модульні тести для EfSearchQueryRepository (історія пошуків, FR-007)
/// на базі ізольованої in-memory SQLite.
/// SearchQueryDB пов'язана зовнішнім ключем з Users, тому кожен кейс висіває реальних користувачів.
/// </summary>
public sealed class EfSearchQueryRepositoryTests : IDisposable
{
    private readonly SqliteTestFixture _fixture = new();

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

    private static SearchQueryDB CreateQuery(Guid userId, string text, DateTimeOffset searchedAt)
    {
        return new SearchQueryDB
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            QueryText = text,
            SearchedAt = searchedAt
        };
    }

    [Fact]
    public async Task AddAsync_EmptyId_GeneratesGuidAndPersists()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        UserDB user = CreateUser("owner@example.com");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        EfSearchQueryRepository repo = new(db);

        // Act
        await repo.AddAsync(new SearchQueryDB { UserId = user.Id, QueryText = "Python розробник" });

        // Assert
        await using WaterfallDbContext verifyDb = _fixture.CreateContext();
        SearchQueryDB? stored = await verifyDb.SearchQueries.SingleOrDefaultAsync(q => q.UserId == user.Id);

        Assert.NotNull(stored);
        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.Equal("Python розробник", stored.QueryText);
        Assert.True(stored.SearchedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsNewestFirstWithinLimit_ExcludesOtherUsers()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        UserDB user = CreateUser("owner@example.com");
        UserDB otherUser = CreateUser("other@example.com");
        db.Users.AddRange(user, otherUser);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        db.SearchQueries.AddRange(
            CreateQuery(user.Id, "q4", now.AddMinutes(-1)),
            CreateQuery(user.Id, "q3", now.AddMinutes(-2)),
            CreateQuery(user.Id, "q2", now.AddMinutes(-3)),
            CreateQuery(user.Id, "q1", now.AddMinutes(-4)),
            CreateQuery(otherUser.Id, "q чужого користувача", now.AddSeconds(-30)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        EfSearchQueryRepository repo = new(db);

        // Act
        IReadOnlyList<SearchQueryDB> recent = await repo.GetRecentAsync(user.Id, limit: 3);
        IReadOnlyList<SearchQueryDB> zeroLimit = await repo.GetRecentAsync(user.Id, limit: 0);

        // Assert
        Assert.Equal(["q4", "q3", "q2"], recent.Select(q => q.QueryText).ToArray());
        Assert.Empty(zeroLimit);
    }

    [Fact]
    public async Task GetLatestAsync_ReturnsNewestQueryOrNothingForUserWithoutHistory()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        UserDB user = CreateUser("owner@example.com");
        UserDB otherUser = CreateUser("other@example.com");
        db.Users.AddRange(user, otherUser);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        db.SearchQueries.AddRange(
            CreateQuery(user.Id, "старший запит", now.AddMinutes(-5)),
            CreateQuery(user.Id, "останній запит", now.AddMinutes(-1)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        EfSearchQueryRepository repo = new(db);

        // Act
        SearchQueryDB? latest = await repo.GetLatestAsync(user.Id);
        SearchQueryDB? emptyHistory = await repo.GetLatestAsync(otherUser.Id);

        // Assert
        Assert.NotNull(latest);
        Assert.Equal("останній запит", latest.QueryText);
        Assert.Null(emptyHistory);
    }

    [Fact]
    public async Task GetCountAsync_CountsOnlyUsersOwnQueries()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        UserDB user = CreateUser("owner@example.com");
        UserDB otherUser = CreateUser("other@example.com");
        db.Users.AddRange(user, otherUser);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        db.SearchQueries.AddRange(
            CreateQuery(user.Id, "q1", now.AddMinutes(-3)),
            CreateQuery(user.Id, "q2", now.AddMinutes(-2)),
            CreateQuery(user.Id, "q3", now.AddMinutes(-1)),
            CreateQuery(otherUser.Id, "q чужого користувача", now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        EfSearchQueryRepository repo = new(db);

        // Act
        int count = await repo.GetCountAsync(user.Id);
        int emptyUserCount = await repo.GetCountAsync(Guid.NewGuid());

        // Assert
        Assert.Equal(3, count);
        Assert.Equal(0, emptyUserCount);
    }

    [Fact]
    public async Task DeleteAllForUserAsync_DeletesOnlyGivenUsersQueriesAndReportsResult()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        UserDB user = CreateUser("owner@example.com");
        UserDB emptyUser = CreateUser("empty@example.com");
        UserDB otherUser = CreateUser("other@example.com");
        db.Users.AddRange(user, emptyUser, otherUser);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        db.SearchQueries.AddRange(
            CreateQuery(user.Id, "q1", now.AddMinutes(-2)),
            CreateQuery(user.Id, "q2", now.AddMinutes(-1)),
            CreateQuery(otherUser.Id, "q чужого користувача", now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        EfSearchQueryRepository repo = new(db);

        // Act
        bool firstClean = await repo.DeleteAllForUserAsync(user.Id);
        bool secondClean = await repo.DeleteAllForUserAsync(user.Id);
        bool cleanForEmptyUser = await repo.DeleteAllForUserAsync(emptyUser.Id);

        // Assert
        Assert.True(firstClean);
        Assert.False(secondClean, "Повторне очищення історії: видаляти вже нічого.");
        Assert.False(cleanForEmptyUser, "Історія порожня: видаляти нічого.");
        Assert.Equal(0, await repo.GetCountAsync(user.Id));
        Assert.Equal(1, await repo.GetCountAsync(otherUser.Id));
    }
}
