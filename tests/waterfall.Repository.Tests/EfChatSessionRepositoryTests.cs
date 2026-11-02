// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Repositories;

namespace Waterfall.Repository.Tests;

/// <summary>
/// Модульні тести репозиторію діалогів з ШІ-консультантом (FR-009) на базі ізольованої in-memory SQLite.
/// Перевіряються інваріанти, важливі для безпеки та цілісності даних:
/// чужий діалог невидимий (THR-12) і посилання на неіснуючу вакансію неможливе (FUNC-001).
/// </summary>
public sealed class EfChatSessionRepositoryTests : IDisposable
{
    private readonly SqliteTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static UserDB CreateUser(string email = "seeker@waterfall.test")
    {
        return new UserDB
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = "hash",
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
            Company = "Waterfall Labs",
            Description = "Опис вакансії",
            Category = "Backend",
            Location = "Київ",
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

    private async Task<(Guid OwnerId, Guid StrangerId, Guid SessionId)> SeedSessionAsync(string filterJson = "{}")
    {
        await using WaterfallDbContext db = _fixture.CreateContext();
        UserDB owner = CreateUser("owner@waterfall.test");
        UserDB stranger = CreateUser("stranger@waterfall.test");
        db.Users.AddRange(owner, stranger);
        await db.SaveChangesAsync();

        EfChatSessionRepository repository = new(db);
        ChatSessionDB session = await repository.CreateSessionAsync(owner.Id, filterJson);
        return (owner.Id, stranger.Id, session.Id);
    }

    [Fact]
    public async Task GetSessionAsync_ReturnsSessionOnlyForItsOwner()
    {
        // Arrange
        (Guid ownerId, Guid strangerId, Guid sessionId) = await SeedSessionAsync("{\"search\":\"python\"}");
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfChatSessionRepository repository = new(db);

        // Act
        ChatSessionDB? own = await repository.GetSessionAsync(sessionId, ownerId);
        ChatSessionDB? foreign = await repository.GetSessionAsync(sessionId, strangerId);

        // Assert
        Assert.NotNull(own);
        Assert.Equal(ownerId, own!.UserId);
        Assert.Null(foreign);
    }

    [Fact]
    public async Task CreateSessionAsync_PersistsOwnerAndInitialFilter()
    {
        // Arrange
        (Guid ownerId, _, _) = await SeedSessionAsync();
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfChatSessionRepository repository = new(db);

        // Act
        ChatSessionDB session = await repository.CreateSessionAsync(ownerId, "{\"category_group\":\"Backend\"}");

        // Assert
        Assert.NotEqual(Guid.Empty, session.Id);
        Assert.Equal(ownerId, session.UserId);
        Assert.Equal("{\"category_group\":\"Backend\"}", session.ContextFilterJson);
        Assert.Equal(session.StartedAt, session.LastActivityAt);
        Assert.NotEqual(default, session.StartedAt);
    }

    [Fact]
    public async Task GetRecentMessagesAsync_ReturnsOnlyLastMessagesInChronologicalOrder()
    {
        // Arrange
        (Guid ownerId, _, Guid sessionId) = await SeedSessionAsync();
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfChatSessionRepository repository = new(db);
        for (int index = 0; index < 5; index++)
        {
            await repository.AddUserMessageAsync(sessionId, ownerId, $"повідомлення {index}");
        }

        // Act
        IReadOnlyList<ChatMessageDB> messages = await repository.GetRecentMessagesAsync(sessionId, ownerId, 3);

        // Assert
        Assert.Equal(3, messages.Count);
        Assert.Equal(
            ["повідомлення 2", "повідомлення 3", "повідомлення 4"],
            messages.Select(message => message.Content));
        Assert.Equal(ChatMessageAuthor.User, messages[0].Author);
    }

    [Fact]
    public async Task GetRecentMessagesAsync_ForeignOwner_GetsNothing()
    {
        // Arrange
        (Guid ownerId, Guid strangerId, Guid sessionId) = await SeedSessionAsync();
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfChatSessionRepository repository = new(db);
        await repository.AddUserMessageAsync(sessionId, ownerId, "привіт");

        // Act
        IReadOnlyList<ChatMessageDB> messages = await repository.GetRecentMessagesAsync(sessionId, strangerId, 10);

        // Assert
        Assert.Empty(messages);
    }

    [Fact]
    public async Task GetRecentMessagesAsync_NonPositiveLimit_ReturnsEmpty()
    {
        // Arrange
        (Guid ownerId, _, Guid sessionId) = await SeedSessionAsync();
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfChatSessionRepository repository = new(db);
        await repository.AddUserMessageAsync(sessionId, ownerId, "привіт");

        // Act
        IReadOnlyList<ChatMessageDB> messages = await repository.GetRecentMessagesAsync(sessionId, ownerId, 0);

        // Assert
        Assert.Empty(messages);
    }

    [Fact]
    public async Task AddUserMessageAsync_ForeignOwner_ReturnsNullAndWritesNothing()
    {
        // Arrange
        (Guid ownerId, Guid strangerId, Guid sessionId) = await SeedSessionAsync();
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfChatSessionRepository repository = new(db);

        // Act
        ChatMessageDB? message = await repository.AddUserMessageAsync(sessionId, strangerId, "чуже повідомлення");

        // Assert
        Assert.Null(message);
        Assert.False(await db.ChatMessages.AnyAsync(item => item.SessionId == sessionId));
        Assert.Empty(await repository.GetRecentMessagesAsync(sessionId, ownerId, 10));
    }

    [Fact]
    public async Task AddUserMessageAsync_UpdatesSessionActivity()
    {
        // Arrange
        (Guid ownerId, _, Guid sessionId) = await SeedSessionAsync();
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfChatSessionRepository repository = new(db);
        ChatSessionDB before = (await repository.GetSessionAsync(sessionId, ownerId))!;
        DateTimeOffset previousActivity = before.LastActivityAt;

        // Act
        await repository.AddUserMessageAsync(sessionId, ownerId, "шукаю python");

        // Assert
        ChatSessionDB after = (await repository.GetSessionAsync(sessionId, ownerId))!;
        Assert.True(after.LastActivityAt >= previousActivity);
    }

    [Fact]
    public async Task AddAssistantMessageAsync_LinksExactlyTheSentVacancies()
    {
        // Arrange
        (Guid ownerId, _, Guid sessionId) = await SeedSessionAsync();
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfChatSessionRepository repository = new(db);
        VacancyDB first = CreateVacancy("First");
        VacancyDB second = CreateVacancy("Second");
        db.Vacancies.AddRange(first, second);
        await db.SaveChangesAsync();

        // Act
        long? messageId = await repository.AddAssistantMessageAsync(
            sessionId,
            ownerId,
            "Ось дві вакансії",
            [first.Id, second.Id, second.Id]);

        // Assert
        Assert.NotNull(messageId);
        await using WaterfallDbContext verification = _fixture.CreateContext();
        ChatMessageDB stored = await verification.ChatMessages.SingleAsync(item => item.Id == messageId);
        Assert.Equal(ChatMessageAuthor.Assistant, stored.Author);
        Assert.Equal("Ось дві вакансії", stored.Content);
        List<Guid> linked = await verification.ChatMessageVacancies
            .Where(link => link.MessageId == messageId)
            .Select(link => link.VacancyId)
            .ToListAsync();
        Assert.Equal(2, linked.Count);
        Assert.Contains(first.Id, linked);
        Assert.Contains(second.Id, linked);
    }

    [Fact]
    public async Task AddAssistantMessageAsync_UnknownVacancy_ViolatesForeignKeyAndPersistsNothing()
    {
        // Arrange
        (Guid ownerId, _, Guid sessionId) = await SeedSessionAsync();
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfChatSessionRepository repository = new(db);

        // Act
        Task<long?> action = repository.AddAssistantMessageAsync(
            sessionId,
            ownerId,
            "Вигадана вакансія",
            [Guid.NewGuid()]);

        // Assert
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => action);
        await using WaterfallDbContext verification = _fixture.CreateContext();
        Assert.False(await verification.ChatMessages.AnyAsync(item => item.SessionId == sessionId));
        Assert.False(await verification.ChatMessageVacancies.AnyAsync());
    }

    [Fact]
    public async Task AddAssistantMessageAsync_ForeignOwner_ReturnsNullAndWritesNothing()
    {
        // Arrange
        (_, Guid strangerId, Guid sessionId) = await SeedSessionAsync();
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfChatSessionRepository repository = new(db);
        VacancyDB vacancy = CreateVacancy("Visible");
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        // Act
        long? messageId = await repository.AddAssistantMessageAsync(sessionId, strangerId, "чужа відповідь", [vacancy.Id]);

        // Assert
        Assert.Null(messageId);
        Assert.False(await db.ChatMessages.AnyAsync());
    }

    [Fact]
    public async Task UpdateContextFilterAsync_SavesFilterForOwnerOnly()
    {
        // Arrange
        (Guid ownerId, Guid strangerId, Guid sessionId) = await SeedSessionAsync();
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfChatSessionRepository repository = new(db);

        // Act
        bool foreignUpdate = await repository.UpdateContextFilterAsync(sessionId, strangerId, "{\"search\":\"чуже\"}");
        bool ownUpdate = await repository.UpdateContextFilterAsync(sessionId, ownerId, "{\"search\":\"python\"}");

        // Assert
        Assert.False(foreignUpdate);
        Assert.True(ownUpdate);
        ChatSessionDB session = (await repository.GetSessionAsync(sessionId, ownerId))!;
        Assert.Equal("{\"search\":\"python\"}", session.ContextFilterJson);
    }

    [Fact]
    public async Task DeleteSessionAsync_ForeignOwner_KeepsSession()
    {
        // Arrange
        (Guid ownerId, Guid strangerId, Guid sessionId) = await SeedSessionAsync();
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfChatSessionRepository repository = new(db);

        // Act
        bool deleted = await repository.DeleteSessionAsync(sessionId, strangerId);

        // Assert
        Assert.False(deleted);
        Assert.NotNull(await repository.GetSessionAsync(sessionId, ownerId));
    }

    [Fact]
    public async Task DeleteSessionAsync_RemovesMessagesAndVacancyLinks()
    {
        // Arrange
        (Guid ownerId, _, Guid sessionId) = await SeedSessionAsync();
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfChatSessionRepository repository = new(db);
        VacancyDB vacancy = CreateVacancy("Linked");
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        await repository.AddUserMessageAsync(sessionId, ownerId, "привіт");
        await repository.AddAssistantMessageAsync(sessionId, ownerId, "відповідь", [vacancy.Id]);

        // Act
        bool deleted = await repository.DeleteSessionAsync(sessionId, ownerId);

        // Assert
        Assert.True(deleted);
        await using WaterfallDbContext verification = _fixture.CreateContext();
        Assert.False(await verification.ChatSessions.AnyAsync(item => item.Id == sessionId));
        Assert.False(await verification.ChatMessages.AnyAsync(item => item.SessionId == sessionId));
        Assert.False(await verification.ChatMessageVacancies.AnyAsync());
        Assert.True(await verification.Vacancies.AnyAsync(item => item.Id == vacancy.Id));
    }
}
