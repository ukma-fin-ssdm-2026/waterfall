// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Helpers;
using Waterfall.Repository.Repositories;

namespace Waterfall.Repository.Tests;

public sealed class VacancyWordMatcherTests : IDisposable
{
    private readonly SqliteTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Theory]
    [InlineData("Senior Java Developer", "Java", true)]
    [InlineData("Java/Kotlin Engineer", "Java", true)]
    [InlineData("We use Java.", "Java", true)]
    [InlineData("JavaScript Developer", "Java", false)]
    [InlineData("TypeScript / JavaScript", "Java", false)]
    [InlineData("Go / Golang Engineer", "Go", true)]
    [InlineData("Google Cloud Engineer", "Go", false)]
    [InlineData("Python & Django Developer", "Go", false)]
    [InlineData("C, Linux Embedded", "C", true)]
    [InlineData("C/C++ Systems Engineer", "C", true)]
    [InlineData("C# / .NET Developer", "C", false)]
    [InlineData("C++ Graphics Engineer", "C", false)]
    [InlineData("HTML & CSS Designer", "C", false)]
    [InlineData("C# / .NET Developer", "C#", true)]
    [InlineData("C# / .NET Developer", ".NET", true)]
    [InlineData("C++ Graphics Engineer", "C++", true)]
    [InlineData("Node.js Backend", "Node", false)]
    [InlineData("Node.js Backend", "Node.js", true)]
    [InlineData("Головний бухгалтер.", "бухгалтер", true)]
    [InlineData("Головний Бухгалтер", "БУХГАЛТЕР", true)]
    [InlineData("Бухгалтерія та аудит", "бухгалтер", false)]
    public void ContainsWord_RespectsTokenBoundariesAndUnicodeCase(string text, string term, bool expectedMatch)
    {
        bool actual = VacancyWordMatcher.ContainsWord(text, term);

        Assert.Equal(expectedMatch, actual);
    }

    [Theory]
    [InlineData("[\"Java\"]", "Java", true)]
    [InlineData("[\"JavaScript\"]", "Java", false)]
    [InlineData("[\"C#\", \".NET\"]", "C", false)]
    [InlineData("[\"C#\", \".NET\"]", "C#", true)]
    [InlineData("[\"Комунікація\"]", "комунікація", true)]
    public void MatchesSkillsJson_MatchesWholeSkillTagsWithoutSubstringBleed(string skillsJson, string term, bool expectedMatch)
    {
        bool actual = VacancyWordMatcher.MatchesSkillsJson(skillsJson, term);

        Assert.Equal(expectedMatch, actual);
    }

    [Fact]
    public async Task GetAllAsync_SkillsFilter_ExcludesJavaScriptWhenFilteringByJavaAndComputesAccurateTotalCount()
    {
        // Arrange
        await using WaterfallDbContext db = _fixture.CreateContext();
        EfVacancyRepository repo = new(db);

        VacancyDB javaRole = CreateVacancy("Backend Java Engineer", "[\"Java\",\"Spring\"]", "Microservices in Java");
        VacancyDB jsRole = CreateVacancy("Frontend Developer", "[\"JavaScript\",\"React\"]", "SPA with JavaScript");
        VacancyDB tsWithJavaInText = CreateVacancy("Fullstack Engineer", "[\"TypeScript\"]", "Integrates with Java/Kotlin services");
        db.Vacancies.AddRange(javaRole, jsRole, tsWithJavaInText);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Act
        (IReadOnlyList<VacancyDB> items, int totalCount) = await repo.GetAllAsync(skills: ["Java"], page: 1, pageSize: 1);

        // Assert: лише Java та Java/Kotlin у тексті, без JavaScript; TotalCount == 2 попри pageSize == 1
        Assert.Equal(2, totalCount);
        Assert.Single(items);

        (IReadOnlyList<VacancyDB> allMatches, int allCount) = await repo.GetAllAsync(skills: ["Java"], page: 1, pageSize: 10);
        Assert.Equal(2, allCount);
        Assert.Contains(allMatches, v => v.Id == javaRole.Id);
        Assert.Contains(allMatches, v => v.Id == tsWithJavaInText.Id);
        Assert.DoesNotContain(allMatches, v => v.Id == jsRole.Id);
    }

    private static VacancyDB CreateVacancy(string title, string skillsJson, string description)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new VacancyDB
        {
            Id = Guid.NewGuid(),
            ExternalId = Guid.NewGuid().ToString("N"),
            Title = title,
            Company = "TestCo",
            Description = description,
            Category = "Backend",
            Location = "Київ",
            Currency = "USD",
            Source = "Djinni",
            SourceUrl = $"https://djinni.co/jobs/{Guid.NewGuid():N}",
            SkillsJson = skillsJson,
            PublishedAt = now,
            FirstSeenAt = now,
            LastSeenAt = now,
            IsActive = true
        };
    }
}
