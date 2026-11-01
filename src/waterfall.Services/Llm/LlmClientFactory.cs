// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Waterfall.Services.Llm;

/// <summary>
/// Створює <see cref="IChatClient"/> для одного провайдера й моделі. Обидва типи
/// провайдерів реалізовано офіційними SDK (ADR-004), тож шлюз не пише власний HTTP-клієнт.
/// </summary>
public static class LlmClientFactory
{
    /// <summary>
    /// Створює клієнт для провайдера. Автоматичні повторні спроби SDK вимкнено:
    /// резервування робить <see cref="FallbackChatClient"/>, інакше таймаути й порядок
    /// ланцюжка були б непередбачуваними.
    /// </summary>
    /// <param name="provider">Налаштування провайдера.</param>
    /// <param name="model">Ідентифікатор моделі.</param>
    /// <param name="apiKey">Ключ доступу (не журналюється).</param>
    /// <returns>Клієнт, придатний для передачі в ланцюжок резервування.</returns>
    public static IChatClient Create(LlmProviderOptions provider, string model, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        if (string.Equals(provider.Kind, LlmProviderKinds.OpenAiCompatible, StringComparison.OrdinalIgnoreCase))
        {
            OpenAIClientOptions clientOptions = new()
            {
                Endpoint = new Uri(provider.BaseUrl, UriKind.Absolute),
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
            };

            OpenAIClient openAiClient = new(new ApiKeyCredential(apiKey), clientOptions);
            return openAiClient.GetChatClient(model).AsIChatClient();
        }

        if (string.Equals(provider.Kind, LlmProviderKinds.Anthropic, StringComparison.OrdinalIgnoreCase))
        {
            Anthropic.AnthropicClient anthropicClient = new()
            {
                ApiKey = apiKey,
                BaseUrl = provider.BaseUrl,
                MaxRetries = 0
            };

            return anthropicClient.AsIChatClient(model);
        }

        throw new InvalidOperationException(
            $"Невідомий тип LLM-провайдера '{provider.Kind}'. Допустимі: {string.Join(", ", LlmProviderKinds.All)}.");
    }
}
