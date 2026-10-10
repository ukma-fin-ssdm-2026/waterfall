// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;

namespace Waterfall.Services.Models.Vacancies;

/// <summary>
/// Модель запиту на оновлення існуючої вакансії (PUT/PATCH).
/// </summary>
public record UpdateVacancyRequest
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
    public JobSource? Source { get; init; }

    /// <summary>
    /// Посилання на оригінал вакансії.
    /// </summary>
    [Url(ErrorMessage = "sourceUrl повинно бути валідним URL посиланням")]
    public string? SourceUrl { get; init; }

    /// <summary>
    /// Список ключових навичок.
    /// </summary>
    public IReadOnlyList<string>? Skills { get; init; }

    /// <summary>
    /// Ознака активності вакансії.
    /// </summary>
    public bool? IsActive { get; init; }
}
