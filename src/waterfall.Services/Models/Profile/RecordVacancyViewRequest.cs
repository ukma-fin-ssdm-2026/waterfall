// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;

namespace Waterfall.Services.Models.Profile;

/// <summary>
/// Модель запиту на запис перегляду вакансії в історію (FR-007).
/// </summary>
public record RecordVacancyViewRequest : IValidatableObject
{
    /// <summary>
    /// Ідентифікатор переглянутої вакансії.
    /// </summary>
    /// <example>11111111-1111-1111-1111-111111111111</example>
    [Required(ErrorMessage = "Поле vacancyId є обов'язковим")]
    public Guid VacancyId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // [Required] не спрацьовує для Guid: пропущене поле стає Guid.Empty,
        // і без цієї перевірки клієнт отримував 404 замість 400
        if (VacancyId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Поле vacancyId є обов'язковим і не може бути порожнім GUID",
                ["vacancyId"]);
        }
    }
}
