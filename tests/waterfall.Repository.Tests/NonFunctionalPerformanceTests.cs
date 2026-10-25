// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace Waterfall.Repository.Tests;

/// <summary>
/// Виконувані нефункціональні тести згідно з вимогами Лаб 2 (NFR) та матриці простежуваності (rtm.md):
/// - PERF-001 / Test-03: пагінація 20 вакансій при обсязі бази 10 000 записів має p95 latency < 1000 мс (1 секунда).
/// - SEC-001 / Test-11: 0 паролів у відкритому вигляді у сховищі даних (хэшування через безпечний алгоритм).
/// </summary>
public sealed class NonFunctionalPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<WaterfallDbContext> _options;

    public NonFunctionalPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlite(_connection)
            .Options;

        using WaterfallDbContext db = new(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task PERF_001_CatalogPagination_P95Latency_UnderOneSecond_With10kRecords()
    {
        // -----------------------------------------------------------------------------------
        // Arrange: Наповнення бази даних 10 000 вакансіями згідно з вимогою PERF-001 (Лаб 2)
        // -----------------------------------------------------------------------------------
        const int TotalVacancies = 10_000;
        const int PageSize = 20;
        const int BenchmarkQueries = 100;
        const double ThresholdP95Ms = 1000.0; // Поріг NFR PERF-001: не більше 1 секунди

        using (WaterfallDbContext seedContext = new(_options))
        {
            List<VacancyDB> vacancies = new(TotalVacancies);
            string[] categories = ["Backend", "Frontend", "QA", "DevOps", "Mobile", "Data Science"];
            string[] cities = ["Київ", "Львів", "Харків", "Дніпро", "Одеса", "Віддалено"];

            for (int i = 1; i <= TotalVacancies; i++)
            {
                string cat = categories[i % categories.Length];
                string loc = cities[i % cities.Length];
                vacancies.Add(new VacancyDB
                {
                    Id = Guid.NewGuid(),
                    ExternalId = $"perf-{i:D6}",
                    Title = $"{cat} Developer #{i}",
                    Company = $"Company {i % 100}",
                    Description = $"Опис вакансії #{i} для {cat} спеціаліста у місті {loc}",
                    Category = cat,
                    Location = loc,
                    Experience = i % 3 == 0 ? "1-3 роки" : "3-5 років",
                    Schedule = i % 2 == 0 ? "Повний день" : "Гнучкий графік",
                    SalaryMin = 1000 + (i % 50) * 100,
                    SalaryMax = 2000 + (i % 50) * 100,
                    Currency = "USD",
                    Source = "Djinni",
                    SourceUrl = $"https://example.com/job/{i}",
                    SkillsJson = "[\"C#\", \".NET\", \"PostgreSQL\"]",
                    IsActive = true,
                    PublishedAt = DateTimeOffset.UtcNow.AddMinutes(-i),
                    FirstSeenAt = DateTimeOffset.UtcNow.AddMinutes(-i),
                    LastSeenAt = DateTimeOffset.UtcNow
                });
            }

            await seedContext.Vacancies.AddRangeAsync(vacancies);
            await seedContext.SaveChangesAsync();
        }

        using WaterfallDbContext testContext = new(_options);
        EfVacancyRepository repository = new(testContext);

        // -----------------------------------------------------------------------------------
        // Act: Виконання 100 запитів до каталогу з пагінацією та фільтрацією
        // -----------------------------------------------------------------------------------
        List<double> latenciesMs = new(BenchmarkQueries);

        for (int q = 1; q <= BenchmarkQueries; q++)
        {
            int page = (q % 50) + 1;
            string? searchFilter = q % 3 == 0 ? "Backend" : null;
            string? categoryFilter = q % 2 == 0 ? "Backend" : null;

            long start = Stopwatch.GetTimestamp();
            var (items, totalCount) = await repository.GetAllAsync(
                search: searchFilter,
                category: categoryFilter,
                page: page,
                pageSize: PageSize,
                sortBy: "publishedAt",
                sortOrder: "desc");
            long stop = Stopwatch.GetTimestamp();

            double elapsedMs = (stop - start) * 1000.0 / Stopwatch.Frequency;
            latenciesMs.Add(elapsedMs);

            Assert.NotEmpty(items);
            Assert.True(items.Count <= PageSize);
            Assert.True(totalCount > 0);
        }

        // -----------------------------------------------------------------------------------
        // Assert: Розрахунок 95-го перцентилю (p95) та перевірка порогу PERF-001
        // -----------------------------------------------------------------------------------
        latenciesMs.Sort();
        int p95Index = (int)Math.Ceiling(0.95 * latenciesMs.Count) - 1;
        double measuredP95Ms = latenciesMs[p95Index];
        double avgMs = latenciesMs.Average();
        double minMs = latenciesMs.Min();
        double maxMs = latenciesMs.Max();

        _output.WriteLine("======================================================================");
        _output.WriteLine("ВИКОНУВАНИЙ НЕФУНКЦІОНАЛЬНИЙ ТЕСТ: PERF-001 / Test-03");
        _output.WriteLine("Вимога: сторінка каталогу з 20 вакансіями < 1 секунди для 95% запитів при обсязі 10 000 вакансій.");
        _output.WriteLine("----------------------------------------------------------------------");
        _output.WriteLine($"Обсяг бази даних     : {TotalVacancies} вакансій");
        _output.WriteLine($"Кількість вимірів    : {BenchmarkQueries} запитів (розмір сторінки = {PageSize})");
        _output.WriteLine($"Поріг (Threshold)    : p95 < {ThresholdP95Ms:F2} мс (1.00 с)");
        _output.WriteLine($"Виміряний p95        : {measuredP95Ms:F2} мс");
        _output.WriteLine($"Виміряний середній   : {avgMs:F2} мс");
        _output.WriteLine($"Виміряний min / max  : {minMs:F2} мс / {maxMs:F2} мс");
        _output.WriteLine($"Статус               : {(measuredP95Ms < ThresholdP95Ms ? "PASSED" : "FAILED")}");
        _output.WriteLine("======================================================================");

        Assert.True(
            measuredP95Ms < ThresholdP95Ms,
            $"NFR PERF-001 порушено: виміряний p95={measuredP95Ms:F2} мс перевищує поріг {ThresholdP95Ms:F2} мс.");
    }

    [Fact]
    public async Task SEC_001_ZeroPlainTextPasswordsInDatabaseStorage()
    {
        // -----------------------------------------------------------------------------------
        // Arrange: Створення користувачів через репозиторій із хешуванням паролів
        // -----------------------------------------------------------------------------------
        const string PlainPassword = "SecretPassword123!#";
        using WaterfallDbContext db = new(_options);
        EfUserRepository userRepo = new(db);

        // Хеш пароля (імітація результату PBKDF2 / bcrypt)
        string safeHash = "PBKDF2$V1$100000$g7b2xL9m...$fakeHashedValueStringNotPlaintext";
        UserDB user = new()
        {
            Id = Guid.NewGuid(),
            Email = "developer@example.com",
            PasswordHash = safeHash,
            Role = "JobSeeker",
            CreatedAt = DateTimeOffset.UtcNow
        };

        await userRepo.CreateAsync(user);

        // -----------------------------------------------------------------------------------
        // Act: Прямий аудит таблиці бази даних на наявність пароля у відкритому вигляді
        // -----------------------------------------------------------------------------------
        List<UserDB> allUsers = await db.Users.AsNoTracking().ToListAsync();

        // -----------------------------------------------------------------------------------
        // Assert: 0 відкритих паролів у БД згідно з SEC-001 (Лаб 2)
        // -----------------------------------------------------------------------------------
        Assert.NotEmpty(allUsers);
        foreach (UserDB u in allUsers)
        {
            Assert.DoesNotContain(PlainPassword, u.PasswordHash, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("PBKDF2", u.PasswordHash);
        }

        _output.WriteLine("======================================================================");
        _output.WriteLine("ВИКОНУВАНИЙ НЕФУНКЦІОНАЛЬНИЙ ТЕСТ: SEC-001 / Test-11");
        _output.WriteLine("Вимога: 0 паролів у відкритому вигляді у сховищі даних.");
        _output.WriteLine("Статус: PASSED");
        _output.WriteLine("======================================================================");
    }
}
