// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.Extensions.AI;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Llm;
using Waterfall.Services.Models.Ai;
using Waterfall.Services.Models.Common;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Tests;

/// <summary>
/// In-memory репозиторій діалогів для тестів ШІ-консультанта: повторює контракт
/// <see cref="IChatSessionRepository"/> (обмеження власника, вікно історії, зв'язки з вакансіями),
/// щоб тести перевіряли поведінку сервісу, а не EF Core.
/// </summary>
public sealed class FakeChatSessionRepository : IChatSessionRepository
{
    private readonly Dictionary<Guid, ChatSessionDB> _sessions = [];
    private readonly List<ChatMessageDB> _messages = [];
    private readonly List<ChatMessageVacancyDB> _links = [];
    private long _nextMessageId = 1;

    public IReadOnlyList<ChatSessionDB> Sessions => _sessions.Values.ToList();

    public IReadOnlyList<ChatMessageDB> Messages => _messages;

    public IReadOnlyList<ChatMessageVacancyDB> Links => _links;

    public Task<ChatSessionDB?> GetSessionAsync(Guid sessionId, Guid userId, CancellationToken ct = default)
    {
        ChatSessionDB? session = _sessions.GetValueOrDefault(sessionId);
        return Task.FromResult(session is not null && session.UserId == userId ? session : null);
    }

    public Task<ChatSessionDB> CreateSessionAsync(Guid userId, string contextFilterJson, CancellationToken ct = default)
    {
        ChatSessionDB session = new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ContextFilterJson = contextFilterJson,
            StartedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow
        };

        _sessions[session.Id] = session;
        return Task.FromResult(session);
    }

    public Task<IReadOnlyList<ChatMessageDB>> GetRecentMessagesAsync(
        Guid sessionId,
        Guid userId,
        int limit,
        CancellationToken ct = default)
    {
        ChatSessionDB? session = _sessions.GetValueOrDefault(sessionId);
        if (session is null || session.UserId != userId || limit <= 0)
        {
            return Task.FromResult<IReadOnlyList<ChatMessageDB>>([]);
        }

        IReadOnlyList<ChatMessageDB> messages = _messages
            .Where(message => message.SessionId == sessionId)
            .TakeLast(limit)
            .ToList();

        return Task.FromResult(messages);
    }

    public Task<ChatMessageDB?> AddUserMessageAsync(
        Guid sessionId,
        Guid userId,
        string content,
        CancellationToken ct = default)
    {
        ChatSessionDB? session = _sessions.GetValueOrDefault(sessionId);
        if (session is null || session.UserId != userId)
        {
            return Task.FromResult<ChatMessageDB?>(null);
        }

        ChatMessageDB message = new()
        {
            Id = _nextMessageId++,
            SessionId = sessionId,
            Author = ChatMessageAuthor.User,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow
        };

        session.LastActivityAt = message.CreatedAt;
        _messages.Add(message);
        return Task.FromResult<ChatMessageDB?>(message);
    }

    public Task<long?> AddAssistantMessageAsync(
        Guid sessionId,
        Guid userId,
        string content,
        IReadOnlyList<Guid> vacancyIds,
        CancellationToken ct = default)
    {
        ChatSessionDB? session = _sessions.GetValueOrDefault(sessionId);
        if (session is null || session.UserId != userId)
        {
            return Task.FromResult<long?>(null);
        }

        ChatMessageDB message = new()
        {
            Id = _nextMessageId++,
            SessionId = sessionId,
            Author = ChatMessageAuthor.Assistant,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow
        };

        session.LastActivityAt = message.CreatedAt;
        _messages.Add(message);
        foreach (Guid vacancyId in vacancyIds.Distinct())
        {
            _links.Add(new ChatMessageVacancyDB { MessageId = message.Id, VacancyId = vacancyId });
        }

        return Task.FromResult<long?>(message.Id);
    }

    public Task<bool> UpdateContextFilterAsync(
        Guid sessionId,
        Guid userId,
        string contextFilterJson,
        CancellationToken ct = default)
    {
        ChatSessionDB? session = _sessions.GetValueOrDefault(sessionId);
        if (session is null || session.UserId != userId)
        {
            return Task.FromResult(false);
        }

        session.ContextFilterJson = contextFilterJson;
        session.LastActivityAt = DateTimeOffset.UtcNow;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteSessionAsync(Guid sessionId, Guid userId, CancellationToken ct = default)
    {
        ChatSessionDB? session = _sessions.GetValueOrDefault(sessionId);
        if (session is null || session.UserId != userId)
        {
            return Task.FromResult(false);
        }

        _sessions.Remove(sessionId);
        _messages.RemoveAll(message => message.SessionId == sessionId);
        _links.Clear();
        return Task.FromResult(true);
    }
}

/// <summary>
/// Каталог вакансій для тестів: фільтрує заданий набір за тими самими правилами, що й
/// <c>VacancyFilterParameters</c> (будь-який формат, зарплата, досвід, навички),
/// і зберігає всі запити, щоб тести перевірили послаблення критеріїв.
/// </summary>
public sealed class FakeCatalogService : IVacancyService
{
    public List<VacancyDto> Vacancies { get; } = [];

    public List<VacancyFilterParameters> Queries { get; } = [];

    public Task<PagedResult<VacancyDto>> GetAllAsync(
        VacancyFilterParameters filter,
        CancellationToken ct = default,
        Guid? userId = null)
    {
        Queries.Add(filter);

        List<VacancyDto> matches = Vacancies.Where(vacancy => Matches(vacancy, filter)).ToList();
        PagedResult<VacancyDto> page = new()
        {
            Items = matches.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList(),
            TotalCount = matches.Count,
            Page = filter.Page,
            PageSize = filter.PageSize
        };

        return Task.FromResult(page);
    }

    public Task<VacancyDto?> GetByIdAsync(Guid id, CancellationToken ct = default, Guid? userId = null)
        => Task.FromResult(Vacancies.FirstOrDefault(vacancy => vacancy.Id == id));

    public Task<VacancyDto> CreateAsync(CreateVacancyRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Каталог у тестах ШІ-консультанта доступний лише для читання.");

    public Task<VacancyDto?> UpdateAsync(Guid id, UpdateVacancyRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Каталог у тестах ШІ-консультанта доступний лише для читання.");

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => throw new NotSupportedException("Каталог у тестах ШІ-консультанта доступний лише для читання.");

    private static bool Matches(VacancyDto vacancy, VacancyFilterParameters filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Search)
            && !vacancy.Title.Contains(filter.Search, StringComparison.OrdinalIgnoreCase)
            && !vacancy.Description.Contains(filter.Search, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.CategoryGroup)
            && !string.Equals(vacancy.CategoryGroup, filter.CategoryGroup, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (filter.WorkFormat is WorkFormat requestedFormat
            && (vacancy.WorkFormat & requestedFormat) == WorkFormat.None)
        {
            return false;
        }

        if (filter.MaxExperienceMonths is int maxExperience
            && vacancy.ExperienceMonths is int vacancyExperience
            && vacancyExperience > maxExperience)
        {
            return false;
        }

        if (filter.MinSalary is decimal minSalary && (vacancy.SalaryMax ?? vacancy.SalaryMin ?? 0) < minSalary)
        {
            return false;
        }

        if (filter.MaxSalary is decimal maxSalary && (vacancy.SalaryMin ?? vacancy.SalaryMax ?? 0) > maxSalary)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.Location)
            && !vacancy.Location.Contains(filter.Location, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (filter.Skills is { Count: > 0 } requestedSkills
            && requestedSkills.Any(skill => !vacancy.Skills.Contains(skill, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }
}

/// <summary>
/// Провайдер LLM для тестів: окремий скриптований клієнт на кожну роль.
/// </summary>
public sealed class ScriptedLlmClientProvider : ILlmClientProvider
{
    public ScriptedChatClient Extract { get; } = new();

    public ScriptedChatClient Chat { get; } = new();

    public ScriptedChatClient Analyze { get; } = new();

    public IChatClient GetClient(LlmRole role) => role switch
    {
        LlmRole.Extract => Extract,
        LlmRole.Chat => Chat,
        LlmRole.Analyze => Analyze,
        _ => throw new NotSupportedException($"Роль {role} не використовується ШІ-консультантом.")
    };
}
