// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json.Serialization;
using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.Models.Ai;

/// <summary>
/// Подія SSE-потоку ШІ-консультанта (POST /api/v1/ai-consultant/messages та /analyze).
/// Кожен нащадок відповідає події <c>event:</c> контракту сервера.
/// </summary>
public abstract record AiStreamEvent;

/// <summary>
/// Відкрита сесія діалогу та фільтр, який сервер витяг із запиту.
/// </summary>
public sealed record AiMetaEvent : AiStreamEvent
{
    [JsonPropertyName("session_id")]
    public Guid SessionId { get; init; }

    [JsonPropertyName("applied_filter")]
    public AiAppliedFilter? AppliedFilter { get; init; }
}

/// <summary>
/// Фільтр, застосований сервером до пошуку вакансій (лише для показу / діагностики).
/// </summary>
public sealed record AiAppliedFilter
{
    [JsonPropertyName("search")]
    public string? Search { get; init; }

    [JsonPropertyName("category_group")]
    public string? CategoryGroup { get; init; }

    /// <summary>Значення: <c>remote</c>, <c>hybrid</c>, <c>office</c>.</summary>
    [JsonPropertyName("work_format")]
    public IReadOnlyList<string>? WorkFormat { get; init; }

    [JsonPropertyName("max_experience_months")]
    public int? MaxExperienceMonths { get; init; }

    [JsonPropertyName("min_salary")]
    public decimal? MinSalary { get; init; }

    [JsonPropertyName("max_salary")]
    public decimal? MaxSalary { get; init; }

    [JsonPropertyName("location")]
    public string? Location { get; init; }

    [JsonPropertyName("skills")]
    public IReadOnlyList<string>? Skills { get; init; }
}

/// <summary>
/// Знайдені вакансії (лише з бази — FUNC-001) та ознака неповної відповідності запиту (US-005 AC-3).
/// </summary>
public sealed record AiVacanciesEvent : AiStreamEvent
{
    [JsonPropertyName("items")]
    public IReadOnlyList<VacancyDto> Items { get; init; } = Array.Empty<VacancyDto>();

    /// <summary>
    /// false — точних збігів не знайдено, у <see cref="Relaxed"/> перелічено зняті фільтри.
    /// </summary>
    [JsonPropertyName("exact")]
    public bool Exact { get; init; }

    [JsonPropertyName("relaxed")]
    public IReadOnlyList<string> Relaxed { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Пояснення сервера, чому вакансії відповідають запиту не на 100%.
    /// </summary>
    [JsonPropertyName("disclaimer")]
    public string? Disclaimer { get; init; }
}

/// <summary>
/// Черговий фрагмент тексту відповіді; клієнт дописує його в те саме повідомлення.
/// </summary>
public sealed record AiDeltaEvent : AiStreamEvent
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

/// <summary>
/// Потік завершено успішно. <see cref="MessageId"/> відсутній у відповіді /analyze.
/// </summary>
public sealed record AiDoneEvent : AiStreamEvent
{
    [JsonPropertyName("message_id")]
    public long? MessageId { get; init; }
}

/// <summary>
/// Термінальна помилка потоку (після неї подій не буде).
/// </summary>
public sealed record AiErrorEvent : AiStreamEvent
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Детермінована оцінка релевантності вакансій до збережених вакансій користувача (FR-010).
/// </summary>
public sealed record AiRelevanceEvent : AiStreamEvent
{
    [JsonPropertyName("items")]
    public IReadOnlyList<AiRelevanceItem> Items { get; init; } = Array.Empty<AiRelevanceItem>();
}

/// <summary>
/// Релевантність однієї вакансії; <see cref="Score"/> = null, коли користувач ще нічого не зберіг.
/// </summary>
public sealed record AiRelevanceItem
{
    [JsonPropertyName("vacancy_id")]
    public Guid VacancyId { get; init; }

    [JsonPropertyName("score")]
    public int? Score { get; init; }

    [JsonPropertyName("matched")]
    public IReadOnlyList<string> Matched { get; init; } = Array.Empty<string>();

    [JsonPropertyName("basis_count")]
    public int BasisCount { get; init; }
}

/// <summary>
/// Тіло POST /api/v1/ai-consultant/messages; <see cref="SessionId"/> = null — нова сесія.
/// </summary>
public sealed record AiMessageRequest
{
    [JsonPropertyName("session_id")]
    public Guid? SessionId { get; init; }

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

/// <summary>
/// Тіло POST /api/v1/ai-consultant/analyze (1–3 вакансії).
/// </summary>
public sealed record AiAnalyzeRequest
{
    [JsonPropertyName("vacancy_ids")]
    public IReadOnlyList<Guid> VacancyIds { get; init; } = Array.Empty<Guid>();
}
