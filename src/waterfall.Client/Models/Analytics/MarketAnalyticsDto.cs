// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json.Serialization;

namespace Waterfall.Client.Models.Analytics;

public sealed record MarketAnalyticsDto
{
    [JsonPropertyName("totalVacancies")]
    public int TotalVacancies { get; init; }

    [JsonPropertyName("hasEnoughData")]
    public bool HasEnoughData { get; init; }

    [JsonPropertyName("primaryCurrency")]
    public string? PrimaryCurrency { get; init; }

    [JsonPropertyName("salaryTransparencyPercent")]
    public double SalaryTransparencyPercent { get; init; }

    [JsonPropertyName("entryLevelPercent")]
    public double EntryLevelPercent { get; init; }

    [JsonPropertyName("entryLevelCount")]
    public int EntryLevelCount { get; init; }

    [JsonPropertyName("salariesByGrade")]
    public IReadOnlyList<GradeSalaryDto> SalariesByGrade { get; init; } = [];

    [JsonPropertyName("topPayingTechnologies")]
    public IReadOnlyList<TechSalaryDto> TopPayingTechnologies { get; init; } = [];

    [JsonPropertyName("workFormats")]
    public IReadOnlyList<CategoryShareDto> WorkFormats { get; init; } = [];

    [JsonPropertyName("topCities")]
    public IReadOnlyList<CategoryShareDto> TopCities { get; init; } = [];

    [JsonPropertyName("sources")]
    public IReadOnlyList<CategoryShareDto> Sources { get; init; } = [];

    [JsonPropertyName("topCompanies")]
    public IReadOnlyList<CategoryShareDto> TopCompanies { get; init; } = [];

    [JsonPropertyName("availableCategories")]
    public IReadOnlyList<string> AvailableCategories { get; init; } = [];
}

public sealed record GradeSalaryDto(
    [property: JsonPropertyName("grade")] string Grade,
    [property: JsonPropertyName("averageSalary")] decimal AverageSalary,
    [property: JsonPropertyName("minSalary")] decimal MinSalary,
    [property: JsonPropertyName("maxSalary")] decimal MaxSalary,
    [property: JsonPropertyName("vacanciesCount")] int VacanciesCount,
    [property: JsonPropertyName("currency")] string Currency);

public sealed record TechSalaryDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("averageSalary")] decimal AverageSalary,
    [property: JsonPropertyName("vacanciesCount")] int VacanciesCount,
    [property: JsonPropertyName("currency")] string Currency);

public sealed record CategoryShareDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("sharePercent")] double SharePercent);
