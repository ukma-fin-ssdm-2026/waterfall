// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// Посилено валідацію вхідних даних (issue #49); перевірено: прогін негативних тест-кейсів через Swagger UI (docs/api/quality-attributes.md, розділ 6)

using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Waterfall.Api.Models.Vacancies;
/// <summary>
/// Параметри пагінації та фільтрації каталогу вакансій.
/// </summary>
public class VacancyFilterParameters : IValidatableObject
{
    /// <summary>
    /// Номер запитуваної сторінки (починається з 1).
    /// </summary>
    /// <example>1</example>
    [FromQuery(Name = "page")]
    // Верхня межа не дає (page - 1) * page_size переповнити int під час пагінації
    [Range(1, 100_000, ErrorMessage = "Номер сторінки 'page' повинен бути в межах від 1 до 100 000.")]
    public int Page { get; set; } = 1;

    /// <summary>
    /// Кількість елементів на одній сторінці (від 1 до 100).
    /// </summary>
    /// <example>10</example>
    [FromQuery(Name = "page_size")]
    [Range(1, 100, ErrorMessage = "Розмір сторінки 'page_size' повинен бути в межах від 1 до 100.")]
    public int PageSize { get; set; } = 10;

    /// <summary>
    /// Пошуковий запит (пошук за назвою посади, компанією або навичками).
    /// </summary>
    /// <example>.NET</example>
    [FromQuery(Name = "search")]
    [StringLength(100, ErrorMessage = "Пошуковий запит 'search' не може перевищувати 100 символів.")]
    public string? Search { get; set; }

    /// <summary>
    /// Фільтр за категорією/спеціалізацією (наприклад, .NET, QA, DevOps).
    /// </summary>
    /// <example>.NET</example>
    [FromQuery(Name = "category")]
    [StringLength(50, ErrorMessage = "Назва категорії не може перевищувати 50 символів.")]
    public string? Category { get; set; }

    /// <summary>
    /// Фільтр за мінімальною заробітною платою.
    /// </summary>
    /// <example>2000</example>
    [FromQuery(Name = "min_salary")]
    [Range(0, 1_000_000, ErrorMessage = "Параметр 'min_salary' повинен бути невід'ємним числом до 1,000,000.")]
    public decimal? MinSalary { get; set; }

    /// <summary>
    /// Фільтр за максимальною заробітною платою.
    /// </summary>
    /// <example>5000</example>
    [FromQuery(Name = "max_salary")]
    [Range(0, 1_000_000, ErrorMessage = "Параметр 'max_salary' повинен бути невід'ємним числом до 1,000,000.")]
    public decimal? MaxSalary { get; set; }

    /// <summary>
    /// Фільтр за необхідним досвідом (наприклад: 3+ роки, 1-3 роки).
    /// </summary>
    /// <example>3+ роки</example>
    [FromQuery(Name = "experience")]
    [StringLength(50, ErrorMessage = "Параметр 'experience' не може перевищувати 50 символів.")]
    public string? Experience { get; set; }

    /// <summary>
    /// Фільтр за джерелом агрегації (WorkUa, RobotaUa, Djinni, Dou).
    /// </summary>
    /// <example>Djinni</example>
    [FromQuery(Name = "source")]
    public JobSource? Source { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinSalary.HasValue && MaxSalary.HasValue && MinSalary > MaxSalary)
        {
            yield return new ValidationResult(
                "Параметр 'min_salary' не може перевищувати 'max_salary'.",
                ["min_salary", "max_salary"]);
        }
    }
}
