// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Models.SavedVacancies;

/// <summary>
/// DTO представлення збереженої вакансії («лайку» / закладки) для клієнтів API.
/// </summary>
public record SavedVacancyDto
{
    /// <summary>
    /// Унікальний ідентифікатор запису збереження (UUID).
    /// </summary>
    /// <example>99999999-9999-9999-9999-999999999999</example>
    public Guid Id { get; init; }

    /// <summary>
    /// Ідентифікатор самої збереженої вакансії.
    /// </summary>
    /// <example>11111111-1111-1111-1111-111111111111</example>
    public Guid VacancyId { get; init; }

    /// <summary>
    /// Повні дані вакансії, включені в закладку.
    /// </summary>
    public VacancyDto? Vacancy { get; init; }

    /// <summary>
    /// Персональні нотатки кандидата до вакансії (за наявності).
    /// </summary>
    /// <example>Цікавий проєкт на .NET 10, підготувати CV</example>
    public string? Notes { get; init; }

    /// <summary>
    /// Дата та час додавання вакансії до збережених (UTC).
    /// </summary>
    public DateTimeOffset SavedAt { get; init; }
}
