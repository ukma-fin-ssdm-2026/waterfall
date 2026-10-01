// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Waterfall.Api.Models.Vacancies;
/// <summary>
/// Тіло запиту для створення або імпорту нової вакансії.
/// </summary>
public class CreateVacancyRequest : IValidatableObject
{
    /// <summary>
    /// Назва посади / вакансії (від 3 до 150 символів).
    /// </summary>
    /// <example>Senior C# / .NET Architect</example>
    [Required(ErrorMessage = "Поле title є обов'язковим для заповнення.")]
    [StringLength(150, MinimumLength = 3, ErrorMessage = "Назва вакансії має містити від 3 до 150 символів.")]
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Назва компанії-роботодавця (від 2 до 100 символів).
    /// </summary>
    /// <example>FinTech Solutions UA</example>
    [Required(ErrorMessage = "Поле company є обов'язковим.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Назва компанії має містити від 2 до 100 символів.")]
    [JsonPropertyName("company")]
    public string Company { get; set; } = string.Empty;

    /// <summary>
    /// Повний зміст опису та вимог до кандидата (щонайменше 20 символів).
    /// </summary>
    /// <example>Розробка надійних фінансових сервісів та інтеграція платіжних систем на .NET.</example>
    [Required(ErrorMessage = "Поле description є обов'язковим.")]
    [MinLength(20, ErrorMessage = "Опис вакансії повинен містити щонайменше 20 символів.")]
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Професійна категорія вакансії.
    /// </summary>
    /// <example>.NET</example>
    [Required(ErrorMessage = "Поле category є обов'язковим.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Категорія має містити від 2 до 50 символів.")]
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Місце роботи або формат зайнятості.
    /// </summary>
    /// <example>Київ / Віддалено</example>
    [Required(ErrorMessage = "Поле location є обов'язковим.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Локація має містити від 2 до 100 символів.")]
    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Повна фізична адреса офісу чи локації
    /// </summary>
    /// <example>Київ, проспект Академіка Глушкова, 1</example>
    [StringLength(200, ErrorMessage = "Повна адреса не повинна перевищувати 200 символів.")]
    [JsonPropertyName("full_address")]
    public string? FullAddress { get; set; }

    /// <summary>
    /// Графік та тип зайнятості 
    /// </summary>
    /// <example>Повна зайнятість · Стандартний графік: 5/2</example>
    [StringLength(100, ErrorMessage = "Графік роботи не повинен перевищувати 100 символів.")]
    [JsonPropertyName("schedule")]
    public string? Schedule { get; set; }

    /// <summary>
    /// Необхідний досвід роботи 
    /// </summary>
    /// <example>3+ роки</example>
    [StringLength(50, ErrorMessage = "Поле experience не повинно перевищувати 50 символів.")]
    [JsonPropertyName("experience")]
    public string? Experience { get; set; }

    /// <summary>
    /// Мінімальна заробітна плата. Повинна бути більшою або рівною 0.
    /// </summary>
    /// <example>3000</example>
    [Range(0, 1_000_000, ErrorMessage = "Мінімальна заробітна плата повинна бути в межах від 0 до 1,000,000.")]
    [JsonPropertyName("salary_min")]
    public decimal? SalaryMin { get; set; }

    /// <summary>
    /// Максимальна заробітна плата. Повинна бути в межах від 0 до 1,000,000.
    /// </summary>
    /// <example>5000</example>
    [Range(0, 1_000_000, ErrorMessage = "Максимальна заробітна плата повинна бути в межах від 0 до 1,000,000.")]
    [JsonPropertyName("salary_max")]
    public decimal? SalaryMax { get; set; }

    /// <summary>
    /// Валюта заробітної плати (3 символи ISO, наприклад USD, EUR, UAH).
    /// </summary>
    /// <example>USD</example>
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Валюта повинна містити рівно 3 літери ISO коду (наприклад: USD, UAH).")]
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Первинне джерело агрегації даних.
    /// </summary>
    /// <example>Djinni</example>
    [Required(ErrorMessage = "Поле source є обов'язковим.")]
    [JsonPropertyName("source")]
    public JobSource Source { get; set; } = JobSource.Djinni;

    /// <summary>
    /// Пряме посилання на першоджерело публікації.
    /// </summary>
    /// <example>https://example.com/careers/senior-dotnet</example>
    [Required(ErrorMessage = "Поле source_url є обов'язковим.")]
    [Url(ErrorMessage = "Посилання source_url повинно мати дійсний формат URL.")]
    [JsonPropertyName("source_url")]
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>
    /// Список ключових навичок.
    /// </summary>
    /// <example>["C#", ".NET", "Architecture", "PostgreSQL"]</example>
    [JsonPropertyName("skills")]
    public List<string> Skills { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SalaryMin.HasValue && SalaryMax.HasValue && SalaryMin > SalaryMax)
        {
            yield return new ValidationResult(
                "Мінімальна заробітна плата (salary_min) не може перевищувати максимальну (salary_max).",
                [nameof(SalaryMin), nameof(SalaryMax)]);
        }
    }
}
