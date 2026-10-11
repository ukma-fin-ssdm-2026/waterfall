// AI: перевірка демонстраційного дампу data/raw/djinni-sample.json (issue #62); перевірено: запуск dotnet test, самоперевірка автора (§8)

using Microsoft.Extensions.Logging.Abstractions;
using Waterfall.Services.Ingestion;

namespace Waterfall.Ingestion.Tests;

/// <summary>
/// Гарантує, що демонстраційний дамп у репозиторії лишається валідним: 10 записів імпортуються,
/// 3 навмисно невалідні відкидаються з поясненням.
/// </summary>
public class SampleDumpTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SampleDump_HasTenValidAndThreeRejectedRecords()
    {
        RawParseResult parsed = new DjinniRawJsonParser().Parse(File.ReadAllText(SampleDumpPath()));
        Assert.True(parsed.IsValid, parsed.Error);

        VacancyNormalizer normalizer = new();
        List<NormalizationResult> results = parsed.Items.Select(raw => normalizer.Normalize(raw, Now)).ToList();

        Assert.Equal(10, results.Count(r => r.IsValid));
        Assert.Single(parsed.ItemErrors);
        Assert.Contains(results, r => !r.IsValid && r.Error.Contains("назви посади"));
        Assert.Contains(results, r => !r.IsValid && r.Error.Contains("сторонній домен"));
    }

    [Fact]
    public async Task SampleDump_ImportsEndToEnd()
    {
        FakeVacancyRepository repository = new();
        VacancyIngestionService service = new(
            new InMemoryRawSource(new RawVacancyDocument("djinni-sample.json", File.ReadAllText(SampleDumpPath()))),
            new DjinniRawJsonParser(),
            new VacancyNormalizer(),
            repository,
            new FakeVacancyCacheService(),
            new ManualTimeProvider(Now),
            NullLogger<VacancyIngestionService>.Instance);

        IngestionReport report = await service.RunOnceAsync();

        Assert.Equal(10, report.Created);
        Assert.Equal(3, report.Skipped);
        Assert.Equal(0, report.Failed);

        // Трекінгові параметри прибрано, перевернуту вилку виправлено, скрипт з опису видалено
        Repository.Entities.VacancyDB lead = repository.Items.Single(v => v.ExternalId == "720096");
        Assert.Equal("https://djinni.co/jobs/720096-lead-net-engineer/", lead.SourceUrl);
        Assert.Equal(5000m, lead.SalaryMin);
        Assert.Equal(6000m, lead.SalaryMax);
        Assert.DoesNotContain("alert", lead.Description);
    }

    private static string SampleDumpPath()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "data", "raw", "djinni-sample.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Не знайдено data/raw/djinni-sample.json у батьківських каталогах тестів.");
    }
}
