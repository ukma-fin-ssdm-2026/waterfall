// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Llm;

/// <summary>
/// Налаштування одного LLM-провайдера. Ключ ніколи не зберігається в репозиторії:
/// його беруть зі змінної середовища <see cref="ApiKeyEnv"/> або з
/// <c>Llm:Providers:&lt;name&gt;:ApiKey</c> у User Secrets / змінних середовища (THR-09).
/// </summary>
public sealed class LlmProviderOptions
{
    /// <summary>Тип провайдера: <see cref="LlmProviderKinds.OpenAiCompatible"/> або <see cref="LlmProviderKinds.Anthropic"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Базова адреса API; має бути абсолютним http(s)-URL.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Назва змінної середовища з ключем (наприклад, <c>OPENROUTER_API_KEY</c>).</summary>
    public string? ApiKeyEnv { get; set; }

    /// <summary>
    /// Ключ із конфігурації (User Secrets або змінна середовища <c>Llm__Providers__&lt;name&gt;__ApiKey</c>).
    /// Використовується, лише якщо змінна з <see cref="ApiKeyEnv"/> не задана.
    /// </summary>
    public string? ApiKey { get; set; }
}
