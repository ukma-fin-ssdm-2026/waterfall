// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.Extensions.AI;

namespace Waterfall.Services.Llm;

/// <summary>
/// Дає клієнт <see cref="IChatClient"/> для ролі. Ланцюжок провайдерів береться
/// з конфігурації, а кожна ланка вже обгорнута <see cref="FallbackChatClient"/>:
/// перемикання на наступного провайдера відбувається лише на таймауті, 429, 5xx
/// або збої мережі (REL-003).
/// </summary>
public interface ILlmClientProvider
{
    /// <summary>
    /// Клієнт для ролі: ланцюжок провайдерів із конфігурації, обгорнутий FallbackChatClient.
    /// Модель для кожної ланки підставляється з конфігурації (<c>ChatOptions.ModelId</c>).
    /// </summary>
    /// <param name="role">Роль, для якої потрібен клієнт.</param>
    /// <returns>
    /// Клієнт, який кидає <see cref="LlmUnavailableException"/> з
    /// <see cref="LlmFailureKind.NotConfigured"/>, якщо для ролі немає жодної придатної ланки.
    /// </returns>
    IChatClient GetClient(LlmRole role);
}
