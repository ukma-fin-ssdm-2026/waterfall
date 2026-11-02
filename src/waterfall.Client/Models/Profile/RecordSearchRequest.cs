// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Client.Models.Profile;

/// <summary>
/// Запит на запис виконаного пошукового запиту в історію (FR-007, US-008).
/// </summary>
public record RecordSearchRequest
{
    public string Query { get; init; } = string.Empty;
}
