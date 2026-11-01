// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;

namespace Waterfall.Services.Models.Profile;

/// <summary>
/// Модель запиту на запис виконаного пошукового запиту в історію (FR-007).
/// </summary>
public record RecordSearchRequest
{
    /// <summary>
    /// Текст виконаного пошукового запиту.
    /// </summary>
    /// <example>Python розробник</example>
    [Required(ErrorMessage = "Поле query є обов'язковим")]
    [StringLength(300, ErrorMessage = "Довжина query не може перевищувати 300 символів")]
    public string Query { get; init; } = string.Empty;
}
