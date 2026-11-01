// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json.Serialization;
using Waterfall.Services.Models.Ai;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Api.Models.Ai;

/// <summary>
/// Імена SSE-подій ШІ-консультанта. Спільні для <c>/messages</c> (#4) і <c>/analyze</c> (#5),
/// щоб клієнт мав один парсер.
/// </summary>
public static class AiConsultantEventNames
{
    /// <summary>Ідентифікатор діалогу та застосований фільтр.</summary>
    public const string Meta = "meta";

    /// <summary>Список вакансій із БД.</summary>
    public const string Vacancies = "vacancies";

    /// <summary>Порція тексту відповіді.</summary>
    public const string Delta = "delta";

    /// <summary>Завершення відповіді.</summary>
    public const string Done = "done";

    /// <summary>Термінальна помилка після початку стрімінгу.</summary>
    public const string Error = "error";
}

/// <summary>
/// Коди помилок ШІ-консультанта у SSE-події <c>error</c> (позастрімові помилки віддаються
/// звичайним <c>ApiErrorResponse</c> з тими самими кодами).
/// </summary>
public static class AiConsultantErrorCodes
{
    /// <summary>LLM недоступна або вичерпано її ліміт (REL-003).</summary>
    public const string LlmUnavailable = "LLM_UNAVAILABLE";

    /// <summary>Внутрішня помилка сервера під час стрімінгу.</summary>
    public const string Internal = "INTERNAL";

    /// <summary>Перевищено ліміт запитів користувача (THR-10).</summary>
    public const string RateLimited = "RATE_LIMITED";
}

/// <summary>
/// Дані події <c>meta</c>.
/// </summary>
public sealed record MetaEventPayload
{
    /// <summary>Ідентифікатор діалогу.</summary>
    [JsonPropertyName("session_id")]
    public Guid SessionId { get; init; }

    /// <summary>Застосований структурований фільтр.</summary>
    [JsonPropertyName("applied_filter")]
    public AppliedFilter AppliedFilter { get; init; } = AppliedFilter.Empty;
}

/// <summary>
/// Дані події <c>vacancies</c>.
/// </summary>
public sealed record VacanciesEventPayload
{
    /// <summary>Вакансії з БД (FUNC-001).</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<VacancyDto> Items { get; init; } = Array.Empty<VacancyDto>();

    /// <summary>true — точний збіг; false — застосовано послаблення критеріїв.</summary>
    [JsonPropertyName("exact")]
    public bool Exact { get; init; }

    /// <summary>Ключі фільтра, які довелося зняти (не більше трьох).</summary>
    [JsonPropertyName("relaxed")]
    public IReadOnlyList<string> Relaxed { get; init; } = Array.Empty<string>();

    /// <summary>Пояснення, що вакансії відповідають запиту не на 100%.</summary>
    [JsonPropertyName("disclaimer")]
    public string? Disclaimer { get; init; }
}

/// <summary>
/// Дані події <c>delta</c>.
/// </summary>
public sealed record DeltaEventPayload
{
    /// <summary>Порція тексту відповіді.</summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

/// <summary>
/// Дані події <c>done</c> для <c>/messages</c>.
/// </summary>
public sealed record DoneEventPayload
{
    /// <summary>Ідентифікатор збереженого повідомлення ШІ.</summary>
    [JsonPropertyName("message_id")]
    public long MessageId { get; init; }
}

/// <summary>
/// Дані термінальної події <c>error</c>.
/// </summary>
public sealed record StreamErrorEventPayload
{
    /// <summary>Машинний код помилки (<see cref="AiConsultantErrorCodes"/>).</summary>
    [JsonPropertyName("code")]
    public string Code { get; init; } = AiConsultantErrorCodes.Internal;

    /// <summary>Текст помилки для користувача.</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}
