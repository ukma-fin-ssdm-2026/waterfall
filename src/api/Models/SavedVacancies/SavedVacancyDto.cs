// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json.Serialization;
using Waterfall.Api.Models.Vacancies;

namespace Waterfall.Api.Models.SavedVacancies;
/// <summary>
/// Представлення збереженої («лайкнутої») вакансії користувача.
/// </summary>
public class SavedVacancyDto
{
    /// <summary>
    /// Ідентифікатор запису збереженої вакансії.
    /// </summary>
    /// <example>9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d</example>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>
    /// Ідентифікатор вакансії, яку збережено.
    /// </summary>
    /// <example>3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    [JsonPropertyName("vacancy_id")]
    public Guid VacancyId { get; init; }

    /// <summary>
    /// Короткі дані збереженої вакансії.
    /// </summary>
    [JsonPropertyName("vacancy")]
    public VacancyDto? Vacancy { get; init; }

    /// <summary>
    /// Особисті примітки шукача роботи до вакансії.
    /// </summary>
    /// <example>Відправити CV у понеділок, звернути увагу на вимоги до Docker.</example>
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    /// <summary>
    /// Дата та час збереження вакансії.
    /// </summary>
    /// <example>2026-09-29T14:30:00Z</example>
    [JsonPropertyName("saved_at")]
    public DateTimeOffset SavedAt { get; init; }
}
