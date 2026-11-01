// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Waterfall.Services.Models.Ai;

/// <summary>
/// Тіло запиту на аналіз вакансій (FR-010): від 1 до 3 ідентифікаторів вакансій каталогу.
/// Ім'я поля — snake_case, як у контракті API (клієнт MAUI надсилає саме його).
/// </summary>
public record AIAnalysisRequest
{
    /// <summary>
    /// Ідентифікатори вакансій для аналізу (1..3; дублікати згортаються).
    /// </summary>
    /// <example>["11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222"]</example>
    [Required(ErrorMessage = "Поле vacancy_ids є обов'язковим")]
    [MinLength(1, ErrorMessage = "Потрібно передати від 1 до 3 вакансій")]
    [MaxLength(3, ErrorMessage = "Потрібно передати від 1 до 3 вакансій")]
    [JsonPropertyName("vacancy_ids")]
    public IReadOnlyList<Guid> VacancyIds { get; init; } = Array.Empty<Guid>();
}
