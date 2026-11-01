// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Llm;

/// <summary>
/// Налаштування LLM-шлюзу із секції <c>Llm</c> у конфігурації. Увесь вибір
/// провайдерів, моделей і порядку резервування живе в конфігурації, а ключі —
/// лише в змінних середовища або .NET User Secrets (THR-09).
/// </summary>
public sealed class LlmOptions
{
    /// <summary>Назва секції конфігурації.</summary>
    public const string SectionName = "Llm";

    /// <summary>
    /// Провайдери за назвою (вона ж використовується в ланцюжках ролей).
    /// </summary>
    public Dictionary<string, LlmProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ланцюжки провайдерів за роллю (<see cref="LlmRole"/>).</summary>
    public Dictionary<string, LlmRoleOptions> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Граничний час однієї спроби; після нього переходимо до наступної ланки.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(12);

    /// <summary>Граничний час усього ланцюжка (REL-003: користувач має отримати відповідь за 30 с).</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
