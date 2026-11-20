// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json;
using Waterfall.Repository.Entities;
using Waterfall.Services.Models.Analytics;

namespace Waterfall.Services.Tests;

public sealed class AnalyticsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static VacancyDB CreateVacancy(
        string company = "Test Corp",
        string category = "Python",
        string? categoryGroup = "Backend",
        string location = "Київ (Віддалено)",
        WorkFormat workFormat = WorkFormat.Remote,
        int? experienceMonths = 24,
        string[]? skills = null,
        decimal? salaryMin = 2000,
        decimal? salaryMax = 3000,
        string? currency = "USD",
        string source = "Djinni",
        DateTimeOffset? publishedAt = null)
    {
        DateTimeOffset date = publishedAt ?? Now.AddDays(-5);
        return new VacancyDB
        {
            Id = Guid.NewGuid(),
            Title = $"{category} Developer",
            Company = company,
            Description = "Desc",
            Category = category,
            CategoryGroup = categoryGroup,
            Location = location,
            WorkFormat = workFormat,
            ExperienceMonths = experienceMonths,
            SkillsJson = JsonSerializer.Serialize(skills ?? ["Python", "Docker"]),
            SalaryMin = salaryMin,
            SalaryMax = salaryMax,
            Currency = currency ?? string.Empty,
            Source = source,
            SourceUrl = "https://djinni.co/jobs/" + Guid.NewGuid().ToString("N")[..8],
            PublishedAt = date,
            FirstSeenAt = date,
            IsActive = true
        };
    }

    [Fact]
    public async Task GetMarketAnalytics_FewerThanFiveVacancies_ReturnsInsufficientData()
    {
        FakeVacancyRepository repo = new();
        for (int i = 0; i < 4; i++)
        {
            repo.Vacancies.Add(CreateVacancy());
        }

        AnalyticsService service = new(repo, new FixedTimeProvider(Now));

        MarketAnalyticsDto result = await service.GetMarketAnalyticsAsync();

        Assert.False(result.HasEnoughData);
        Assert.Equal(4, result.TotalVacancies);
        Assert.Empty(result.SalariesByGrade);
        Assert.Empty(result.TopPayingTechnologies);
        Assert.Contains("Backend", result.AvailableCategories);
    }

    [Fact]
    public async Task GetMarketAnalytics_EnoughVacancies_ComputesAllFiveAnalytics()
    {
        FakeVacancyRepository repo = new();
        repo.Vacancies.Add(CreateVacancy(company: "Alpha", location: "Київ (Віддалено)", workFormat: WorkFormat.Remote, experienceMonths: 6, skills: ["Python", "Django"], salaryMin: 1000, salaryMax: 1500, source: "Djinni"));
        repo.Vacancies.Add(CreateVacancy(company: "Alpha", location: "Київ", workFormat: WorkFormat.Hybrid, experienceMonths: 24, skills: ["Python", "Docker"], salaryMin: 2500, salaryMax: 3500, source: "DOU"));
        repo.Vacancies.Add(CreateVacancy(company: "Beta", location: "Львів, вул. Садова", workFormat: WorkFormat.Office, experienceMonths: 48, skills: ["Go", "Docker"], salaryMin: 4000, salaryMax: 5000, source: "Djinni"));
        repo.Vacancies.Add(CreateVacancy(company: "Beta", location: "Львів", workFormat: WorkFormat.Remote, experienceMonths: 48, skills: ["Go", "Kafka"], salaryMin: 4500, salaryMax: 5500, source: "WorkUa"));
        repo.Vacancies.Add(CreateVacancy(company: "Gamma", location: "Віддалено", workFormat: WorkFormat.Remote, experienceMonths: 12, skills: ["Python"], salaryMin: null, salaryMax: null, currency: null, source: "WorkUa"));

        AnalyticsService service = new(repo, new FixedTimeProvider(Now));

        MarketAnalyticsDto result = await service.GetMarketAnalyticsAsync(months: 3);

        Assert.True(result.HasEnoughData);
        Assert.Equal(5, result.TotalVacancies);
        Assert.Equal("USD", result.PrimaryCurrency);
        Assert.Equal(80.0, result.SalaryTransparencyPercent);
        Assert.Equal(2, result.EntryLevelCount);
        Assert.Equal(40.0, result.EntryLevelPercent);

        // 1. Зарплати за грейдами (Junior, Middle, Senior)
        Assert.Equal(3, result.SalariesByGrade.Count);
        Assert.Equal(1250m, result.SalariesByGrade.Single(g => g.Grade.StartsWith("Junior")).AverageSalary);
        Assert.Equal(3000m, result.SalariesByGrade.Single(g => g.Grade.StartsWith("Middle")).AverageSalary);
        Assert.Equal(4750m, result.SalariesByGrade.Single(g => g.Grade.StartsWith("Senior")).AverageSalary);

        // 2. Найвисокооплачуваніші технології (Go дорожче за Python)
        Assert.Equal("Go", result.TopPayingTechnologies[0].Name);
        Assert.Equal(4750m, result.TopPayingTechnologies[0].AverageSalary);

        // 3. Формати роботи
        Assert.Contains(result.WorkFormats, w => w.Name == "Віддалено" && w.Count == 3);
        Assert.Contains(result.WorkFormats, w => w.Name == "Гібрид" && w.Count == 1);
        Assert.Contains(result.WorkFormats, w => w.Name == "Офіс" && w.Count == 1);

        // 4. Міста і майданчики
        Assert.Contains(result.TopCities, c => c.Name == "Київ" && c.Count == 2);
        Assert.Contains(result.TopCities, c => c.Name == "Львів" && c.Count == 2);
        Assert.Contains(result.Sources, s => s.Name == "Djinni" && s.Count == 2);
        Assert.Contains(result.Sources, s => s.Name == "WorkUa" && s.Count == 2);

        // 5. Топ компаній
        Assert.Contains(result.TopCompanies, c => c.Name == "Alpha" && c.Count == 2);
        Assert.Contains(result.TopCompanies, c => c.Name == "Beta" && c.Count == 2);
    }

    [Fact]
    public async Task GetMarketAnalytics_FiltersByCategoryAndPeriod()
    {
        FakeVacancyRepository repo = new();
        for (int i = 0; i < 5; i++)
        {
            repo.Vacancies.Add(CreateVacancy(category: "Python", categoryGroup: "Backend", publishedAt: Now.AddDays(-i * 3)));
        }
        repo.Vacancies.Add(CreateVacancy(category: "QA", categoryGroup: "QA", publishedAt: Now.AddDays(-1)));
        repo.Vacancies.Add(CreateVacancy(category: "Python", categoryGroup: "Backend", publishedAt: Now.AddMonths(-6)));

        AnalyticsService service = new(repo, new FixedTimeProvider(Now));

        MarketAnalyticsDto result = await service.GetMarketAnalyticsAsync(category: "Backend", months: 1);

        Assert.True(result.HasEnoughData);
        Assert.Equal(5, result.TotalVacancies);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
