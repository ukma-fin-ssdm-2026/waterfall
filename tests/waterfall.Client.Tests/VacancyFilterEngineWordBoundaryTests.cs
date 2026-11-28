// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.Services;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Repositories;

namespace Waterfall.Client.Tests;

public sealed class VacancyFilterEngineWordBoundaryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<WaterfallDbContext> _dbOptions;

    public VacancyFilterEngineWordBoundaryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _dbOptions = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlite(_connection)
            .Options;

        using WaterfallDbContext db = new(_dbOptions);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Theory]
    [InlineData("Java", "Frontend Developer", new[] { "JavaScript", "React" }, "SPA with JavaScript", false)]
    [InlineData("Java", "Backend Java Developer", new[] { "Java", "Spring" }, "Spring Boot", true)]
    [InlineData("Go", "Python Developer", new[] { "Python", "Django" }, "Google Cloud", false)]
    [InlineData("Go", "Go Developer", new[] { "Go" }, "gRPC services", true)]
    [InlineData("C", "C# / .NET Developer", new[] { "C#", ".NET", "CSS" }, "C++ interop", false)]
    [InlineData("C#", "Backend Developer", new[] { "C#", ".NET" }, "ASP.NET Core", true)]
    [InlineData("бухгалтер", "Бухгалтерія", new[] { "Облік" }, "Бухгалтерський облік", false)]
    [InlineData("бухгалтер", "Головний Бухгалтер", new[] { "Облік" }, "Звітність", true)]
    public void ContainsWord_UsesWordBoundariesForTitlesSkillsAndText(
        string query,
        string title,
        string[] skills,
        string description,
        bool expectedMatch)
    {
        VacancyDto vacancy = new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            Company = "TestCo",
            Category = "IT",
            Location = "Київ",
            Description = description,
            Skills = skills
        };

        bool actual = VacancyFilterEngine.ContainsWord(vacancy, query, titleOnly: false);

        Assert.Equal(expectedMatch, actual);
    }

    [Theory]
    [InlineData("Java")]
    [InlineData("Go")]
    [InlineData("C")]
    [InlineData("C#")]
    [InlineData(".NET")]
    [InlineData("бухгалтер")]
    public async Task ServerRepositoryAndClientFilterEngine_AgreeOnMatchedVacancies(string skillTerm)
    {
        // Arrange: спільний корпус вакансій у серверній БД та клієнтському DTO-списку
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<VacancyDB> corpus =
        [
            MakeEntity("Java Backend", "[\"Java\",\"Spring\"]", "REST APIs", now),
            MakeEntity("JavaScript Frontend", "[\"JavaScript\",\"React\"]", "SPA in JavaScript", now),
            MakeEntity("Go Systems Engineer", "[\"Go\"]", "High-load backend", now),
            MakeEntity("Python Django Dev", "[\"Python\",\"Django\"]", "Google Cloud", now),
            MakeEntity("Embedded C Engineer", "[\"C\",\"Linux\"]", "Firmware", now),
            MakeEntity("Senior C# Developer", "[\"C#\",\".NET\"]", "ASP.NET Core", now),
            MakeEntity("C++ Engine Developer", "[\"C++\"]", "Graphics", now),
            MakeEntity("Головний Бухгалтер", "[\"Фінанси\"]", "Фінансова звітність", now),
            MakeEntity("Спеціаліст відділу", "[\"Бухгалтерія\"]", "Бухгалтерія підприємства", now)
        ];

        await using WaterfallDbContext db = new(_dbOptions);
        db.Vacancies.AddRange(corpus);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        EfVacancyRepository serverRepo = new(db);
        List<VacancyDto> clientDtos = corpus
            .Select(e => new VacancyDto
            {
                Id = e.Id,
                Title = e.Title,
                Company = e.Company,
                Description = e.Description,
                Category = e.Category,
                Location = e.Location,
                Skills = JsonSerializer.Deserialize<string[]>(e.SkillsJson) ?? []
            })
            .ToList();

        // Act
        (IReadOnlyList<VacancyDB> serverMatches, _) = await serverRepo.GetAllAsync(skills: [skillTerm], page: 1, pageSize: 50);
        List<Guid> serverIds = serverMatches.Select(v => v.Id).Order().ToList();
        List<Guid> clientIds = clientDtos
            .Where(d => VacancyFilterEngine.ContainsWord(d, skillTerm, titleOnly: false))
            .Select(d => d.Id)
            .Order()
            .ToList();

        // Assert
        Assert.Equal(serverIds, clientIds);
        Assert.NotEmpty(serverIds);
    }

    private static VacancyDB MakeEntity(string title, string skillsJson, string description, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ExternalId = Guid.NewGuid().ToString("N"),
        Title = title,
        Company = "ParityCo",
        Description = description,
        Category = "IT",
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
