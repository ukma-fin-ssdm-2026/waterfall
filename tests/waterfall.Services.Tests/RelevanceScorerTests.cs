// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Entities;
using Waterfall.Services.Analysis;
using Waterfall.Services.Models.Ai;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Tests;

/// <summary>
/// Тести оцінки релевантності (FR-010): межі 0..100, null без збережених вакансій, монотонність
/// («більше збігів — вища оцінка») і осмислені результати для не-IT вакансій.
/// </summary>
public sealed class RelevanceScorerTests
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
        params string[] skills)
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
            Skills = skills,
            IsActive = true
        };
    }

    private static PreferenceProfile ItProfile()
    {
        return PreferenceProfileBuilder.Build(
        [
            CreateVacancy(
                "Python Developer",
                description: "Python backend services on Django",
                skills: "Python", "Django", "PostgreSQL")
        ]);
    }

    [Fact]
    public void Score_NoSavedVacancies_ReturnsNullScoreAndZeroBasis()
    {
        // Arrange
        VacancyDto vacancy = CreateVacancy("Python Developer", skills: "Python");

        // Act
        VacancyRelevance relevance = RelevanceScorer.Score(PreferenceProfile.Empty, vacancy);

        // Assert
        Assert.Null(relevance.Score);
        Assert.Equal(0, relevance.BasisCount);
        Assert.Empty(relevance.Matched);
        Assert.Equal(vacancy.Id, relevance.VacancyId);
    }

    [Fact]
    public void Score_FullMatch_IsOneHundredAndListsMatches()
    {
        // Arrange
        PreferenceProfile profile = ItProfile();
        VacancyDto vacancy = CreateVacancy(
            "Python Developer",
            description: "Python backend services on Django",
            skills: "Python", "Django", "PostgreSQL");

        // Act
        VacancyRelevance relevance = RelevanceScorer.Score(profile, vacancy);

        // Assert
        Assert.Equal(100, relevance.Score);
        Assert.Equal(1, relevance.BasisCount);
        Assert.Contains("Software Development", relevance.Matched);
        Assert.Contains("віддалено", relevance.Matched);
        Assert.Contains("python", relevance.Matched);
    }

    [Fact]
    public void Score_MoreOverlap_GivesHigherScore()
    {
        // Arrange
        PreferenceProfile profile = ItProfile();
        VacancyDto close = CreateVacancy(
            "Python Developer",
            description: "Python backend services on Django",
            skills: "Python", "Django");
        VacancyDto far = CreateVacancy(
            "Java Developer",
            format: WorkFormat.Office,
            experienceMonths: 60,
            currency: "EUR",
            salaryMin: 6000,
            salaryMax: 8000,
            description: "Java Spring enterprise applications",
            skills: "Java", "Spring");

        // Act
        VacancyRelevance closeRelevance = RelevanceScorer.Score(profile, close);
        VacancyRelevance farRelevance = RelevanceScorer.Score(profile, far);

        // Assert
        Assert.NotNull(closeRelevance.Score);
        Assert.NotNull(farRelevance.Score);
        Assert.True(
            closeRelevance.Score > farRelevance.Score,
            $"Очікували ближчу вакансію вище: {closeRelevance.Score} vs {farRelevance.Score}");
    }

    [Fact]
    public void Score_IsAlwaysWithinBounds()
    {
        // Arrange
        PreferenceProfile profile = ItProfile();
        VacancyDto[] vacancies =
        [
            CreateVacancy("Python Developer"),
            CreateVacancy("Бухгалтер", categoryGroup: "Finance", format: WorkFormat.Office),
            CreateVacancy("Порожня", categoryGroup: null, format: WorkFormat.None, experienceMonths: null,
                salaryMin: null, salaryMax: null, description: string.Empty)
        ];

        // Act & Assert
        foreach (VacancyDto vacancy in vacancies)
        {
            VacancyRelevance relevance = RelevanceScorer.Score(profile, vacancy);
            Assert.NotNull(relevance.Score);
            Assert.InRange(relevance.Score.Value, 0, 100);
        }
    }

    [Fact]
    public void Score_NonItProfile_RatesAccountantVacancyAboveDeveloperVacancy()
    {
        // Arrange: бухгалтер зберіг вакансії головного бухгалтера
        PreferenceProfile profile = PreferenceProfileBuilder.Build(
        [
            CreateVacancy(
                "Головний бухгалтер",
                categoryGroup: "Finance",
                format: WorkFormat.Office,
                experienceMonths: 36,
                salaryMin: 20000,
                salaryMax: 30000,
                currency: "UAH",
                description: "Ведення бухгалтерського обліку, податкова звітність, 1С",
                skills: "1С", "Облік", "Податки")
        ]);

        VacancyDto accountant = CreateVacancy(
            "Бухгалтер",
            categoryGroup: "Finance",
            format: WorkFormat.Office,
            experienceMonths: 36,
            salaryMin: 25000,
            salaryMax: 30000,
            currency: "UAH",
            description: "Облік, податкова звітність, 1С",
            skills: "1С", "Облік");

        VacancyDto developer = CreateVacancy(
            "Python Developer",
            categoryGroup: "Software Development",
            format: WorkFormat.Remote,
            experienceMonths: 60,
            salaryMin: 4000,
            salaryMax: 5000,
            description: "Python backend services",
            skills: "Python");

        // Act
        VacancyRelevance accountantRelevance = RelevanceScorer.Score(profile, accountant);
        VacancyRelevance developerRelevance = RelevanceScorer.Score(profile, developer);

        // Assert
        Assert.NotNull(accountantRelevance.Score);
        Assert.NotNull(developerRelevance.Score);
        Assert.InRange(accountantRelevance.Score.Value, 70, 100);
        Assert.InRange(developerRelevance.Score.Value, 0, 30);
        Assert.Contains("Finance", accountantRelevance.Matched);
        Assert.Contains("1с", accountantRelevance.Matched);
    }

    [Fact]
    public void Score_DifferentCurrency_DoesNotPenalizeForSalaryMismatch()
    {
        // Arrange: зарплата вакансії в іншій валюті не порівнюється, складова просто не враховується
        PreferenceProfile profile = PreferenceProfileBuilder.Build(
        [
            CreateVacancy("Python Developer", salaryMin: 1000, salaryMax: 2000, currency: "USD", skills: "Python")
        ]);
        VacancyDto sameCurrency = CreateVacancy("Python Developer", salaryMin: 1000, salaryMax: 2000, skills: "Python");
        VacancyDto otherCurrency = CreateVacancy(
            "Python Developer",
            salaryMin: 1000,
            salaryMax: 2000,
            currency: "EUR",
            skills: "Python");

        // Act
        VacancyRelevance same = RelevanceScorer.Score(profile, sameCurrency);
        VacancyRelevance other = RelevanceScorer.Score(profile, otherCurrency);

        // Assert
        Assert.Equal(same.Score, other.Score);
    }
}
