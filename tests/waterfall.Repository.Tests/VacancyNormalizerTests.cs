// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Normalization;

namespace Waterfall.Repository.Tests;

/// <summary>
/// Тести нормалізатора вакансії: похідні поля (пошуковий текст, формат роботи, широка категорія)
/// та їх перерахунок на кожному шляху запису через DbContext.
/// </summary>
public sealed class VacancyNormalizerTests : IDisposable
{
    private readonly SqliteTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static VacancyDB Vacancy(string? location = "Київ", string? schedule = null, string category = ".NET") => new()
    {
        Id = Guid.NewGuid(),
        Title = "Backend Developer",
        Company = "TechCorp",
        Description = "Розробка сервісів",
        Category = category,
        Location = location ?? string.Empty,
        Schedule = schedule,
        Currency = "USD",
        Source = "Djinni",
        SourceUrl = "https://djinni.co/jobs/1/",
        SkillsJson = "[]"
    };

    [Theory]
    // Явна згадка формату перемагає: місто поруч із «віддалено» не робить вакансію офісною
    [InlineData("Київ, віддалено", null, WorkFormat.Remote)]
    [InlineData("Київ (Віддалено)", "Повна зайнятість · Гнучкий графік", WorkFormat.Remote)]
    [InlineData("Віддалено", "Повна зайнятість · Віддалена робота", WorkFormat.Remote)]
    [InlineData("за кордоном, віддалено", null, WorkFormat.Remote)]
    [InlineData("Львів / Віддалено", null, WorkFormat.Remote)]
    // Гібрид — окремий формат, а не Remote|Office
    [InlineData("Київ (Гібрид)", "Повна зайнятість · Гібридний формат", WorkFormat.Hybrid)]
    // Офіс явно або через місто
    [InlineData("Київ (Офіс)", "Повна зайнятість · Офіс: 5/2", WorkFormat.Office)]
    [InlineData("Київ", "Повна зайнятість · Стандартний графік: 5/2", WorkFormat.Office)]
    [InlineData("Україна (Київ, Львів)", null, WorkFormat.Office)]
    // Кілька форматів одночасно
    [InlineData("Київ", "Можна віддалено або в офісі", WorkFormat.Remote | WorkFormat.Office)]
    // Без міста й без формату
    [InlineData("Не вказано", null, WorkFormat.None)]
    [InlineData("Україна", null, WorkFormat.None)]
    [InlineData(null, null, WorkFormat.None)]
    public void ResolveWorkFormat_DerivesFlagsFromLocationAndSchedule(string? location, string? schedule, WorkFormat expected)
    {
        Assert.Equal(expected, VacancyNormalizer.ResolveWorkFormat(location, schedule));
    }

    [Fact]
    public void BuildSearchText_LowercasesCyrillicAndIncludesTags()
    {
        VacancyDB vacancy = Vacancy();
        vacancy.Title = "Senior Бухгалтер";
        vacancy.Company = "Фінансова Група";
        vacancy.Description = "Облік та Звітність";
        vacancy.SkillsJson = "[\"1С\", \"Excel\"]";

        string searchText = VacancyNormalizer.BuildSearchText(vacancy);

        Assert.Contains("senior бухгалтер", searchText, StringComparison.Ordinal);
        Assert.Contains("фінансова група", searchText, StringComparison.Ordinal);
        Assert.Contains("облік та звітність", searchText, StringComparison.Ordinal);
        Assert.Contains("1с", searchText, StringComparison.Ordinal);
        Assert.Contains("excel", searchText, StringComparison.Ordinal);
        Assert.DoesNotContain("Бухгалтер", searchText, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSearchText_BrokenSkillsJson_StillGivesText()
    {
        VacancyDB vacancy = Vacancy();
        vacancy.SkillsJson = "{ не json";

        string searchText = VacancyNormalizer.BuildSearchText(vacancy);

        Assert.Contains("backend developer", searchText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("QA Automation", "QA")]
    [InlineData("QA", "QA")]
    [InlineData(".NET", "Software Development")]
    [InlineData("JavaScript / Front-End", "Software Development")]
    [InlineData("Data Science", "AI / ML")]
    [InlineData("Analytics", "Data")]
    [InlineData("DevOps", "DevOps")]
    [InlineData("Legal", "Legal")]
    [InlineData("Sales", "Sales")]
    [InlineData("Design", "Design")]
    [InlineData("HR", "HR")]
    [InlineData("Finances", "Finance")]
    [InlineData("Інше", "Other")]
    [InlineData("Astronaut", "Other")]
    [InlineData("", "Other")]
    [InlineData(null, "Other")]
    public void ResolveCategory_MapsSourceCategoriesToCanonicalGroups(string? category, string expected)
    {
        Assert.Equal(expected, VacancyCategoryMap.Resolve(category));
    }

    [Fact]
    public void CategoryMap_All_StartsWithCanonicalGroupsAndEndsWithFallback()
    {
        IReadOnlyList<string> all = VacancyCategoryMap.All;

        Assert.Equal(VacancyCategoryMap.Other, all[^1]);
        Assert.Contains("Software Development", all);
        Assert.Contains("Legal", all);
        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task SaveChanges_ComputesDerivedFieldsForAddedVacancy()
    {
        await using WaterfallDbContext db = _fixture.CreateContext();
        VacancyDB vacancy = Vacancy(location: "Київ, віддалено", category: "QA Automation");
        vacancy.Title = "Бухгалтер";
        db.Vacancies.Add(vacancy);

        await db.SaveChangesAsync();

        await using WaterfallDbContext verify = _fixture.CreateContext();
        VacancyDB stored = await verify.Vacancies.SingleAsync(v => v.Id == vacancy.Id);
        Assert.Equal(WorkFormat.Remote, stored.WorkFormat);
        Assert.Equal("QA", stored.CategoryGroup);
        Assert.Contains("бухгалтер", stored.SearchText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveChanges_RecomputesDerivedFieldsForModifiedVacancy()
    {
        await using WaterfallDbContext db = _fixture.CreateContext();
        VacancyDB vacancy = Vacancy(location: "Київ (Офіс)", category: ".NET");
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        vacancy.Location = "Віддалено";
        vacancy.Category = "Legal";
        vacancy.Title = "Юрист";
        await db.SaveChangesAsync();

        await using WaterfallDbContext verify = _fixture.CreateContext();
        VacancyDB stored = await verify.Vacancies.SingleAsync(v => v.Id == vacancy.Id);
        Assert.Equal(WorkFormat.Remote, stored.WorkFormat);
        Assert.Equal("Legal", stored.CategoryGroup);
        Assert.Contains("юрист", stored.SearchText, StringComparison.Ordinal);
    }
}
