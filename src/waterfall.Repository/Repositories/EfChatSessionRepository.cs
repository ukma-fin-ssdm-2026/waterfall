// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;

namespace Waterfall.Repository.Repositories;

/// <summary>
/// Репозиторій діалогів з ШІ-консультантом на базі EF Core.
/// Кожен запит, що приймає sessionId, додатково фільтрується за власником (THR-12),
/// щоб чужий діалог був невидимий навіть за помилки у верхньому шарі.
/// </summary>
public class EfChatSessionRepository : IChatSessionRepository
{
    private readonly WaterfallDbContext _context;

    public EfChatSessionRepository(WaterfallDbContext context)
    {
        _context = context;
    }

    public async Task<ChatSessionDB?> GetSessionAsync(Guid sessionId, Guid userId, CancellationToken ct = default)
    {
        return await _context.ChatSessions
            .FirstOrDefaultAsync(session => session.Id == sessionId && session.UserId == userId, ct);
    }

    public async Task<ChatSessionDB> CreateSessionAsync(Guid userId, string contextFilterJson, CancellationToken ct = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ChatSessionDB session = new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ContextFilterJson = contextFilterJson,
            StartedAt = now,
            LastActivityAt = now
        };

        _context.ChatSessions.Add(session);
        await _context.SaveChangesAsync(ct);
        return session;
    }

    public async Task<IReadOnlyList<ChatMessageDB>> GetRecentMessagesAsync(
        Guid sessionId,
        Guid userId,
        int limit,
        CancellationToken ct = default)
    {
        if (limit <= 0)
        {
            return Array.Empty<ChatMessageDB>();
        }

        // Беремо останні за ідентифікатором (він монотонний), а потім розвертаємо в хронологію
        List<ChatMessageDB> messages = await (
                from message in _context.ChatMessages.AsNoTracking()
                join session in _context.ChatSessions on message.SessionId equals session.Id
                where message.SessionId == sessionId && session.UserId == userId
                orderby message.Id descending
                select message)
            .Take(limit)
            .ToListAsync(ct);

        messages.Reverse();
        return messages;
    }

    public async Task<ChatMessageDB?> AddUserMessageAsync(
        Guid sessionId,
        Guid userId,
        string content,
        CancellationToken ct = default)
    {
        ChatSessionDB? session = await GetSessionAsync(sessionId, userId, ct);
        if (session is null)
        {
            return null;
        }

        ChatMessageDB message = new()
        {
            SessionId = sessionId,
            Author = ChatMessageAuthor.User,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow
        };

        session.LastActivityAt = message.CreatedAt;
        _context.ChatMessages.Add(message);
        await _context.SaveChangesAsync(ct);
        return message;
    }

    public async Task<long?> AddAssistantMessageAsync(
        Guid sessionId,
        Guid userId,
        string content,
        IReadOnlyList<Guid> vacancyIds,
        CancellationToken ct = default)
    {
        ChatSessionDB? session = await GetSessionAsync(sessionId, userId, ct);
        if (session is null)
        {
            return null;
        }

        await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(ct);

        ChatMessageDB message = new()
        {
            SessionId = sessionId,
            Author = ChatMessageAuthor.Assistant,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.ChatMessages.Add(message);
        await _context.SaveChangesAsync(ct);

        // Зв'язки з вакансіями, які реально пішли клієнту: FK на Vacancies унеможливлює
        // посилання на вигадану вакансію (FUNC-001, data-model.md інваріант 6)
        foreach (Guid vacancyId in vacancyIds.Distinct())
        {
            _context.ChatMessageVacancies.Add(new ChatMessageVacancyDB
            {
                MessageId = message.Id,
                VacancyId = vacancyId
            });
        }

        session.LastActivityAt = message.CreatedAt;
        await _context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return message.Id;
    }

    public async Task<bool> UpdateContextFilterAsync(
        Guid sessionId,
        Guid userId,
        string contextFilterJson,
        CancellationToken ct = default)
    {
        ChatSessionDB? session = await GetSessionAsync(sessionId, userId, ct);
        if (session is null)
        {
            return false;
        }

        session.ContextFilterJson = contextFilterJson;
        session.LastActivityAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteSessionAsync(Guid sessionId, Guid userId, CancellationToken ct = default)
    {
        ChatSessionDB? session = await _context.ChatSessions
            .FirstOrDefaultAsync(item => item.Id == sessionId && item.UserId == userId, ct);

        if (session is null)
        {
            return false;
        }

        // Повідомлення та зв'язки з вакансіями видаляє каскад FK
        _context.ChatSessions.Remove(session);
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
