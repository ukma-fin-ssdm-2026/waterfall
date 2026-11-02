// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Llm;

/// <summary>
/// Допустимі типи провайдерів. <see cref="OpenAiCompatible"/> покриває OpenRouter,
/// Gemini через OpenAI-сумісний endpoint і локальний Ollama; <see cref="Anthropic"/> —
/// офіційний Anthropic API (ADR-004).
/// </summary>
public static class LlmProviderKinds
{
    /// <summary>Будь-який endpoint, сумісний з OpenAI Chat Completions.</summary>
    public const string OpenAiCompatible = "OpenAiCompatible";

    /// <summary>Офіційний Anthropic Messages API.</summary>
    public const string Anthropic = "Anthropic";

    /// <summary>Усі підтримувані типи (для валідації конфігурації).</summary>
    public static IReadOnlyList<string> All { get; } = [OpenAiCompatible, Anthropic];
}
