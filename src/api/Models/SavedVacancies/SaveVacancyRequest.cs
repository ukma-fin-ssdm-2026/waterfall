// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// Посилено валідацію вхідних даних (issue #49); перевірено: прогін негативних тест-кейсів через Swagger UI (docs/api/quality-attributes.md, розділ 6)

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Waterfall.Api.Models.SavedVacancies;
/// <summary>
/// Тіло запиту для збереження («лайку») вакансії.
/// </summary>
public class SaveVacancyRequest : IValidatableObject
{
    /// <summary>
    /// Унікальний ідентифікатор вакансії, яку потрібно додати у збережені.
    /// </summary>
    /// <example>3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    [Required(ErrorMessage = "Параметр vacancy_id є обов'язковим.")]
    [JsonPropertyName("vacancy_id")]
    public Guid VacancyId { get; set; }

    /// <summary>
    /// Необов'язкові власні нотатки користувача щодо цієї вакансії (до 500 символів).
    /// </summary>
    /// <example>Цікавий стек технологій, підготувати запитання про процеси тестування.</example>
    [StringLength(500, ErrorMessage = "Нотатки не можуть перевищувати 500 символів.")]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // [Required] не спрацьовує для Guid: пропущене поле стає Guid.Empty,
        // і без цієї перевірки клієнт отримував 404 замість 400
        if (VacancyId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Параметр vacancy_id є обов'язковим і не може бути порожнім GUID.",
                ["vacancy_id"]);
        }
    }
}
