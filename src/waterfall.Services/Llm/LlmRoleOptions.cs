// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Llm;

/// <summary>Ланцюжок резервних провайдерів для однієї ролі.</summary>
public sealed class LlmRoleOptions
{
    /// <summary>Провайдери в порядку спроб: перший — основний, далі — резервні.</summary>
    public List<LlmChainEntryOptions> Chain { get; set; } = [];
}
