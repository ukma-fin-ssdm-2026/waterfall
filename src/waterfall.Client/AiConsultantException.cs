// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Net;
using Waterfall.Client.Models.Common;

namespace Waterfall.Client;

/// <summary>
/// Помилка ендпоінтів ШІ-консультанта до початку SSE-потоку (400/401/403/404/429/503).
/// Це той самий <see cref="ApiException"/> для ViewModel-ей, але з машинним кодом сервера,
/// щоб UI міг показати специфічне повідомлення (REL-003) замість загального тексту.
/// </summary>
public sealed class AiConsultantException : ApiException
{
    public const string RateLimitedCode = "RATE_LIMITED";
    public const string LlmUnavailableCode = "LLM_UNAVAILABLE";

    public AiConsultantException(HttpStatusCode? statusCode, ApiError? error, string message, string? code, Exception? innerException = null)
        : base(statusCode, error, message, innerException)
    {
        Code = code;
    }

    /// <summary>
    /// Машинний код сервера (<c>RATE_LIMITED</c>, <c>LLM_UNAVAILABLE</c>, …), якщо він був у відповіді.
    /// </summary>
    public string? Code { get; }

    /// <summary>
    /// Перевищено ліміт запитів до ШІ (429 / <c>RATE_LIMITED</c>).
    /// </summary>
    public bool IsRateLimited => Code == RateLimitedCode || StatusCode == HttpStatusCode.TooManyRequests;

    /// <summary>
    /// LLM недоступна (503 / <c>LLM_UNAVAILABLE</c>) — REL-003: пропонуємо звичайний пошук.
    /// </summary>
    public bool IsLlmUnavailable => Code == LlmUnavailableCode || StatusCode == HttpStatusCode.ServiceUnavailable;
}
