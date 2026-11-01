// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Waterfall.Repository.Entities;

/// <summary>
/// Сутність вакансії для збереження в реляційній базі даних (SQLite / MSSQL) через EF Core.
/// </summary>
[Table("Vacancies")]
[Index(nameof(Category))]
[Index(nameof(CategoryGroup))]
[Index(nameof(PublishedAt))]
[Index(nameof(IsActive))]
[Index(nameof(Source))]
public class VacancyDB
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Company { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Category { get; set; } = string.Empty;

    // AI: широка канонічна категорія для фільтра й ШІ-консультанта (довідник VacancyCategoryMap); перевірено: тести нормалізатора
    [MaxLength(100)]
    public string? CategoryGroup { get; set; }

    [Required]
    [MaxLength(200)]
    public string Location { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? FullAddress { get; set; }

    [MaxLength(100)]
    public string? Schedule { get; set; }

    [MaxLength(100)]
    public string? Experience { get; set; }

    /// <summary>
    /// Мінімальний необхідний досвід у місяцях: 0 — без досвіду, 12 — від року, null — джерело не вказало.
    /// Парсери беруть точне число (Djinni monthsOfExperience, фасети DOU), решту дає розбір тексту.
    /// </summary>
    public int? ExperienceMonths { get; set; }

    /// <summary>
    /// Формат роботи прапорцями. Заповнюється нормалізатором із Location і Schedule, бо окремого поля в джерелах немає.
    /// </summary>
    public WorkFormat WorkFormat { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? SalaryMin { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? SalaryMax { get; set; }

    [Required]
    [MaxLength(10)]
    public string Currency { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Source { get; set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    public string SourceUrl { get; set; } = string.Empty;

    public string SkillsJson { get; set; } = "[]";

    /// <summary>
    /// Нижній регістр назви, компанії, опису й тегів. SQLite порівнює кирилицю з урахуванням регістру,
    /// тож каталог шукає підрядок саме в цьому полі — і працює однаково на SQLite та MSSQL.
    /// </summary>
    [Required]
    public string SearchText { get; set; } = string.Empty;

    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(100)]
    public string? ExternalId { get; set; }

    public bool IsActive { get; set; } = true;
}
