// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Client.Models.SavedVacancies;

/// <summary>
/// Тіло POST /api/v1/saved-vacancies.
/// </summary>
public record SaveVacancyRequest
{
    public Guid VacancyId { get; init; }

    /// <summary>
    /// Нотатки (до 500 символів, перевіряє сервер).
    /// </summary>
    public string? Notes { get; init; }
}
