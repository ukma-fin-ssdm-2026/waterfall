// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: посилено валідацію вхідних даних (issue #49); перевірено: прогін негативних тест-кейсів (docs/api/quality-attributes.md, розділ 6)

using System.ComponentModel.DataAnnotations;

namespace Waterfall.Services.Models.Vacancies;

/// <summary>
/// Модель запиту на оновлення існуючої вакансії (PUT/PATCH).
/// </summary>
public record UpdateVacancyRequest : IValidatableObject
{
    /// <summary>
    /// Назва посади / позиції.
    /// </summary>
    /// <example>Lead .NET Developer</example>
    [StringLength(200, MinimumLength = 3, ErrorMessage = "Довжина title повинна бути від 3 до 200 символів")]
    public string? Title { get; init; }

    /// <summary>
    /// Назва компанії-роботодавця.
    /// </summary>
    /// <example>Genesis Tech</example>
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Довжина company повинна бути від 2 до 200 символів")]
    public string? Company { get; init; }

    /// <summary>
    /// Повний опис вакансії.
    /// </summary>
    /// <example>Оновлений опис посади...</example>
    [MinLength(10, ErrorMessage = "Довжина description повинна бути не менше 10 символів")]
    [MaxLength(10_000, ErrorMessage = "Довжина description не може перевищувати 10 000 символів")]
    public string? Description { get; init; }

    /// <summary>
    /// Професійна категорія / напрям.
    /// </summary>
    /// <example>.NET</example>
    [StringLength(100, ErrorMessage = "Довжина category не може перевищувати 100 символів")]
    public string? Category { get; init; }

    /// <summary>
    /// Місто / формат роботи.
    /// </summary>
    /// <example>Віддалено</example>
    [StringLength(200, ErrorMessage = "Довжина location не може перевищувати 200 символів")]
    public string? Location { get; init; }

    /// <summary>
    /// Повна адреса офісу (за наявності).
    /// </summary>
    [StringLength(300, ErrorMessage = "Довжина fullAddress не може перевищувати 300 символів")]
    public string? FullAddress { get; init; }

    /// <summary>
    /// Графік роботи.
    /// </summary>
    [StringLength(100, ErrorMessage = "Довжина schedule не може перевищувати 100 символів")]
    public string? Schedule { get; init; }

    /// <summary>
    /// Необхідний досвід.
    /// </summary>
    [StringLength(100, ErrorMessage = "Довжина experience не може перевищувати 100 символів")]
    public string? Experience { get; init; }

    /// <summary>
    /// Мінімальна заробітна плата.
    /// </summary>
    [Range(0, 1000000, ErrorMessage = "salaryMin повинна бути від 0 до 1 000 000")]
    public decimal? SalaryMin { get; init; }

    /// <summary>
    /// Максимальна заробітна плата.
    /// </summary>
    [Range(0, 1000000, ErrorMessage = "salaryMax повинна бути від 0 до 1 000 000")]
    public decimal? SalaryMax { get; init; }

    /// <summary>
    /// Валюта виплати заробітної плати (USD, EUR, UAH).
    /// </summary>
    [RegularExpression("^(USD|EUR|UAH)$", ErrorMessage = "currency повинна мати значення 'USD', 'EUR' або 'UAH'")]
    public string? Currency { get; init; }

    /// <summary>
    /// Джерело походження вакансії.
    /// </summary>
    [EnumDataType(typeof(JobSource), ErrorMessage = "source повинно бути одним із значень: WorkUa, RobotaUa, Djinni, Dou")]
    public JobSource? Source { get; init; }

    /// <summary>
    /// Посилання на оригінал вакансії.
    /// </summary>
    [Url(ErrorMessage = "sourceUrl повинно бути валідним URL посиланням")]
    [StringLength(2048, MinimumLength = 1, ErrorMessage = "Довжина sourceUrl не може перевищувати 2048 символів")]
    public string? SourceUrl { get; init; }

    /// <summary>
    /// Список ключових навичок.
    /// </summary>
    [MaxLength(30, ErrorMessage = "Список skills не може містити більше 30 навичок")]
    public IReadOnlyList<string>? Skills { get; init; }

    /// <summary>
    /// Ознака активності вакансії.
    /// </summary>
    public bool? IsActive { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SalaryMin.HasValue && SalaryMax.HasValue && SalaryMin > SalaryMax)
        {
            yield return new ValidationResult(
                "salaryMin не може перевищувати salaryMax",
                ["salaryMin", "salaryMax"]);
        }

        if (Source == JobSource.Unknown)
        {
            yield return new ValidationResult(
                "source повинно бути одним із значень: WorkUa, RobotaUa, Djinni, Dou",
                ["source"]);
        }

        // Порожні навички допустимі: вони зберігаються, але не потрапляють під пошук
        if (Skills is not null && Skills.Any(skill => skill is not null && skill.Length > 50))
        {
            yield return new ValidationResult(
                "Кожна навичка у skills не може перевищувати 50 символів",
                ["skills"]);
        }
    }
}
