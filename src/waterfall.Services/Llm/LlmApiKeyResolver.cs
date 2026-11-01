// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Llm;

/// <summary>
/// Дістає ключ провайдера зі змінної середовища, названу в <see cref="LlmProviderOptions.ApiKeyEnv"/>,
/// або з конфігурації (<c>Llm:Providers:&lt;name&gt;:ApiKey</c> з User Secrets / env).
/// Ключ ніколи не пишеться в журнал (THR-09).
/// </summary>
public static class LlmApiKeyResolver
{
    /// <summary>Повертає ключ або <c>null</c>, якщо провайдер не налаштований.</summary>
    /// <param name="provider">Налаштування провайдера.</param>
    public static string? Resolve(LlmProviderOptions provider)
    {
        if (!string.IsNullOrWhiteSpace(provider.ApiKeyEnv))
        {
            string? fromEnvironment = Environment.GetEnvironmentVariable(provider.ApiKeyEnv);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return fromEnvironment;
            }
        }

        return string.IsNullOrWhiteSpace(provider.ApiKey) ? null : provider.ApiKey;
    }
}
