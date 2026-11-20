// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Models.Analytics;

/// <summary>
/// Агрегована ринкова аналітика вакансій (FR-003, FR-008, US-006, US-007).
/// </summary>
public class MarketAnalyticsDto
{
    public int TotalVacancies { get; set; }

    public bool HasEnoughData { get; set; }

    public string? PrimaryCurrency { get; set; }

    /// <summary>Частка вакансій, у яких вказана зарплата (%).</summary>
    public double SalaryTransparencyPercent { get; set; }

    /// <summary>Частка вакансій із низьким порогом входу (досвід &lt;= 1 року, %).</summary>
    public double EntryLevelPercent { get; set; }

    public int EntryLevelCount { get; set; }

    /// <summary>1. Зарплати за грейдами (Junior / Middle / Senior).</summary>
    public IReadOnlyList<GradeSalaryDto> SalariesByGrade { get; set; } = [];

    /// <summary>2. Топ-5 найвисокооплачуваніших технологій за середньою зарплатою.</summary>
    public IReadOnlyList<TechSalaryDto> TopPayingTechnologies { get; set; } = [];

    /// <summary>3. Розподіл форматів роботи (Remote / Hybrid / Office).</summary>
    public IReadOnlyList<CategoryShareDto> WorkFormats { get; set; } = [];

    /// <summary>4a. Топ-5 міст за кількістю вакансій.</summary>
    public IReadOnlyList<CategoryShareDto> TopCities { get; set; } = [];

    /// <summary>4b. Розподіл вакансій за джерелами (Djinni, DOU, WorkUa, RobotaUa).</summary>
    public IReadOnlyList<CategoryShareDto> Sources { get; set; } = [];

    /// <summary>5. Топ-5 найактивніших компаній-роботодавців.</summary>
    public IReadOnlyList<CategoryShareDto> TopCompanies { get; set; } = [];

    public IReadOnlyList<string> AvailableCategories { get; set; } = [];
}

public sealed record GradeSalaryDto(
    string Grade,
    decimal AverageSalary,
    decimal MinSalary,
    decimal MaxSalary,
    int VacanciesCount,
    string Currency);

public sealed record TechSalaryDto(
    string Name,
    decimal AverageSalary,
    int VacanciesCount,
    string Currency);

public sealed record CategoryShareDto(
    string Name,
    int Count,
    double SharePercent);
