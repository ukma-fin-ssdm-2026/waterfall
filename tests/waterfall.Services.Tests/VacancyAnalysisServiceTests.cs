// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text;
using Microsoft.Extensions.AI;
using Waterfall.Repository.Entities;
using Waterfall.Services.Exceptions;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Ai;
using Waterfall.Services.Models.SavedVacancies;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Tests;

/// <summary>
/// Тести сервісу аналізу вакансій (FR-010, THR-06, FUNC-001): релевантність обчислюється до LLM,
/// промпт містить рівно запитані вакансії та узагальнений профіль без персональних даних,
/// а відсутня вакансія дає 404 ще до стрімінгу.
/// </summary>
public sealed class VacancyAnalysisServiceTests
{
    private readonly FakeSavedVacancyService _savedVacancies = new();
    private readonly FakeCatalogService _catalog = new();
    private readonly ScriptedLlmClientProvider _llm = new();
    private readonly FakeLogger<VacancyAnalysisService> _logger = new();
    private readonly Guid _userId = Guid.NewGuid();

    private VacancyAnalysisService CreateService()
        => new(_savedVacancies, _catalog, _llm, _logger);

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
            Experience = experienceMonths is int months ? $"{months} міс." : null,
            ExperienceMonths = experienceMonths,
            WorkFormat = format,
            SalaryMin = salaryMin,
            SalaryMax = salaryMax,
            Currency = currency,
            Source = JobSource.Djinni,
            SourceUrl = "https://example.com/job",
            Skills = skills,
            PublishedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };
    }

    private void Save(VacancyDto vacancy)
    {
        _savedVacancies.Saved.Add(new SavedVacancyDto
        {
            Id = Guid.NewGuid(),
            VacancyId = vacancy.Id,
            Vacancy = vacancy,
            SavedAt = DateTimeOffset.UtcNow
        });
    }

    private static string MessageText(ChatMessage message)
        => string.Concat(message.Contents.OfType<TextContent>().Select(content => content.Text));

    private static string PromptText(AIAnalysisTurn turn)
        => string.Join("\n", turn.Prompt.Select(MessageText));

    [Fact]
    public async Task PrepareAsync_PromptContainsExactlyTheRequestedVacancies()
    {
        // Arrange
        VacancyDto first = CreateVacancy("Python Developer", skills: "Python");
        VacancyDto second = CreateVacancy("QA Automation Engineer", categoryGroup: "QA", skills: "Playwright");
        VacancyDto notRequested = CreateVacancy("Java Developer", skills: "Java");
        _catalog.Vacancies.AddRange([first, second, notRequested]);
        Save(first);
        VacancyAnalysisService service = CreateService();

        // Act
        AIAnalysisTurn turn = await service.PrepareAsync(_userId, [first.Id, second.Id]);

        // Assert
        string prompt = PromptText(turn);
        Assert.Equal(new[] { first.Id, second.Id }, turn.Vacancies.Select(vacancy => vacancy.Id));
        Assert.Contains("Python Developer", prompt, StringComparison.Ordinal);
        Assert.Contains("QA Automation Engineer", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Java Developer", prompt, StringComparison.Ordinal);
        Assert.Contains("Релевантність профілю", prompt, StringComparison.Ordinal);
        Assert.Equal(2, turn.Relevance.Count);
    }

    [Fact]
    public async Task PrepareAsync_PromptHasNoPersonalDataOfTheUser()
    {
        // Arrange
        VacancyDto vacancy = CreateVacancy("Python Developer", skills: "Python");
        _catalog.Vacancies.Add(vacancy);
        Save(vacancy);
        VacancyAnalysisService service = CreateService();

        // Act
        AIAnalysisTurn turn = await service.PrepareAsync(_userId, [vacancy.Id]);

        // Assert
        string prompt = PromptText(turn);
        Assert.DoesNotContain(_userId.ToString(), prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@waterfall.com", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Узагальнений профіль кандидата", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_NoSavedVacancies_GivesNullScoresAndZeroBasis()
    {
        // Arrange
        VacancyDto vacancy = CreateVacancy("Python Developer", skills: "Python");
        _catalog.Vacancies.Add(vacancy);
        VacancyAnalysisService service = CreateService();

        // Act
        AIAnalysisTurn turn = await service.PrepareAsync(_userId, [vacancy.Id]);

        // Assert
        VacancyRelevance relevance = Assert.Single(turn.Relevance);
        Assert.Null(relevance.Score);
        Assert.Equal(0, relevance.BasisCount);
        Assert.Contains("немає збережених вакансій", PromptText(turn), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_ComputesRelevanceWithoutCallingLlm()
    {
        // Arrange
        VacancyDto vacancy = CreateVacancy("Python Developer", skills: "Python");
        _catalog.Vacancies.Add(vacancy);
        Save(vacancy);
        VacancyAnalysisService service = CreateService();

        // Act
        AIAnalysisTurn turn = await service.PrepareAsync(_userId, [vacancy.Id]);

        // Assert
        Assert.NotNull(Assert.Single(turn.Relevance).Score);
        Assert.Equal(0, _llm.Analyze.CallCount);
        Assert.Equal(0, _llm.Extract.CallCount);
        Assert.Equal(0, _llm.Chat.CallCount);
    }

    [Fact]
    public async Task PrepareAsync_UnknownVacancy_ThrowsNotFound()
    {
        // Arrange
        VacancyAnalysisService service = CreateService();
        Guid unknown = Guid.NewGuid();

        // Act
        VacancyNotFoundException exception = await Assert.ThrowsAsync<VacancyNotFoundException>(
            () => service.PrepareAsync(_userId, [unknown]));

        // Assert
        Assert.Equal(unknown, exception.VacancyId);
        Assert.Empty(_llm.Analyze.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task PrepareAsync_InvalidVacancyCount_ThrowsArgumentException(int count)
    {
        // Arrange
        VacancyAnalysisService service = CreateService();
        List<Guid> ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.PrepareAsync(_userId, ids));
    }

    [Fact]
    public async Task PrepareAsync_DuplicateIds_AreCollapsed()
    {
        // Arrange
        VacancyDto vacancy = CreateVacancy("Python Developer", skills: "Python");
        _catalog.Vacancies.Add(vacancy);
        Save(vacancy);
        VacancyAnalysisService service = CreateService();

        // Act
        AIAnalysisTurn turn = await service.PrepareAsync(_userId, [vacancy.Id, vacancy.Id]);

        // Assert
        Assert.Single(turn.Vacancies);
        Assert.Single(turn.Relevance);
    }

    [Fact]
    public async Task PrepareAsync_UsesArchivedSavedVacanciesAsProfileBasis()
    {
        // Arrange
        VacancyDto saved = CreateVacancy("Python Developer", skills: "Python");
        VacancyDto requested = CreateVacancy("Python Engineer", skills: "Python");
        _catalog.Vacancies.Add(requested);
        Save(saved);
        VacancyAnalysisService service = CreateService();

        // Act
        await service.PrepareAsync(_userId, [requested.Id]);

        // Assert: профіль не залежить від того, чи збережена вакансія ще активна
        Assert.Equal(new[] { false }, _savedVacancies.OnlyActiveCalls);
    }

    [Fact]
    public async Task StreamAnalysisAsync_StreamsDeltasAndCompletes()
    {
        // Arrange
        VacancyDto vacancy = CreateVacancy("Python Developer", skills: "Python");
        _catalog.Vacancies.Add(vacancy);
        Save(vacancy);
        VacancyAnalysisService service = CreateService();
        AIAnalysisTurn turn = await service.PrepareAsync(_userId, [vacancy.Id]);
        _llm.Analyze.Stream("Вимоги: ", "Python.");

        // Act
        List<AIAnalysisStreamItem> items = [];
        StringBuilder text = new();
        await foreach (AIAnalysisStreamItem item in service.StreamAnalysisAsync(turn))
        {
            items.Add(item);
            if (item is AIAnalysisDelta delta)
            {
                text.Append(delta.Text);
            }
        }

        // Assert
        Assert.Equal("Вимоги: Python.", text.ToString());
        Assert.IsType<AIAnalysisCompleted>(items[^1]);
        ScriptedChatCall call = Assert.Single(_llm.Analyze.Calls);
        Assert.True(call.Streaming);
        Assert.Contains("Python Developer", string.Join("\n", call.Messages.Select(MessageText)), StringComparison.Ordinal);
    }

    /// <summary>
    /// Підміна сервісу збережених вакансій: повертає заданий набір і фіксує, чи враховувалися архівні.
    /// </summary>
    private sealed class FakeSavedVacancyService : ISavedVacancyService
    {
        public List<SavedVacancyDto> Saved { get; } = [];

        public List<bool> OnlyActiveCalls { get; } = [];

        public Task<IReadOnlyList<SavedVacancyDto>> GetAllAsync(
            Guid userId,
            bool onlyActive = true,
            CancellationToken ct = default)
        {
            OnlyActiveCalls.Add(onlyActive);
            return Task.FromResult<IReadOnlyList<SavedVacancyDto>>(Saved);
        }

        public Task<SavedVacancyDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default)
            => Task.FromResult(Saved.FirstOrDefault(item => item.Id == id));

        public Task<SavedVacancyDto> SaveAsync(SaveVacancyRequest request, Guid userId, CancellationToken ct = default)
            => throw new NotSupportedException("Запис збережених вакансій у тестах аналізу не використовується.");

        public Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default)
            => throw new NotSupportedException("Видалення збережених вакансій у тестах аналізу не використовується.");
    }
}
