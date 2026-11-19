// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Client.Models.Analytics;

public class MarketAnalyticsDto
{
    public int TotalVacancies { get; set; }

    public bool HasEnoughData { get; set; }

    public string? PrimaryCurrency { get; set; }

    public double SalaryTransparencyPercent { get; set; }

    public double EntryLevelPercent { get; set; }

    public int EntryLevelCount { get; set; }

    public IReadOnlyList<GradeSalaryDto> SalariesByGrade { get; set; } = [];

    public IReadOnlyList<TechSalaryDto> TopPayingTechnologies { get; set; } = [];

    public IReadOnlyList<CategoryShareDto> WorkFormats { get; set; } = [];

    public IReadOnlyList<CategoryShareDto> TopCities { get; set; } = [];

    public IReadOnlyList<CategoryShareDto> Sources { get; set; } = [];

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
