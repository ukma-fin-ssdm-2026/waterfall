// AI: модульні тести репозиторію вакансій (EfVacancyRepository, Lab 06); перевірено: запуск dotnet test, самоперевірка автора (Денис Сидоренко, §8)

using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Repositories;

namespace Waterfall.Repository.Tests;

/// <summary>
/// Модульні тести для EfVacancyRepository згідно з шаблоном AAA (Arrange-Act-Assert)
/// на ізольованій in-memory SQLite базі даних.
/// </summary>
public sealed class EfVacancyRepositoryTests : IDisposable
{
    private readonly SqliteTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static VacancyDB CreateVacancy(
        string title = ".NET Backend Developer",
        string company = "TechCorp",
        string category = ".NET",
        string location = "Kyiv",
        string? experience = "2-3 роки",
        string? schedule = "Повний день",
        decimal? minSalary = 2000,
        decimal? maxSalary = 3500,
        string source = "Djinni",
        string? externalId = null,
        bool isActive = true,
        DateTimeOffset? publishedAt = null)
    {
        DateTimeOffset now = publishedAt ?? DateTimeOffset.UtcNow;
        return new VacancyDB
        {
            Id = Guid.NewGuid(),
            ExternalId = externalId ?? Guid.NewGuid().ToString("N"),
            Title = title,
            Company = company,
            Description = $"Опис вакансії {title} в компанії {company}",
            Category = category,
            Location = location,
            Experience = experience,
            Schedule = schedule,
            SalaryMin = minSalary,
            SalaryMax = maxSalary,
            Currency = "USD",
            Source = source,
            SourceUrl = "https://example.com/job/1",
            SkillsJson = "[\"C#\", \".NET\", \"PostgreSQL\"]",
            PublishedAt = now,
            FirstSeenAt = now,
            LastSeenAt = now,
            IsActive = isActive
        };
    }

    [Fact]
    public async Task GetAllAsync_NoFilters_ReturnsActiveVacanciesOrderedByPublishedAtDescending()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfVacancyRepository repo = new(db);

        DateTimeOffset baseTime = DateTimeOffset.UtcNow;
        VacancyDB oldVac = CreateVacancy(title: "Old Vacancy", publishedAt: baseTime.AddDays(-2));
        VacancyDB newVac = CreateVacancy(title: "New Vacancy", publishedAt: baseTime.AddDays(-1));
        VacancyDB inactiveVac = CreateVacancy(title: "Inactive Vacancy", isActive: false);

        db.Vacancies.AddRange(oldVac, newVac, inactiveVac);
        await db.SaveChangesAsync();

        // Act
        (IReadOnlyList<VacancyDB> items, int totalCount) = await repo.GetAllAsync(onlyActive: true);

        // Assert
        Assert.Equal(2, totalCount);
        Assert.Equal(2, items.Count);
        Assert.Equal(newVac.Id, items[0].Id);
        Assert.Equal(oldVac.Id, items[1].Id);
    }

    [Theory]
    [InlineData("Frontend", "Title search")]
    [InlineData("CoolStartup", "Company search")]
    [InlineData("SpecialDesc", "Description search")]
    [InlineData("React", "Skills search")]
    public async Task GetAllAsync_SearchFilter_MatchesTitleCompanyDescriptionOrSkills(string searchTerm, string scenario)
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfVacancyRepository repo = new(db);

        VacancyDB target = CreateVacancy(
            title: "Frontend React Developer",
            company: "CoolStartup",
            category: "JavaScript");
        target.Description = "SpecialDesc for frontend developer";
        target.SkillsJson = "[\"React\", \"Redux\"]";

        VacancyDB other = CreateVacancy(
            title: "DevOps Engineer",
            company: "CloudInc",
            category: "DevOps");
        other.Description = "DevOps infrastructure role";
        other.SkillsJson = "[\"Docker\", \"Kubernetes\"]";

        db.Vacancies.AddRange(target, other);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Act
        (IReadOnlyList<VacancyDB> items, int totalCount) = await repo.GetAllAsync(search: searchTerm);

        // Assert
        Assert.True(totalCount >= 1, $"Scenario '{scenario}' should return at least 1 match.");
        Assert.Contains(items, v => v.Id == target.Id);
        Assert.DoesNotContain(items, v => v.Id == other.Id);
    }

    [Fact]
    public async Task GetAllAsync_CategoryFilter_FiltersCaseInsensitively()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfVacancyRepository repo = new(db);

        VacancyDB dotnet = CreateVacancy(category: ".NET");
        VacancyDB python = CreateVacancy(category: "Python");
        db.Vacancies.AddRange(dotnet, python);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Act
        (IReadOnlyList<VacancyDB> items, int totalCount) = await repo.GetAllAsync(category: " .net ");

        // Assert
        Assert.Equal(1, totalCount);
        Assert.Equal(dotnet.Id, items.Single().Id);
    }

    [Fact]
    public async Task GetAllAsync_ExperienceAndScheduleFilters_FilterCorrectlyAndHandleNulls()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfVacancyRepository repo = new(db);

        VacancyDB vac1 = CreateVacancy(experience: "3-5 years", schedule: "Remote");
        VacancyDB vac2 = CreateVacancy(experience: null, schedule: null);
        db.Vacancies.AddRange(vac1, vac2);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Act
        (IReadOnlyList<VacancyDB> expMatches, int expCount) = await repo.GetAllAsync(experience: "3-5");
        (IReadOnlyList<VacancyDB> schMatches, int schCount) = await repo.GetAllAsync(schedule: "remote");

        // Assert
        Assert.Equal(1, expCount);
        Assert.Equal(vac1.Id, expMatches.Single().Id);

        Assert.Equal(1, schCount);
        Assert.Equal(vac1.Id, schMatches.Single().Id);
    }

    [Fact]
    public async Task GetAllAsync_LocationFilter_MatchesLocationSubstring()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfVacancyRepository repo = new(db);

        VacancyDB kyivVac = CreateVacancy(location: "Kyiv, Ukraine");
        VacancyDB lvivVac = CreateVacancy(location: "Lviv, Ukraine");
        db.Vacancies.AddRange(kyivVac, lvivVac);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Act
        (IReadOnlyList<VacancyDB> items, int totalCount) = await repo.GetAllAsync(location: "KYIV");

        // Assert
        Assert.Equal(1, totalCount);
        Assert.Equal(kyivVac.Id, items.Single().Id);
    }

    [Theory]
    [InlineData(2500, 1)] // MinSalary >= 2500 -> matches v1 (2000..3500) because max >= 2500
    [InlineData(4000, 0)] // MinSalary >= 4000 -> no matches
    public async Task GetAllAsync_MinSalaryFilter_ChecksSalaryMinOrMax(decimal minSalary, int expectedCount)
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfVacancyRepository repo = new(db);

        VacancyDB v1 = CreateVacancy(minSalary: 2000, maxSalary: 3500);
        db.Vacancies.Add(v1);
        await db.SaveChangesAsync();

        // Act
        (IReadOnlyList<VacancyDB> items, int totalCount) = await repo.GetAllAsync(minSalary: minSalary);

        // Assert
        Assert.Equal(expectedCount, totalCount);
        Assert.Equal(expectedCount, items.Count);
    }

    [Theory]
    [InlineData(1500, 0)] // MaxSalary <= 1500 -> no matches for 2000..3500
    [InlineData(2500, 1)] // MaxSalary <= 2500 -> matches because min 2000 <= 2500
    public async Task GetAllAsync_MaxSalaryFilter_ChecksSalaryMaxOrMin(decimal maxSalary, int expectedCount)
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfVacancyRepository repo = new(db);

        VacancyDB v1 = CreateVacancy(minSalary: 2000, maxSalary: 3500);
        db.Vacancies.Add(v1);
        await db.SaveChangesAsync();

        // Act
        (IReadOnlyList<VacancyDB> items, int totalCount) = await repo.GetAllAsync(maxSalary: maxSalary);

        // Assert
        Assert.Equal(expectedCount, totalCount);
        Assert.Equal(expectedCount, items.Count);
    }

    [Fact]
    public async Task GetAllAsync_SourceFilter_MatchesExactSource()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfVacancyRepository repo = new(db);

        VacancyDB djinni = CreateVacancy(source: "Djinni");
        VacancyDB dou = CreateVacancy(source: "DOU");
        db.Vacancies.AddRange(djinni, dou);
        await db.SaveChangesAsync();

        // Act
        (IReadOnlyList<VacancyDB> items, int totalCount) = await repo.GetAllAsync(source: "Djinni");

        // Assert
        Assert.Equal(1, totalCount);
        Assert.Equal(djinni.Id, items.Single().Id);
    }

    [Fact]
    public async Task GetAllAsync_OnlyActiveFalse_ConcatenatesActiveAndArchiveVacancies()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfVacancyRepository repo = new(db);

        VacancyDB active = CreateVacancy(title: "Active", isActive: true);
        VacancyDB inactive = CreateVacancy(title: "Archive", isActive: false);
        db.Vacancies.AddRange(active, inactive);
        await db.SaveChangesAsync();

        // Act
        (IReadOnlyList<VacancyDB> items, int totalCount) = await repo.GetAllAsync(onlyActive: false);

        // Assert
        Assert.Equal(2, totalCount);
        Assert.Contains(items, v => v.Id == active.Id);
        Assert.Contains(items, v => v.Id == inactive.Id);
    }

    [Theory]
    [InlineData("title", "asc", "Alpha", "Zeta")]
    [InlineData("title", "desc", "Zeta", "Alpha")]
    [InlineData("salarymin", "asc", "Alpha", "Zeta")] // Alpha min: 1000, Zeta min: 3000
    [InlineData("salarymin", "desc", "Zeta", "Alpha")]
    [InlineData("salarymax", "asc", "Alpha", "Zeta")] // Alpha max: 2000, Zeta max: 4000
    [InlineData("salarymax", "desc", "Zeta", "Alpha")]
    [InlineData("firstseenat", "asc", "Alpha", "Zeta")]
    [InlineData("firstseenat", "desc", "Zeta", "Alpha")]
    public async Task GetAllAsync_Sorting_OrdersItemsCorrectly(
        string sortBy,
        string sortOrder,
        string expectedFirstTitle,
        string expectedSecondTitle)
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfVacancyRepository repo = new(db);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        VacancyDB alpha = CreateVacancy(
            title: "Alpha",
            minSalary: 1000,
            maxSalary: 2000,
            publishedAt: now.AddDays(-5));
        alpha.FirstSeenAt = now.AddDays(-5);

        VacancyDB zeta = CreateVacancy(
            title: "Zeta",
            minSalary: 3000,
            maxSalary: 4000,
            publishedAt: now.AddDays(-1));
        zeta.FirstSeenAt = now.AddDays(-1);

        db.Vacancies.AddRange(alpha, zeta);
        await db.SaveChangesAsync();

        // Act
        (IReadOnlyList<VacancyDB> items, _) = await repo.GetAllAsync(sortBy: sortBy, sortOrder: sortOrder);

        // Assert
        Assert.Equal(2, items.Count);
        Assert.Equal(expectedFirstTitle, items[0].Title);
        Assert.Equal(expectedSecondTitle, items[1].Title);
    }

    [Fact]
    public async Task GetAllAsync_Pagination_SkipsAndTakesExpectedWindow()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfVacancyRepository repo = new(db);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int i = 1; i <= 5; i++)
        {
            db.Vacancies.Add(CreateVacancy(title: $"Job {i}", publishedAt: now.AddMinutes(i)));
        }
        await db.SaveChangesAsync();

        // Act - Page 1 (size 2), should get Job 5 and Job 4 (descending by publishedAt)
        (IReadOnlyList<VacancyDB> page1, int totalCount) = await repo.GetAllAsync(page: 1, pageSize: 2);
        // Act - Page 2 (size 2), should get Job 3 and Job 2
        (IReadOnlyList<VacancyDB> page2, _) = await repo.GetAllAsync(page: 2, pageSize: 2);

        // Assert
        Assert.Equal(5, totalCount);
        Assert.Equal(2, page1.Count);
        Assert.Equal("Job 5", page1[0].Title);
        Assert.Equal("Job 4", page1[1].Title);

        Assert.Equal(2, page2.Count);
        Assert.Equal("Job 3", page2[0].Title);
        Assert.Equal("Job 2", page2[1].Title);
    }


    [Fact]
    public async Task BulkUpsertAsync_CreatesNewAndUpdatesExisting_PreservingIdAndTimestamps()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfVacancyRepository repo = new(db);

        DateTimeOffset initialPublished = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset initialFirstSeen = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        VacancyDB existing = CreateVacancy(
            title: "Original Title",
            source: "Djinni",
            externalId: "ext-100",
            publishedAt: initialPublished);
        existing.FirstSeenAt = initialFirstSeen;

        db.Vacancies.Add(existing);
        await db.SaveChangesAsync();

        VacancyDB updateCandidate = CreateVacancy(
            title: "Updated Title via BulkUpsert",
            source: "Djinni",
            externalId: "ext-100",
            publishedAt: DateTimeOffset.UtcNow); // should be ignored

        VacancyDB brandNew = CreateVacancy(
            title: "Brand New Vacancy",
            source: "Djinni",
            externalId: "ext-200");

        // Act
        (int created, int updated) = await repo.BulkUpsertAsync([updateCandidate, brandNew]);

        // Assert
        Assert.Equal(1, created);
        Assert.Equal(1, updated);

        await using WaterfallDbContext verifyDb = _fixture.CreateContext();
        VacancyDB? updatedStored = verifyDb.Vacancies.FirstOrDefault(v => v.ExternalId == "ext-100");
        Assert.NotNull(updatedStored);
        Assert.Equal(existing.Id, updatedStored.Id);
        Assert.Equal("Updated Title via BulkUpsert", updatedStored.Title);
        Assert.Equal(initialPublished, updatedStored.PublishedAt);
        Assert.Equal(initialFirstSeen, updatedStored.FirstSeenAt);

        VacancyDB? newStored = verifyDb.Vacancies.FirstOrDefault(v => v.ExternalId == "ext-200");
        Assert.NotNull(newStored);
        Assert.Equal("Brand New Vacancy", newStored.Title);
    }
}
