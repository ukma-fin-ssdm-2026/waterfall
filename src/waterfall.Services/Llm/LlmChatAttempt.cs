// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.Extensions.AI;

namespace Waterfall.Services.Llm;

/// <summary>
/// Одна спроба звернення до LLM: провайдер, модель і готовий клієнт.
/// </summary>
public sealed class LlmChatAttempt
{
    /// <summary>Створює ланку ланцюжка.</summary>
    /// <param name="provider">Назва провайдера з конфігурації.</param>
    /// <param name="model">Ідентифікатор моделі для цієї ланки.</param>
    /// <param name="client">Клієнт, який виконує запит.</param>
    public LlmChatAttempt(string provider, string model, IChatClient client)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(client);

        Provider = provider;
        Model = model;
        Client = client;
    }

    /// <summary>Назва провайдера (лише для журналу, не для запиту).</summary>
    public string Provider { get; }

    /// <summary>Модель, яка підставляється в <see cref="ChatOptions.ModelId"/> для цієї спроби.</summary>
    public string Model { get; }

    /// <summary>Клієнт провайдера.</summary>
    public IChatClient Client { get; }
}
