// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Llm;

/// <summary>Одна ланка ланцюжка ролі: провайдер і його модель.</summary>
public sealed class LlmChainEntryOptions
{
    /// <summary>Назва провайдера з <see cref="LlmOptions.Providers"/>.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Ідентифікатор моделі в термінах цього провайдера.</summary>
    public string Model { get; set; } = string.Empty;
}
