// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json;
using Waterfall.Repository.Entities;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Tests;

/// <summary>
/// Модульні тести для <see cref="RecommendationService"/> (FR-006, US-004, Test-13..Test-15).
/// </summary>
public sealed class RecommendationServiceTests
{
    private static VacancyDB CreateVacancy(
        string title,
        string category = "Backend",
        string? categoryGroup = "Backend",
        string[]? skills = null,
        bool isActive = true)
    {
        return new VacancyDB
        {
            Id = Guid.NewGuid(),
            Title = title,
            Company = "Test Corp",
            Description = title,
            Category = category,
            CategoryGroup = categoryGroup,
            Location = "Remote",
            WorkFormat = WorkFormat.Remote,
            SkillsJson = JsonSerializer.Serialize(skills ?? []),
            Source = "Djinni",
            SourceUrl = "https://djinni.co/jobs/" + Guid.NewGuid().ToString("N")[..8],
            PublishedAt = DateTimeOffset.UtcNow,
            IsActive = isActive
        };
    }

    [Fact]
    public async Task GetRecommendations_WhenNoSavedVacancies_ReturnsEmptyInitialState()
    {
        // Arrange (Test-14: 0 збережених -> порожній результат і SavedCount == 0)
        Guid userId = Guid.NewGuid();
        FakeSavedVacancyRepository savedRepo = new();
        FakeSearchQueryRepository searchRepo = new();
        FakeVacancyRepository vacancyRepo = new();
        vacancyRepo.Vacancies.Add(CreateVacancy("Python Developer", skills: ["Python"]));
        await searchRepo.AddAsync(new SearchQueryDB { UserId = userId, QueryText = "Python" });

        RecommendationService service = new(savedRepo, searchRepo, vacancyRepo);

        // Act
        RecommendationsResultDto result = await service.GetRecommendationsAsync(userId);

        // Assert
        Assert.Equal(0, result.SavedCount);
        Assert.Empty(result.Items);
        Assert.Empty(result.BasisKeywords);
    }

    [Fact]
    public async Task GetRecommendations_BasedOnSavedPythonVacancies_ExcludesSavedAndRecommendsNewPython()
    {
        // Arrange (Test-13: 3 збережені з Python -> рекомендації з Python без збережених)
        Guid userId = Guid.NewGuid();
        FakeSavedVacancyRepository savedRepo = new();
        FakeSearchQueryRepository searchRepo = new();
        FakeVacancyRepository vacancyRepo = new();

        VacancyDB saved1 = CreateVacancy("Python Backend Engineer", skills: ["Python", "Django"]);
        VacancyDB saved2 = CreateVacancy("Senior Python Developer", skills: ["Python", "FastAPI"]);
        VacancyDB saved3 = CreateVacancy("Middle Python Developer", skills: ["Python"]);
        VacancyDB unsavedPython = CreateVacancy("Python Automation Engineer", skills: ["Python", "Pytest"]);
        VacancyDB unsavedDesign = CreateVacancy("UI/UX Designer", category: "Design", categoryGroup: "Design", skills: ["Figma"]);

        vacancyRepo.Vacancies.AddRange([saved1, saved2, saved3, unsavedPython, unsavedDesign]);

        foreach (VacancyDB v in new[] { saved1, saved2, saved3 })
        {
            await savedRepo.SaveAsync(new SavedVacancyDB
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                VacancyId = v.Id,
                Vacancy = v,
                SavedAt = DateTimeOffset.UtcNow
            });
        }

        RecommendationService service = new(savedRepo, searchRepo, vacancyRepo);

        // Act
        RecommendationsResultDto result = await service.GetRecommendationsAsync(userId);

        // Assert
        Assert.Equal(3, result.SavedCount);
        Assert.DoesNotContain(result.Items, item => item.Id == saved1.Id || item.Id == saved2.Id || item.Id == saved3.Id);
        Assert.Contains(result.Items, item => item.Id == unsavedPython.Id);
        Assert.All(result.Items, item => Assert.False(item.IsSaved));
        Assert.Contains("Python", result.BasisKeywords, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetRecommendations_WhenUserSavesNewTechnology_DynamicallyIncludesNewStack()
    {
        // Arrange (Test-15: 3 збережені з Python, потім +2 Java -> список оновлюється і включає Java)
        Guid userId = Guid.NewGuid();
        FakeSavedVacancyRepository savedRepo = new();
        FakeSearchQueryRepository searchRepo = new();
        FakeVacancyRepository vacancyRepo = new();

        VacancyDB pythonSaved = CreateVacancy("Python Developer", skills: ["Python"]);
        VacancyDB javaSaved1 = CreateVacancy("Java Developer", skills: ["Java", "Spring"]);
        VacancyDB javaSaved2 = CreateVacancy("Senior Java Engineer", skills: ["Java", "Kafka"]);
        VacancyDB unsavedJava = CreateVacancy("Java Backend Engineer", categoryGroup: null, skills: ["Java"]);
        unsavedJava.WorkFormat = WorkFormat.None;

        vacancyRepo.Vacancies.AddRange([pythonSaved, javaSaved1, javaSaved2, unsavedJava]);

        await savedRepo.SaveAsync(new SavedVacancyDB
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            VacancyId = pythonSaved.Id,
            Vacancy = pythonSaved,
            SavedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        });

        RecommendationService service = new(savedRepo, searchRepo, vacancyRepo);

        RecommendationsResultDto beforeJava = await service.GetRecommendationsAsync(userId);
        Assert.DoesNotContain(beforeJava.Items, item => item.Id == unsavedJava.Id);

        // Act: користувач зберігає вакансії з новою технологією (Java)
        foreach (VacancyDB javaVacancy in new[] { javaSaved1, javaSaved2 })
        {
            await savedRepo.SaveAsync(new SavedVacancyDB
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                VacancyId = javaVacancy.Id,
                Vacancy = javaVacancy,
                SavedAt = DateTimeOffset.UtcNow
            });
        }

        RecommendationsResultDto afterJava = await service.GetRecommendationsAsync(userId);

        // Assert
        Assert.Equal(3, afterJava.SavedCount);
        Assert.Contains(afterJava.Items, item => item.Id == unsavedJava.Id);
        Assert.Contains("Java", afterJava.BasisKeywords, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetRecommendations_UsesSearchHistoryToBoostMatchingVacancies()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        FakeSavedVacancyRepository savedRepo = new();
        FakeSearchQueryRepository searchRepo = new();
        FakeVacancyRepository vacancyRepo = new();

        VacancyDB savedPython = CreateVacancy("Python Developer", categoryGroup: null, skills: ["Python"]);
        savedPython.WorkFormat = WorkFormat.None;
        VacancyDB unsavedGo = CreateVacancy("Golang Backend Developer", categoryGroup: null, skills: ["Go"]);
        unsavedGo.WorkFormat = WorkFormat.None;

        vacancyRepo.Vacancies.AddRange([savedPython, unsavedGo]);

        await savedRepo.SaveAsync(new SavedVacancyDB
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            VacancyId = savedPython.Id,
            Vacancy = savedPython,
            SavedAt = DateTimeOffset.UtcNow
        });

        await searchRepo.AddAsync(new SearchQueryDB
        {
            UserId = userId,
            QueryText = "Go",
            SearchedAt = DateTimeOffset.UtcNow
        });

        RecommendationService service = new(savedRepo, searchRepo, vacancyRepo);

        // Act
        RecommendationsResultDto result = await service.GetRecommendationsAsync(userId);

        // Assert
        Assert.Contains(result.Items, item => item.Id == unsavedGo.Id);
        Assert.Contains("Go", result.BasisSearches);
    }
}
