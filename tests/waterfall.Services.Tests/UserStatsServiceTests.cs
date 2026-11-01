// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Entities;
using Waterfall.Services;
using Waterfall.Services.Models.Profile;

namespace Waterfall.Services.Tests;

/// <summary>
/// Тести UserStatsService (FR-007, US-008): агрегація статистики (Test-28),
/// свіжість лічильників (Test-29), порожній стан (Test-30) та правила запису подій.
/// Зовнішні ключі не задіяні: всі залежності — тестові дублери з RepositoryMocks.cs.
/// </summary>
public sealed class UserStatsServiceTests
{
    private readonly FakeSavedVacancyRepository _savedRepository = new();
    private readonly FakeSearchQueryRepository _searchQueryRepository = new();
    private readonly FakeVacancyViewRepository _viewRepository = new();
    private readonly FakeUserRepository _userRepository = new();
    private readonly UserStatsService _service;

    public UserStatsServiceTests()
    {
        _service = new UserStatsService(
            _savedRepository,
            _searchQueryRepository,
            _viewRepository,
            _userRepository,
            new FakeLogger<UserStatsService>());
    }

    private static VacancyDB CreateVacancy(string title, string category, string skillsJson)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new VacancyDB
        {
            Id = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = title,
            Company = "TopCompany",
            Description = "Great job opportunity",
            Category = category,
            Location = "Kyiv",
            SalaryMin = 2000,
            SalaryMax = 3000,
            Currency = "USD",
            Source = "Djinni",
            SourceUrl = "https://example.com/job",
            SkillsJson = skillsJson,
            PublishedAt = now,
            FirstSeenAt = now,
            LastSeenAt = now,
            IsActive = true
        };
    }

    private Guid SeedUser(string role)
    {
        UserDB user = new()
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@example.com",
            PasswordHash = "hash",
            Role = role
        };
        _userRepository.Users.Add(user);
        return user.Id;
    }

    // Test-28: агрегація — лічильники, топи технологій/категорій, останні запити.
    [Fact]
    public async Task GetStatsAsync_AggregatesSavedAndViewedVacancies()
    {
        // Arrange
        Guid userId = SeedUser("JobSeeker");

        VacancyDB pythonDjango = CreateVacancy("Backend #1", "Backend", "[\"Python\", \"Django\"]");
        VacancyDB pythonSql = CreateVacancy("Backend #2", "Backend", "[\"Python\"]");
        VacancyDB csharp = CreateVacancy("Frontend #1", "Frontend", "[\"C#\"]");
        VacancyDB pythonView = CreateVacancy("Backend #3", "Backend", "[\"Python\"]");

        _savedRepository.SavedVacancies.AddRange(
            new SavedVacancyDB { Id = Guid.NewGuid(), UserId = userId, Vacancy = pythonDjango },
            new SavedVacancyDB { Id = Guid.NewGuid(), UserId = userId, Vacancy = pythonSql },
            new SavedVacancyDB { Id = Guid.NewGuid(), UserId = userId, Vacancy = csharp });
        _viewRepository.Views.Add(
            new VacancyViewDB { Id = Guid.NewGuid(), UserId = userId, Vacancy = pythonView });

        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int i = 5; i >= 1; i--)
        {
            _searchQueryRepository.Queries.Add(new SearchQueryDB
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                QueryText = $"Запит {6 - i}",
                SearchedAt = now.AddMinutes(-i)
            });
        }

        // Act
        UserStatsDto stats = await _service.GetStatsAsync(userId);

        // Assert
        Assert.Equal(3, stats.SavedVacanciesCount);
        Assert.Equal(5, stats.TotalSearches);

        // Python: 2 збереження + 1 перегляд; решта — по одній, зв'язок за алфавітом.
        List<(string Name, int Count)> techTally =
        [
            .. stats.TopTechnologies.Select(t => (t.Name, t.Count))
        ];
        Assert.Equal(
            [("Python", 3), ("C#", 1), ("Django", 1)],
            techTally.Take(3));

        List<(string Name, int Count)> categoryTally =
        [
            .. stats.TopCategories.Select(c => (c.Name, c.Count))
        ];
        Assert.Equal(
            [("Backend", 3), ("Frontend", 1)],
            categoryTally.Take(2));

        Assert.Equal(
            ["Запит 5", "Запит 4", "Запит 3", "Запит 2", "Запит 1"],
            stats.RecentSearches);
    }

    // Test-29: нове збереження відображається в наступному запиті — кешування не перешкоджає свіжості.
    [Fact]
    public async Task GetStatsAsync_ReflectsFreshlySavedVacancyInNextRequest()
    {
        // Arrange
        Guid userId = SeedUser("JobSeeker");
        VacancyDB vacancy = CreateVacancy("Backend", "Backend", "[\"Python\"]");
        _savedRepository.SavedVacancies.Add(
            new SavedVacancyDB { Id = Guid.NewGuid(), UserId = userId, Vacancy = vacancy });

        // Act
        UserStatsDto beforeNewSave = await _service.GetStatsAsync(userId);
        _savedRepository.SavedVacancies.Add(
            new SavedVacancyDB { Id = Guid.NewGuid(), UserId = userId, Vacancy = vacancy });
        UserStatsDto afterFreshSave = await _service.GetStatsAsync(userId);

        // Assert
        Assert.Equal(1, beforeNewSave.SavedVacanciesCount);
        Assert.Equal(2, afterFreshSave.SavedVacanciesCount);
    }

    // Test-30: користувач без активності — нульові лічильники та порожні списки.
    [Fact]
    public async Task GetStatsAsync_UserWithoutActivity_ReturnsEmptyState()
    {
        // Arrange
        Guid userId = SeedUser("JobSeeker");

        // Act
        UserStatsDto stats = await _service.GetStatsAsync(userId);

        // Assert
        Assert.Equal(0, stats.SavedVacanciesCount);
        Assert.Equal(0, stats.TotalSearches);
        Assert.Empty(stats.TopTechnologies);
        Assert.Empty(stats.TopCategories);
        Assert.Empty(stats.RecentSearches);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RecordSearchAsync_BlankQuery_IsSkipped(string query)
    {
        // Arrange
        Guid userId = SeedUser("JobSeeker");

        // Act
        UserStatsRecordResult result = await _service.RecordSearchAsync(userId, query);

        // Assert
        Assert.Equal(UserStatsRecordResult.Skipped, result);
        Assert.Empty(_searchQueryRepository.Queries);
    }

    [Fact]
    public async Task RecordSearchAsync_AnalystOrUnknownUser_IsForbidden()
    {
        // Arrange
        Guid analystId = SeedUser("Analyst");
        Guid unknownUserId = Guid.NewGuid();

        // Act
        UserStatsRecordResult analystResult = await _service.RecordSearchAsync(analystId, "Python");
        UserStatsRecordResult unknownUserResult = await _service.RecordSearchAsync(unknownUserId, "Python");

        // Assert
        Assert.Equal(UserStatsRecordResult.Forbidden, analystResult);
        Assert.Equal(UserStatsRecordResult.Forbidden, unknownUserResult);
        Assert.Empty(_searchQueryRepository.Queries);
    }

    [Fact]
    public async Task RecordSearchAsync_RepeatsPreviousRecordedQueryAfterTrim_IsSkipped()
    {
        // Arrange
        Guid userId = SeedUser("JobSeeker");

        // Act
        UserStatsRecordResult first = await _service.RecordSearchAsync(userId, "Python розробник");
        UserStatsRecordResult repeated = await _service.RecordSearchAsync(userId, "  Python розробник  ");

        // Assert
        Assert.Equal(UserStatsRecordResult.Recorded, first);
        Assert.Equal(UserStatsRecordResult.Skipped, repeated);
        Assert.Single(_searchQueryRepository.Queries);
    }

    [Fact]
    public async Task RecordSearchAsync_CaseSensitiveDuplicate_IsRecorded()
    {
        // Arrange
        Guid userId = SeedUser("JobSeeker");

        // Act
        UserStatsRecordResult first = await _service.RecordSearchAsync(userId, "Python");
        UserStatsRecordResult otherCase = await _service.RecordSearchAsync(userId, "python");

        // Assert
        Assert.Equal(UserStatsRecordResult.Recorded, first);
        Assert.Equal(UserStatsRecordResult.Recorded, otherCase);
        Assert.Equal(["Python", "python"], _searchQueryRepository.AddedQueryTexts);
    }

    [Fact]
    public async Task RecordSearchAsync_TrimsAndTruncatesQueryToMaxLength()
    {
        // Arrange
        Guid userId = SeedUser("JobSeeker");
        const int MaxLength = 300;
        string longQuery = "  " + new string('x', MaxLength + 100) + "  ";

        // Act
        UserStatsRecordResult result = await _service.RecordSearchAsync(userId, longQuery);

        // Assert
        Assert.Equal(UserStatsRecordResult.Recorded, result);
        string stored = Assert.Single(_searchQueryRepository.AddedQueryTexts);
        Assert.Equal(longQuery.Trim()[..MaxLength], stored);
        Assert.Equal(MaxLength, stored.Length);
    }

    [Fact]
    public async Task RecordVacancyViewAsync_AnalystIsForbidden_JobSeekerRecorded()
    {
        // Arrange
        Guid analystId = SeedUser("Analyst");
        Guid jobSeekerId = SeedUser("JobSeeker");
        Guid vacancyId = Guid.NewGuid();

        // Act
        UserStatsRecordResult analyst = await _service.RecordVacancyViewAsync(analystId, vacancyId);
        UserStatsRecordResult jobSeeker = await _service.RecordVacancyViewAsync(jobSeekerId, vacancyId);

        // Assert
        Assert.Equal(UserStatsRecordResult.Forbidden, analyst);
        Assert.Equal(UserStatsRecordResult.Recorded, jobSeeker);
        VacancyViewDB recorded = Assert.Single(_viewRepository.Views);
        Assert.Equal(jobSeekerId, recorded.UserId);
        Assert.Equal(vacancyId, recorded.VacancyId);
    }

    [Fact]
    public async Task RecordVacancyViewAsync_RecentViewWithinWindow_IsSkipped()
    {
        // Arrange
        Guid userId = SeedUser("JobSeeker");
        Guid vacancyId = Guid.NewGuid();
        _viewRepository.RecentViewExistsToReturn = true;

        // Act
        UserStatsRecordResult result = await _service.RecordVacancyViewAsync(userId, vacancyId);

        // Assert
        Assert.Equal(UserStatsRecordResult.Skipped, result);
        Assert.Empty(_viewRepository.Views);
    }

    [Fact]
    public async Task ClearSearchHistoryAsync_ReportsWhetherAnythingWasDeleted()
    {
        // Arrange
        Guid userId = SeedUser("JobSeeker");
        await _service.RecordSearchAsync(userId, "Python");
        Guid otherUser = SeedUser("JobSeeker");
        await _service.RecordSearchAsync(otherUser, "C#");

        // Act
        bool firstClear = await _service.ClearSearchHistoryAsync(userId);
        bool secondClear = await _service.ClearSearchHistoryAsync(userId);

        // Assert
        Assert.True(firstClear);
        Assert.False(secondClear, "Друге очищення: історія вже порожня.");
        Assert.DoesNotContain(_searchQueryRepository.Queries, q => q.UserId == userId);
        Assert.Single(_searchQueryRepository.Queries);
    }
}
