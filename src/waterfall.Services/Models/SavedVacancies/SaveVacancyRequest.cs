// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;

namespace Waterfall.Services.Models.SavedVacancies;

/// <summary>
/// Модель запиту на збереження вакансії у закладки («лайк»).
/// </summary>
public record SaveVacancyRequest
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
}
