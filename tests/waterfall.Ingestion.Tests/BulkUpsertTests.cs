// AI: тести масового збереження вакансій (BulkUpsertAsync, issue #62, Lab 06); перевірено: запуск dotnet test, самоперевірка автора (§8)

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Repositories;

namespace Waterfall.Ingestion.Tests;

/// <summary>
/// Перевіряє ключ оновлення «джерело + id на сайті» на справжній SQLite.
/// Фікстура: нова база в пам'яті на кожен тест (xUnit створює новий екземпляр класу), Dispose її знищує.
/// </summary>
public sealed class BulkUpsertTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<WaterfallDbContext> _options;

    public BulkUpsertTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<WaterfallDbContext>().UseSqlite(_connection).Options;
        using WaterfallDbContext db = new(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task SameExternalIdFromAnotherSite_IsSeparateVacancy()
    {
        // id на різних сайтах незалежні: 720001 на Work.ua — не та сама вакансія, що 720001 на Djinni
        await UpsertAsync(Vacancy("WorkUa", "720001"));

        (int created, int updated) = await UpsertAsync(Vacancy("Djinni", "720001"));

        Assert.Equal((1, 0), (created, updated));
        Assert.Equal(2, Count());
    }

    [Fact]
    public async Task DuplicateInsideOneBatch_IsStoredOnce()
    {
        // Якщо дамп містить ту саму вакансію двічі, друга копія оновлює першу, а не створює дубль
        (int created, int updated) = await UpsertAsync(Vacancy("Djinni", "1", "Перша"), Vacancy("Djinni", "1", "Друга"));

        Assert.Equal((1, 1), (created, updated));
        Assert.Equal(1, Count());
        await using WaterfallDbContext db = new(_options);
        Assert.Equal("Друга", db.Vacancies.Single().Title);
    }

    [Fact]
    public async Task EmptyBatch_ChangesNothing()
    {
        Assert.Equal((0, 0), await UpsertAsync());
        Assert.Equal(0, Count());
    }

    [Fact]
    public async Task Update_KeepsIdentityAndFirstSeenDates()
    {
        VacancyDB original = Vacancy("Djinni", "1", "Стара назва");
        await UpsertAsync(original);

        VacancyDB again = Vacancy("Djinni", "1", "Нова назва");
        again.PublishedAt = Now.AddDays(5);
        again.FirstSeenAt = Now.AddDays(5);
        await UpsertAsync(again);

        await using WaterfallDbContext db = new(_options);
        VacancyDB saved = db.Vacancies.Single();
        Assert.Equal(original.Id, saved.Id);
        Assert.Equal("Нова назва", saved.Title);
        Assert.Equal(Now, saved.PublishedAt);
        Assert.Equal(Now, saved.FirstSeenAt);
    }

    private async Task<(int Created, int Updated)> UpsertAsync(params VacancyDB[] vacancies)
    {
        await using WaterfallDbContext db = new(_options);
        return await new EfVacancyRepository(db).BulkUpsertAsync(vacancies);
    }

    private int Count()
    {
        using WaterfallDbContext db = new(_options);
        return db.Vacancies.Count();
    }

    private static VacancyDB Vacancy(string source, string externalId, string title = "Middle .NET Developer") => new()
    {
        Id = Guid.NewGuid(),
        Source = source,
        ExternalId = externalId,
        Title = title,
        Company = "Kvitka Software",
        Description = "Опис",
        Category = ".NET",
        Location = "Київ",
        Currency = "USD",
        SourceUrl = "https://djinni.co/jobs/1/",
        PublishedAt = Now,
        FirstSeenAt = Now,
        LastSeenAt = Now
    };
}
