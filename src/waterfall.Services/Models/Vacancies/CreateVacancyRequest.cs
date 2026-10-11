// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: посилено валідацію вхідних даних (issue #49); перевірено: прогін негативних тест-кейсів (docs/api/quality-attributes.md, розділ 6)

using System.ComponentModel.DataAnnotations;

namespace Waterfall.Services.Models.Vacancies;

/// <summary>
/// Модель запиту на створення нової вакансії через REST API.
/// </summary>
public record CreateVacancyRequest : IValidatableObject
{
    /// <summary>
    /// Назва посади / позиції.
    /// </summary>
    /// <example>Middle .NET Developer</example>
    [Required(ErrorMessage = "Поле title є обов'язковим")]
    [StringLength(200, MinimumLength = 3, ErrorMessage = "Довжина title повинна бути від 3 до 200 символів")]
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Назва компанії-роботодавця.
    /// </summary>
    /// <example>Genesis Tech</example>
    [Required(ErrorMessage = "Поле company є обов'язковим")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Довжина company повинна бути від 2 до 200 символів")]
    public string Company { get; init; } = string.Empty;

    /// <summary>
    /// Повний опис вакансії.
    /// </summary>
    /// <example>Розробка бекенд сервісів на .NET 10.</example>
    [Required(ErrorMessage = "Поле description є обов'язковим")]
    [MinLength(10, ErrorMessage = "Довжина description повинна бути не менше 10 символів")]
    [MaxLength(10_000, ErrorMessage = "Довжина description не може перевищувати 10 000 символів")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Категорія / напрям.
    /// </summary>
    /// <example>.NET</example>
    [Required(ErrorMessage = "Поле category є обов'язковим")]
    [StringLength(100, ErrorMessage = "Довжина category не може перевищувати 100 символів")]
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// Локація / місто / формат роботи.
    /// </summary>
    /// <example>Київ (Віддалено)</example>
    [Required(ErrorMessage = "Поле location є обов'язковим")]
    [StringLength(200, ErrorMessage = "Довжина location не може перевищувати 200 символів")]
    public string Location { get; init; } = string.Empty;

    /// <summary>
    /// Повна адреса офісу (за наявності).
    /// </summary>
    /// <example>Київ, вул. Мечникова, 2</example>
    [StringLength(300, ErrorMessage = "Довжина fullAddress не може перевищувати 300 символів")]
    public string? FullAddress { get; init; }

    /// <summary>
    /// Графік роботи.
    /// </summary>
    /// <example>Повна зайнятість</example>
    [StringLength(100, ErrorMessage = "Довжина schedule не може перевищувати 100 символів")]
    public string? Schedule { get; init; }

    /// <summary>
    /// Необхідний досвід.
    /// </summary>
    /// <example>2+ роки</example>
    [StringLength(100, ErrorMessage = "Довжина experience не може перевищувати 100 символів")]
    public string? Experience { get; init; }

    /// <summary>
    /// Мінімальна заробітна плата.
    /// </summary>
    /// <example>2500</example>
    [Range(0, 1000000, ErrorMessage = "salaryMin повинна бути від 0 до 1 000 000")]
    public decimal? SalaryMin { get; init; }

    /// <summary>
    /// Максимальна заробітна плата.
    /// </summary>
    /// <example>4000</example>
    [Range(0, 1000000, ErrorMessage = "salaryMax повинна бути від 0 до 1 000 000")]
    public decimal? SalaryMax { get; init; }

    /// <summary>
    /// Валюта виплати заробітної плати (USD, EUR, UAH).
    /// </summary>
    /// <example>USD</example>
    [Required(ErrorMessage = "Поле currency є обов'язковим")]
    [RegularExpression("^(USD|EUR|UAH)$", ErrorMessage = "currency повинна мати значення 'USD', 'EUR' або 'UAH'")]
    public string Currency { get; init; } = "USD";

    /// <summary>
    /// Джерело надходження вакансії.
    /// </summary>
    /// <example>Djinni</example>
    [EnumDataType(typeof(JobSource), ErrorMessage = "source повинно бути одним із значень: WorkUa, RobotaUa, Djinni, Dou")]
    public JobSource Source { get; init; } = JobSource.Unknown;

    /// <summary>
    /// Посилання на оригінал вакансії.
    /// </summary>
    /// <example>https://djinni.co/jobs/example/</example>
    [Required(ErrorMessage = "Поле sourceUrl є обов'язковим")]
    [Url(ErrorMessage = "sourceUrl повинно бути валідним URL посиланням")]
    [StringLength(2048, MinimumLength = 1, ErrorMessage = "Довжина sourceUrl не може перевищувати 2048 символів")]
    public string SourceUrl { get; init; } = string.Empty;

    /// <summary>
    /// Список ключових навичок.
    /// </summary>
    /// <example>[".NET 10", "C#", "PostgreSQL"]</example>
    [MaxLength(30, ErrorMessage = "Список skills не може містити більше 30 навичок")]
    public IReadOnlyList<string> Skills { get; init; } = Array.Empty<string>();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SalaryMin.HasValue && SalaryMax.HasValue && SalaryMin > SalaryMax)
        {
            yield return new ValidationResult(
                "salaryMin не може перевищувати salaryMax",
                ["salaryMin", "salaryMax"]);
        }

        // Unknown — резервне значення для розбору даних з БД; через API джерело вказується явно
        if (Source == JobSource.Unknown)
        {
            yield return new ValidationResult(
                "Поле source є обов'язковим: WorkUa, RobotaUa, Djinni або Dou",
                ["source"]);
        }

        if (Skills is not null && Skills.Any(skill => string.IsNullOrWhiteSpace(skill) || skill.Length > 50))
        {
            yield return new ValidationResult(
                "Кожна навичка у skills має бути непорожнім рядком до 50 символів",
                ["skills"]);
        }
    }
}
