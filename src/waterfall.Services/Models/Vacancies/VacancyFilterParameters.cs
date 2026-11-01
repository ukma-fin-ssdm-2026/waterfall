// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: посилено валідацію вхідних даних (issue #49); перевірено: прогін негативних тест-кейсів (docs/api/quality-attributes.md, розділ 6)
// AI: фільтри нормалізованих атрибутів вакансії (формат роботи, досвід у місяцях, широка категорія, локація, навички); перевірено: тести VacancyFilterParametersTests

using System.ComponentModel.DataAnnotations;
using Waterfall.Repository.Entities;

namespace Waterfall.Services.Models.Vacancies;

/// <summary>
/// Параметри фільтрації, сортування та пагінації для пошуку вакансій.
/// </summary>
public class VacancyFilterParameters : IValidatableObject
{
    /// <summary>Скільки навичок приймаємо за один запит: більше — уже не пошук, а перебір усього каталогу.</summary>
    public const int MaxSkills = 20;

    /// <summary>Максимальна довжина одного терміна навички.</summary>
    public const int MaxSkillLength = 100;

    private int _page = 1;
    private int _pageSize = 10;
    private IReadOnlyList<string>? _skills;

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
    /// Верхня межа мінімального досвіду в місяцях, включно з межею (0 — без досвіду, 12 — від року).
    /// Вакансії з невідомим досвідом не відсіюються: невідоме не означає «забагато».
    /// </summary>
    /// <example>24</example>
    [Range(0, 600, ErrorMessage = "maxExperienceMonths повинен бути від 0 до 600")]
    public int? MaxExperienceMonths { get; set; }

    /// <summary>
    /// Фільтр за форматом роботи. Прапорці: можна вибрати кілька форматів — повертаються вакансії,
    /// які дозволяють будь-який із них.
    /// </summary>
    /// <example>Remote</example>
    public WorkFormat? WorkFormat { get; set; }

    /// <summary>
    /// Фільтр за широкою канонічною категорією з довідника <see cref="CategoryGroups.All"/> — точний збіг.
    /// </summary>
    /// <example>Software Development</example>
    [StringLength(100, ErrorMessage = "Довжина categoryGroup не може перевищувати 100 символів")]
    public string? CategoryGroup { get; set; }

    /// <summary>
    /// Фільтр за містом: підрядок без урахування регістру, зокрема кирилицею.
    /// </summary>
    /// <example>Київ</example>
    [StringLength(200, ErrorMessage = "Довжина location не може перевищувати 200 символів")]
    public string? Location { get; set; }

    /// <summary>
    /// Навички для пошуку. Кожен термін має знайтися в тегах або в тексті вакансії, терміни поєднуються через AND.
    /// Приймається і повторюваний параметр (<c>?skills=C%23&amp;skills=.NET</c>), і список через кому (<c>?skills=C%23,.NET</c>).
    /// </summary>
    /// <example>["C#", "SQL"]</example>
    public IReadOnlyList<string>? Skills
    {
        get => _skills;
        set => _skills = SplitSkills(value);
    }

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

        if (Skills is { Count: > MaxSkills } many)
        {
            yield return new ValidationResult(
                $"Кількість навичок у skills не може перевищувати {MaxSkills}, а задано {many.Count}",
                ["skills"]);
        }

        if (Skills?.Any(skill => skill.Length > MaxSkillLength) == true)
        {
            yield return new ValidationResult(
                $"Довжина кожної навички в skills не може перевищувати {MaxSkillLength} символів",
                ["skills"]);
        }
    }

    // приймаємо обидва варіанти й прибираємо порожні терміни та повтори
    private static IReadOnlyList<string>? SplitSkills(IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return null;
        }

        List<string> terms = values
            .Where(value => value is not null)
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return terms.Count == 0 ? null : terms;
    }
}
