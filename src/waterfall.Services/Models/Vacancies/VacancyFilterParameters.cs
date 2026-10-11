// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: посилено валідацію вхідних даних (issue #49); перевірено: прогін негативних тест-кейсів (docs/api/quality-attributes.md, розділ 6)

using System.ComponentModel.DataAnnotations;

namespace Waterfall.Services.Models.Vacancies;

/// <summary>
/// Параметри фільтрації, сортування та пагінації для пошуку вакансій.
/// </summary>
public class VacancyFilterParameters : IValidatableObject
{
    private int _page = 1;
    private int _pageSize = 10;

    /// <summary>
    /// Номер запитуваної сторінки (нумерація з 1).
    /// </summary>
    /// <example>1</example>
    // Верхня межа не дає (page - 1) * pageSize переповнити int: від'ємний OFFSET на SQL Server — це 500
    [Range(1, 100_000, ErrorMessage = "Номер сторінки повинен бути в межах від 1 до 100 000")]
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    /// <summary>
    /// Кількість елементів на одній сторінці (від 1 до 100).
    /// </summary>
    /// <example>10</example>
    [Range(1, 100, ErrorMessage = "Розмір сторінки повинен бути в діапазоні від 1 до 100")]
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => 10,
            > 100 => 100,
            _ => value
        };
    }

    /// <summary>
    /// Пошуковий запит (пошук за назвою посади, компанією або навичками).
    /// </summary>
    /// <example>.NET</example>
    [StringLength(100, ErrorMessage = "Довжина search не може перевищувати 100 символів")]
    public string? Search { get; set; }

    /// <summary>
    /// Фільтр за категорією/спеціалізацією (.NET, QA, Frontend, DevOps).
    /// </summary>
    /// <example>.NET</example>
    [StringLength(100, ErrorMessage = "Довжина category не може перевищувати 100 символів")]
    public string? Category { get; set; }

    /// <summary>
    /// Мінімальна заробітна плата.
    /// </summary>
    /// <example>2000</example>
    [Range(0, 1000000, ErrorMessage = "min_salary повинна бути від 0 до 1 000 000")]
    public decimal? MinSalary { get; set; }

    /// <summary>
    /// Максимальна заробітна плата.
    /// </summary>
    /// <example>5000</example>
    [Range(0, 1000000, ErrorMessage = "max_salary повинна бути від 0 до 1 000 000")]
    public decimal? MaxSalary { get; set; }

    /// <summary>
    /// Фільтр за необхідним досвідом.
    /// </summary>
    /// <example>3+ роки</example>
    [StringLength(100, ErrorMessage = "Довжина experience не може перевищувати 100 символів")]
    public string? Experience { get; set; }

    /// <summary>
    /// Фільтр за джерелом агрегації (WorkUa, RobotaUa, Djinni, Dou).
    /// </summary>
    /// <example>Djinni</example>
    public JobSource? Source { get; set; }

    /// <summary>
    /// Ознака вибірки тільки активних вакансій (за замовчуванням true).
    /// Якщо false — повертає як активні, так і закриті (архівні) вакансії.
    /// </summary>
    /// <example>true</example>
    public bool OnlyActive { get; set; } = true;

    /// <summary>
    /// Поле для сортування (title, publishedAt, salaryMin, salaryMax).
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>
    /// Напрямок сортування (asc, desc).
    /// </summary>
    public string? SortOrder { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MinSalary.HasValue && MaxSalary.HasValue && MinSalary > MaxSalary)
        {
            yield return new ValidationResult(
                "minSalary не може перевищувати maxSalary",
                ["minSalary", "maxSalary"]);
        }
    }
}
