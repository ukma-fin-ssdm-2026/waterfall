// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: посилено валідацію вхідних даних (issue #49); перевірено: прогін негативних тест-кейсів (docs/api/quality-attributes.md, розділ 6)

using System.ComponentModel.DataAnnotations;

namespace Waterfall.Services.Models.SavedVacancies;

/// <summary>
/// Модель запиту на збереження вакансії у закладки («лайк»).
/// </summary>
public record SaveVacancyRequest : IValidatableObject
{
    /// <summary>
    /// Ідентифікатор вакансії, яку потрібно зберегти.
    /// </summary>
    /// <example>11111111-1111-1111-1111-111111111111</example>
    [Required(ErrorMessage = "Поле vacancyId є обов'язковим")]
    public Guid VacancyId { get; init; }

    /// <summary>
    /// Довільні нотатки кандидата до вакансії (за бажанням).
    /// </summary>
    /// <example>Подати резюме у понеділок</example>
    [StringLength(500, ErrorMessage = "Довжина notes не може перевищувати 500 символів")]
    public string? Notes { get; init; }

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
