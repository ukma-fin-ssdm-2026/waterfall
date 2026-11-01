// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Waterfall.Services.Models.Ai;

/// <summary>
/// Тіло запиту до ШІ-консультанта: нова репліка в діалозі.
/// Імена полів — snake_case, як у контракті API (клієнт MAUI надсилає саме їх).
/// </summary>
public record AIConsultantMessageRequest : IValidatableObject
{
    /// <summary>
    /// Ідентифікатор діалогу; null означає новий діалог.
    /// </summary>
    /// <example>6f9619ff-8b86-d011-b42d-00c04fc964ff</example>
    [JsonPropertyName("session_id")]
    public Guid? SessionId { get; init; }

    /// <summary>
    /// Текст запиту природною мовою (1..1000 символів).
    /// </summary>
    /// <example>Хочу віддалену роботу на Python для джуніора</example>
    [Required(ErrorMessage = "Поле text є обов'язковим")]
    [StringLength(1000, MinimumLength = 1, ErrorMessage = "Довжина text повинна бути від 1 до 1000 символів")]
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Рядок із самих пробілів формально проходить [Required], але це не запит до ШІ
        if (!string.IsNullOrEmpty(Text) && string.IsNullOrWhiteSpace(Text))
        {
            yield return new ValidationResult(
                "Поле text не може складатися лише з пробілів",
                [nameof(Text)]);
        }
    }
}
