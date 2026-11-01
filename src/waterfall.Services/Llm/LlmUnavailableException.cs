// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Llm;

/// <summary>
/// LLM недоступна: усі ланки ланцюжка вичерпано або їх не було. Верхній рівень
/// (ендпоінт ШІ-консультанта) перетворює цей виняток на 503 <c>LLM_UNAVAILABLE</c>,
/// тож відмова ШІ не блокує решту функцій застосунку (REL-003).
/// </summary>
public sealed class LlmUnavailableException : Exception
{
    /// <summary>
    /// Створює виняток із типовим (безпечним для клієнта) текстом для вказаної причини.
    /// </summary>
    /// <param name="kind">Причина недоступності LLM.</param>
    public LlmUnavailableException(LlmFailureKind kind)
        : this(kind, BuildDefaultMessage(kind))
    {
    }

    /// <summary>
    /// Створює виняток із власним текстом. Текст не повинен містити запит або відповідь
    /// користувача (THR-06, THR-08).
    /// </summary>
    /// <param name="kind">Причина недоступності LLM.</param>
    /// <param name="message">Безпечний опис для журналу та клієнта.</param>
    /// <param name="innerException">Остання помилка провайдера (може бути <c>null</c>).</param>
    public LlmUnavailableException(LlmFailureKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    /// <summary>Причина, через яку ланцюжок не дав відповіді.</summary>
    public LlmFailureKind Kind { get; }

    private static string BuildDefaultMessage(LlmFailureKind kind) => kind switch
    {
        LlmFailureKind.NotConfigured => "LLM-провайдер не налаштований: для цієї ролі немає ключа або ланцюжка.",
        LlmFailureKind.Timeout => "LLM не встигла відповісти за відведений час.",
        LlmFailureKind.RateLimited => "LLM-провайдер обмежив частоту запитів.",
        LlmFailureKind.ServerError => "LLM-провайдер недоступний або повернув помилку.",
        _ => "LLM недоступна."
    };
}
