// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Client.Models.Profile;

/// <summary>
/// Запит на запис перегляду вакансії в історію (FR-007, US-008).
/// </summary>
public record RecordVacancyViewRequest
{
    public Guid VacancyId { get; init; }
}
