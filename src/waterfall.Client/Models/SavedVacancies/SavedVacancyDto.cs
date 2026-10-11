// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.Models.SavedVacancies;

/// <summary>
/// Збережена вакансія («Обране»), відповідь GET /api/v1/saved-vacancies.
/// </summary>
public record SavedVacancyDto
{
    /// <summary>
    /// Ідентифікатор запису збереження.
    /// </summary>
    public Guid Id { get; init; }

    public Guid VacancyId { get; init; }
    public VacancyDto? Vacancy { get; init; }
    public string? Notes { get; init; }
    public DateTimeOffset SavedAt { get; init; }
}
