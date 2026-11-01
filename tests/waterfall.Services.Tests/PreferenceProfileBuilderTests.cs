// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Entities;
using Waterfall.Services.Analysis;
using Waterfall.Services.Models.Ai;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Tests;

/// <summary>
/// Тести побудови профілю переваг зі збережених вакансій (FR-010): частоти категорій і форматів,
/// медіана досвіду, медіани зарплати в одній валюті та ваги ключових слів.
/// </summary>
public sealed class PreferenceProfileBuilderTests
{
    private static VacancyDto CreateVacancy(
        string title,
        string? categoryGroup = "Software Development",
        WorkFormat format = WorkFormat.Remote,
        int? experienceMonths = 24,
        decimal? salaryMin = 1000,
        decimal? salaryMax = 2000,
        string currency = "USD",
        string description = "Python backend developer",
        string[]? skills = null)
    {
        return new VacancyDto
        {
            Id = Guid.NewGuid(),
            Title = title,
            Company = "Waterfall Labs",
            Description = description,
            Category = categoryGroup ?? "Other",
            CategoryGroup = categoryGroup,
            Location = "Київ",
            ExperienceMonths = experienceMonths,
            WorkFormat = format,
            SalaryMin = salaryMin,
            SalaryMax = salaryMax,
            Currency = currency,
            Skills = skills ?? [],
            IsActive = true
        };
    }

    [Fact]
    public void Build_ComputesCategoryAndWorkFormatFrequencies()
    {
        // Arrange: 2 із 3 збережених — Software Development/remote, 1 — QA/hybrid
        VacancyDto[] saved =
        [
            CreateVacancy("Python Developer", "Software Development", WorkFormat.Remote),
            CreateVacancy("C# Developer", "Software Development", WorkFormat.Remote),
            CreateVacancy("QA Engineer", "QA", WorkFormat.Hybrid)
        ];

        // Act
        PreferenceProfile profile = PreferenceProfileBuilder.Build(saved);

        // Assert
        Assert.Equal(3, profile.BasisCount);
        Assert.True(profile.HasBasis);
        Assert.Equal(2.0 / 3.0, profile.CategoryWeights["Software Development"], 3);
        Assert.Equal(1.0 / 3.0, profile.CategoryWeights["QA"], 3);
        Assert.Equal(2.0 / 3.0, profile.WorkFormatWeights[WorkFormat.Remote], 3);
        Assert.Equal(1.0 / 3.0, profile.WorkFormatWeights[WorkFormat.Hybrid], 3);
        Assert.False(profile.WorkFormatWeights.ContainsKey(WorkFormat.Office));
    }

    [Fact]
    public void Build_EmptySet_ReturnsEmptyProfileWithoutBasis()
    {
        // Act
        PreferenceProfile profile = PreferenceProfileBuilder.Build([]);

        // Assert
        Assert.Same(PreferenceProfile.Empty, profile);
        Assert.Equal(0, profile.BasisCount);
        Assert.False(profile.HasBasis);
        Assert.Empty(profile.CategoryWeights);
        Assert.Empty(profile.KeywordWeights);
        Assert.Null(profile.MedianExperienceMonths);
        Assert.Null(profile.Salary);
    }

    [Fact]
    public void Build_MedianExperience_OddCountTakesMiddleValue()
    {
        // Arrange
        VacancyDto[] saved =
        [
            CreateVacancy("Junior", experienceMonths: 12),
            CreateVacancy("Middle", experienceMonths: 60),
            CreateVacancy("Senior", experienceMonths: 24)
        ];

        // Act
        PreferenceProfile profile = PreferenceProfileBuilder.Build(saved);

        // Assert
        Assert.Equal(24, profile.MedianExperienceMonths);
    }

    [Fact]
    public void Build_MedianExperience_EvenCountAveragesTwoMiddleValues()
    {
        // Arrange
        VacancyDto[] saved =
        [
            CreateVacancy("Junior", experienceMonths: 12),
            CreateVacancy("Middle", experienceMonths: 25)
        ];

        // Act
        PreferenceProfile profile = PreferenceProfileBuilder.Build(saved);

        // Assert
        Assert.Equal(19, profile.MedianExperienceMonths);
    }

    [Fact]
    public void Build_Salary_UsesMedianOfMinsAndMaxesInTheDominantCurrency()
    {
        // Arrange: дві вакансії в USD і одна в EUR — євро ігноруємо, курси не вигадуємо
        VacancyDto[] saved =
        [
            CreateVacancy("A", salaryMin: 1000, salaryMax: 1500, currency: "USD"),
            CreateVacancy("B", salaryMin: 2000, salaryMax: 2500, currency: "usd"),
            CreateVacancy("C", salaryMin: 5000, salaryMax: 9000, currency: "EUR")
        ];

        // Act
        PreferenceProfile profile = PreferenceProfileBuilder.Build(saved);

        // Assert
        SalaryPreference salary = Assert.IsType<SalaryPreference>(profile.Salary);
        Assert.Equal("USD", salary.Currency);
        Assert.Equal(1500m, salary.MedianMin);
        Assert.Equal(2000m, salary.MedianMax);
        Assert.True(salary.HasRange);
    }

    [Fact]
    public void Build_Salary_IgnoresVacanciesWithoutSalary()
    {
        // Arrange
        VacancyDto[] saved =
        [
            CreateVacancy("A", salaryMin: 1000, salaryMax: 1500),
            CreateVacancy("B", salaryMin: null, salaryMax: null)
        ];

        // Act
        PreferenceProfile profile = PreferenceProfileBuilder.Build(saved);

        // Assert
        SalaryPreference salary = Assert.IsType<SalaryPreference>(profile.Salary);
        Assert.Equal(1000m, salary.MedianMin);
        Assert.Equal(1500m, salary.MedianMax);
    }

    [Fact]
    public void Build_Keywords_PreferTagsAndKeepTopTwelveDeterministically()
    {
        // Arrange
        VacancyDto[] saved =
        [
            CreateVacancy(
                "Python Developer",
                description: "Python Django backend",
                skills: ["Python", "Django", "PostgreSQL"]),
            CreateVacancy(
                "Python Engineer",
                description: "Python FastAPI services",
                skills: ["Python", "FastAPI"])
        ];

        // Act
        PreferenceProfile profile = PreferenceProfileBuilder.Build(saved);

        // Assert
        Assert.InRange(profile.KeywordWeights.Count, 1, PreferenceProfileBuilder.MaxKeywords);
        Assert.Equal(1.0, profile.KeywordWeights["python"], 3);
        Assert.Contains("django", profile.KeywordWeights.Keys);
        Assert.Contains("postgresql", profile.KeywordWeights.Keys);

        // Стоп-слова й загальні слова оголошень не стають перевагами
        Assert.DoesNotContain("developer", profile.KeywordWeights.Keys);
        Assert.DoesNotContain("and", profile.KeywordWeights.Keys);
    }

    [Fact]
    public void Build_Keywords_WorkForNonItVacancies()
    {
        // Arrange
        VacancyDto[] saved =
        [
            CreateVacancy(
                "Головний бухгалтер",
                categoryGroup: "Finance",
                format: WorkFormat.Office,
                description: "Ведення бухгалтерського обліку, податкова звітність, 1С",
                skills: ["1С", "Облік", "Податки"])
        ];

        // Act
        PreferenceProfile profile = PreferenceProfileBuilder.Build(saved);

        // Assert
        Assert.Contains("1с", profile.KeywordWeights.Keys);
        Assert.Contains("облік", profile.KeywordWeights.Keys);
        Assert.Equal("Finance", Assert.Single(profile.CategoryWeights).Key);
        Assert.Equal(WorkFormat.Office, Assert.Single(profile.WorkFormatWeights).Key);
    }

    [Fact]
    public void ToPromptSummary_ContainsAggregatesAndNoPersonalData()
    {
        // Arrange
        PreferenceProfile profile = PreferenceProfileBuilder.Build(
        [
            CreateVacancy("Python Developer", skills: ["Python"])
        ]);

        // Act
        string summary = profile.ToPromptSummary();

        // Assert
        Assert.Contains("Збережених вакансій: 1", summary, StringComparison.Ordinal);
        Assert.Contains("Software Development", summary, StringComparison.Ordinal);
        Assert.Contains("віддалено", summary, StringComparison.Ordinal);
        Assert.Contains("python", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("@", summary, StringComparison.Ordinal);
    }
}
