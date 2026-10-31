// AI: тести нормалізації та збереження вакансій (issue #62), регресійні тести з челенджу «Зламай, потім відлагодь» (Lab 06); перевірено: запуск dotnet test, тести падають з внесеними помилками, самоперевірка автора (§8)

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Waterfall.Api.Workers;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Repositories;
using Waterfall.Services;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Ingestion.Tests;

public class VacancyIngestionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("$3500-4500", 3500, 4500, "USD")]
    [InlineData("$6000-5000", 5000, 6000, "USD")]
    [InlineData("до $2500", null, 2500, "USD")]
    [InlineData("від $2000", 2000, null, "USD")]
    [InlineData("€2500–3500", 2500, 3500, "EUR")]
    [InlineData("45 000 грн", 45000, 45000, "UAH")]
    [InlineData("$12/год", null, null, "USD")]
    [InlineData("за домовленістю", null, null, "USD")]
    [InlineData(null, null, null, "USD")] // поле зарплати відсутнє
    public void ParseSalary_ReadsCommonFormats(string? salary, int? min, int? max, string currency)
    {
        (decimal? actualMin, decimal? actualMax, string actualCurrency) = VacancyIngestionService.ParseSalary(salary);

        Assert.Equal(min, (int?)actualMin);
        Assert.Equal(max, (int?)actualMax);
        Assert.Equal(currency, actualCurrency);
    }

    [Theory]
    [InlineData("сьогодні", 0)]
    [InlineData("вчора", 24 * 60)]
    [InlineData("3 дні тому", 3 * 24 * 60)]
    [InlineData("5 годин тому", 5 * 60)]
    [InlineData("10 хв. тому", 10)]
    [InlineData("1 день тому", 24 * 60)]
    [InlineData("1 тиждень тому", 7 * 24 * 60)]
    [InlineData("щойно", 0)]
    [InlineData("незрозумілий формат", 0)]
    [InlineData(null, 0)] // дата відсутня — беремо момент збору
    public void ParsePublishedAt_ReadsRelativeDates(string? posted, int minutesAgo)
    {
        Assert.Equal(Now.AddMinutes(-minutesAgo), VacancyIngestionService.ParsePublishedAt(posted, Now));
    }


    [Fact]
    public void ParsePublishedAt_ReadsMonths()
    {
        Assert.Equal(Now.AddMonths(-2), VacancyIngestionService.ParsePublishedAt("2 місяці тому", Now));
    }

    [Theory]
    [InlineData("99999999999 днів тому")] // число не вміщується в int
    [InlineData("9999999 днів тому")]     // дата вийшла б за межі календаря
    public void ParsePublishedAt_AbsurdTimeAgo_FallsBackToNow(string posted)
    {
        Assert.Equal(Now, VacancyIngestionService.ParsePublishedAt(posted, Now));
    }

    [Fact]
    public void ParsePublishedAt_ReadsExactDates()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero), VacancyIngestionService.ParsePublishedAt("08.10.2026", Now));
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 9, 30, 0, TimeSpan.Zero), VacancyIngestionService.ParsePublishedAt("2026-10-08T09:30:00Z", Now));
        // Дата з майбутнього — помилка на сайті, тож беремо поточну
        Assert.Equal(Now, VacancyIngestionService.ParsePublishedAt("01.01.2030", Now));
    }

    [Fact]
    public void CleanText_TurnsHtmlIntoPlainText()
    {
        string html = "<p>Шукаємо&nbsp;розробника</p><script>alert('x')</script><ul><li>C#</li><li>SQL</li></ul>";

        Assert.Equal("Шукаємо розробника\nC#\nSQL", VacancyIngestionService.CleanText(html));
    }

    [Fact]
    public void Normalize_FillsVacancyFields()
    {
        RawVacancy raw = Raw() with
        {
            Title = "  Senior .NET   Developer ",
            Url = "https://djinni.co/jobs/1-dev/?utm_source=telegram#apply",
            Company = "",
            Category = "",
            Tags = "C#, .NET; c# | Docker"
        };

        (VacancyDB? vacancy, string? error) = VacancyIngestionService.Normalize(raw, Now);

        Assert.Null(error);
        Assert.NotNull(vacancy);
        Assert.Equal("Djinni", vacancy.Source);
        Assert.Equal("Senior .NET Developer", vacancy.Title);
        Assert.Equal("https://djinni.co/jobs/1-dev/", vacancy.SourceUrl);
        Assert.Equal("Не вказано", vacancy.Company);
        Assert.Equal("Інше", vacancy.Category);
        Assert.Equal("[\"C#\",\".NET\",\"Docker\"]", vacancy.SkillsJson);
        Assert.Equal(3500m, vacancy.SalaryMin);
        Assert.Equal(Now.AddDays(-2), vacancy.PublishedAt);
    }

    [Fact]
    public void Normalize_StoresSkillsReadably_ForCatalogSearch()
    {
        // Поле «Необхідні навички» на Djinni буває українською; пошук каталогу шукає підрядок у SkillsJson
        (VacancyDB? vacancy, _) = VacancyIngestionService.Normalize(Raw() with { Tags = "Комунікація, C/C++" }, Now);

        Assert.Equal("[\"Комунікація\",\"C/C++\"]", vacancy?.SkillsJson);
    }

    [Fact]
    public void Normalize_WithoutSkillsField_DoesNotGuessSkillsFromText()
    {
        // Навички беремо лише з окремого поля на сайті, а не з опису
        RawVacancy raw = Raw() with { Tags = null, DescriptionHtml = "<p>Потрібні C# та PostgreSQL</p>" };

        (VacancyDB? vacancy, _) = VacancyIngestionService.Normalize(raw, Now);

        Assert.Equal("[]", vacancy?.SkillsJson);
    }

    [Fact]
    public void Normalize_WorkUa_ChecksWorkUaDomainAndDefaultsToHryvnias()
    {
        RawVacancy raw = Raw() with { Source = JobSource.WorkUa, Url = "https://www.work.ua/jobs/8051901/", Salary = "17 000 – 35 000" };

        (VacancyDB? vacancy, string? error) = VacancyIngestionService.Normalize(raw, Now);

        Assert.Null(error);
        Assert.NotNull(vacancy);
        Assert.Equal("WorkUa", vacancy.Source);
        Assert.Equal("https://www.work.ua/jobs/8051901/", vacancy.SourceUrl);
        Assert.Equal((17000m, 35000m, "UAH"), (vacancy.SalaryMin, vacancy.SalaryMax, vacancy.Currency));
        // Посилання на Djinni у вакансії з Work.ua — підробка (THR-03)
        Assert.Contains("work.ua", VacancyIngestionService.Normalize(raw with { Url = "https://djinni.co/jobs/1-dev/" }, Now).Error);
    }

    [Theory]
    [InlineData(JobSource.Unknown)]
    [InlineData(JobSource.RobotaUa)] // збирати з Robota.ua ще не вміємо, тож і домену для перевірки посилань немає
    public void Normalize_UnsupportedSource_IsRejected(JobSource source)
    {
        (VacancyDB? vacancy, string? error) = VacancyIngestionService.Normalize(Raw() with { Source = source }, Now);

        Assert.Null(vacancy);
        Assert.Contains("невідоме джерело", error);
    }

    [Theory]
    [InlineData("17 000 – 35 000", "UAH")]
    [InlineData("$500", "USD")] // валюта в рядку важливіша за типову для сайту
    public void ParseSalary_UsesSiteCurrencyWhenNoneIsWritten(string salary, string currency)
    {
        Assert.Equal(currency, VacancyIngestionService.ParseSalary(salary, "UAH").Currency);
    }

    // Типова валюта тут XXX («без валюти» за ISO 4217): так видно, що валюту дала саме позначка в рядку
    [Theory]
    [InlineData("2500-5000 USD", "USD")] // так зарплату записує DjinniParser
    [InlineData("від 2000 EUR", "EUR")]
    [InlineData("₴30 000", "UAH")]
    [InlineData("30 000 UAH", "UAH")]
    [InlineData("$1000 або 41 000 грн", "USD")] // кілька позначок: долар перевіряється першим
    [InlineData("€900 або 38 000 грн", "EUR")] // євро — раніше за гривню
    [InlineData("30 000", "XXX")]
    public void ParseSalary_CurrencyMarkInText_WinsInFixedOrder(string salary, string currency)
    {
        Assert.Equal(currency, VacancyIngestionService.ParseSalary(salary, "XXX").Currency);
    }

    [Theory]
    [InlineData("https://djinni-careers.example.net/jobs/1/")]
    [InlineData("https://djinni.co.evil.example/jobs/1/")]
    // Домен-двійник закінчується на djinni.co, але не є його піддоменом (регресія з Lab 06)
    [InlineData("https://fakedjinni.co/jobs/1/")]
    [InlineData("http://djinni.co/jobs/1/")]
    [InlineData("не посилання")]
    [InlineData(null)]
    public void Normalize_RejectsLinksOutsideDjinni(string? url)
    {
        (VacancyDB? vacancy, string? error) = VacancyIngestionService.Normalize(Raw() with { Url = url }, Now);

        Assert.Null(vacancy);
        Assert.Contains("djinni.co", error);
    }

    [Fact]
    public void Normalize_AcceptsDjinniSubdomain()
    {
        (VacancyDB? vacancy, _) = VacancyIngestionService.Normalize(Raw() with { Url = "https://jobs.djinni.co/jobs/1-dev/" }, Now);

        Assert.Equal("https://jobs.djinni.co/jobs/1-dev/", vacancy?.SourceUrl);
    }

    [Theory]
    [InlineData("-", false)]  // 1 символ — сміття, а не назва посади
    [InlineData("QA", true)]  // 2 символи — мінімум: QA, PM, UX — справжні назви посад
    public void Normalize_TitleLengthBoundary(string title, bool accepted)
    {
        Assert.Equal(accepted, VacancyIngestionService.Normalize(Raw() with { Title = title }, Now).Vacancy != null);
    }

    [Fact]
    public void Normalize_FitsDatabaseLimits_AndTreatsBlankFieldsAsMissing()
    {
        RawVacancy raw = Raw() with
        {
            Id = new string('9', 150),
            Title = new string('T', 250),
            Company = new string('C', 200),
            Experience = "   ",
            Employment = "",
            Location = null,
            Tags = null
        };

        (VacancyDB? vacancy, _) = VacancyIngestionService.Normalize(raw, Now);

        Assert.NotNull(vacancy);
        Assert.Equal(100, vacancy.ExternalId?.Length);
        Assert.Equal(200, vacancy.Title.Length);   // обрізано до MaxLength колонки
        Assert.Equal(200, vacancy.Company.Length); // рівно на межі — без змін
        Assert.Null(vacancy.Experience);
        Assert.Null(vacancy.Schedule);
        Assert.Equal("Не вказано", vacancy.Location);
        Assert.Equal("[]", vacancy.SkillsJson);
    }

    [Fact]
    public void Normalize_RejectsVacancyWithoutTitleOrDescription()
    {
        Assert.Null(VacancyIngestionService.Normalize(Raw() with { Title = " " }, Now).Vacancy);
        Assert.Null(VacancyIngestionService.Normalize(Raw() with { DescriptionHtml = "<p></p>" }, Now).Vacancy);
        Assert.Null(VacancyIngestionService.Normalize(Raw() with { Id = null }, Now).Vacancy);
    }

    [Fact]
    public async Task SampleDump_IsSavedOnce_AndUpdatedOnRepeatedRun()
    {
        // Справжній репозиторій на SQLite в пам'яті: перевіряємо і нормалізацію, і BulkUpsertAsync
        using SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();
        DbContextOptions<WaterfallDbContext> options = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlite(connection)
            .Options;
        using (WaterfallDbContext db = new(options))
        {
            db.Database.EnsureCreated();
        }

        List<RawVacancy> rawItems = JobParserService.ParseDump(File.ReadAllText(SampleDumpPath()));

        IngestionResult first = await ProcessAsync(options, rawItems);
        Dictionary<string, VacancyDB> afterFirstRun = LoadVacancies(options).ToDictionary(v => v.ExternalId!);
        IngestionResult second = await ProcessAsync(options, rawItems);

        Assert.Equal(new IngestionResult(Created: 10, Updated: 0, Skipped: 2), first);
        Assert.Equal(new IngestionResult(Created: 0, Updated: 10, Skipped: 2), second);

        List<VacancyDB> saved = LoadVacancies(options);
        Assert.Equal(10, saved.Count);
        Assert.All(saved, v =>
        {
            Assert.Equal(afterFirstRun[v.ExternalId!].Id, v.Id);
            // «3 дні тому» в повторному прогоні не повинно зсувати дату публікації (регресія з Lab 06)
            Assert.Equal(afterFirstRun[v.ExternalId!].PublishedAt, v.PublishedAt);
        });

        // Запис із «брудними» даними: трекінг у посиланні, перевернута вилка, script в описі
        VacancyDB lead = saved.Single(v => v.ExternalId == "720096");
        Assert.Equal("https://djinni.co/jobs/720096-lead-net-engineer/", lead.SourceUrl);
        Assert.Equal(5000m, lead.SalaryMin);
        Assert.Equal(6000m, lead.SalaryMax);
        Assert.DoesNotContain("alert", lead.Description);
    }

    private static async Task<IngestionResult> ProcessAsync(DbContextOptions<WaterfallDbContext> options, List<RawVacancy> rawItems)
    {
        await using WaterfallDbContext db = new(options);
        VacancyIngestionService service = new(new EfVacancyRepository(db), NullLogger<VacancyIngestionService>.Instance);
        return await service.ProcessRawVacanciesAsync(rawItems);
    }

    private static List<VacancyDB> LoadVacancies(DbContextOptions<WaterfallDbContext> options)
    {
        using WaterfallDbContext db = new(options);
        return db.Vacancies.AsNoTracking().ToList();
    }

    private static RawVacancy Raw() => new()
    {
        Source = JobSource.Djinni,
        Id = "1",
        Url = "https://djinni.co/jobs/1-dev/",
        Title = "Middle .NET Developer",
        Company = "Kvitka Software",
        Salary = "$3500-4500",
        Location = "Kyiv",
        Posted = "2 дні тому",
        DescriptionHtml = "<p>Опис вакансії</p>",
        Tags = "C#"
    };

    internal static string SampleDumpPath()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "data", "raw", "djinni-sample.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Не знайдено data/raw/djinni-sample.json.");
    }
}
