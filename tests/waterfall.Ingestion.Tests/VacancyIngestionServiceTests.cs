// AI: тести циклу фонового збору вакансій (issue #62); перевірено: запуск dotnet test, самоперевірка автора (§8)

using Microsoft.Extensions.Logging.Abstractions;
using Waterfall.Repository.Entities;
using Waterfall.Services.Ingestion;

namespace Waterfall.Ingestion.Tests;

public class VacancyIngestionServiceTests
{
    private static readonly DateTimeOffset FirstRun = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeVacancyRepository _repository = new();
    private readonly FakeVacancyCacheService _cache = new();
    private readonly ManualTimeProvider _time = new(FirstRun);

    [Fact]
    public async Task FirstRun_CreatesValidVacancies()
    {
        IngestionReport report = await CreateService(Document("a.json", RawData.Job("1"), RawData.Job("2"))).RunOnceAsync();

        Assert.Equal(2, report.Created);
        Assert.Equal(0, report.Updated);
        Assert.Equal(2, _repository.Items.Count);
        Assert.All(_repository.Items, v => Assert.Equal("Djinni", v.Source));
        Assert.Equal(1, _cache.CatalogInvalidations);
    }

    [Fact]
    public async Task RepeatedRun_UpdatesInsteadOfDuplicating()
    {
        RawVacancyDocument document = Document("a.json", RawData.Job("1"), RawData.Job("2"));
        await CreateService(document).RunOnceAsync();
        VacancyDB before = _repository.Items.Single(v => v.ExternalId == "1");

        _time.Now = FirstRun.AddHours(6);
        IngestionReport report = await CreateService(document).RunOnceAsync();

        Assert.Equal(0, report.Created);
        Assert.Equal(2, report.Updated);
        Assert.Equal(2, _repository.Items.Count);

        VacancyDB after = _repository.Items.Single(v => v.ExternalId == "1");
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.FirstSeenAt, after.FirstSeenAt);
        // «2 дні тому» в повторному прогоні не повинно зсунути дату публікації
        Assert.Equal(before.PublishedAt, after.PublishedAt);
        Assert.Equal(FirstRun.AddHours(6), after.LastSeenAt);
        Assert.Contains(after.Id, _cache.InvalidatedVacancies);
    }

    [Fact]
    public async Task InvalidRecords_AreSkippedAndValidOnesSaved()
    {
        RawVacancyDocument document = Document("a.json",
            RawData.Job("1"),
            RawData.Job("2", title: ""),
            RawData.Job("3", url: "https://djinni-careers.example.net/jobs/3/"),
            "зламаний запис");

        IngestionReport report = await CreateService(document).RunOnceAsync();

        Assert.Equal(1, report.Created);
        Assert.Equal(3, report.Skipped);
        Assert.Equal(4, report.Items);
        Assert.Equal("1", Assert.Single(_repository.Items).ExternalId);
    }

    [Fact]
    public async Task BrokenDocument_DoesNotStopOtherDocuments()
    {
        IngestionReport report = await CreateService(
            new RawVacancyDocument("broken.json", "{ не json"),
            RawVacancyDocument.Unreadable("locked.json", "файл зайнятий іншим процесом"),
            Document("ok.json", RawData.Job("1"))).RunOnceAsync();

        Assert.Equal(2, report.FailedDocuments);
        Assert.Equal(1, report.Documents);
        Assert.Equal(1, report.Created);
    }

    [Fact]
    public async Task DatabaseFailureForOneVacancy_DoesNotStopOthers()
    {
        _repository.FailOnExternalId = "2";

        IngestionReport report = await CreateService(Document("a.json", RawData.Job("1"), RawData.Job("2"), RawData.Job("3"))).RunOnceAsync();

        Assert.Equal(2, report.Created);
        Assert.Equal(1, report.Failed);
        Assert.DoesNotContain(_repository.Items, v => v.ExternalId == "2");
    }

    [Fact]
    public async Task NothingSaved_KeepsCache()
    {
        IngestionReport report = await CreateService(Document("a.json", RawData.Job("1", title: ""))).RunOnceAsync();

        Assert.Equal(1, report.Skipped);
        Assert.Equal(0, _cache.CatalogInvalidations);
    }

    [Fact]
    public async Task CancelledRun_StopsWithoutSwallowingCancellation()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService(Document("a.json", RawData.Job("1"))).RunOnceAsync(cts.Token));
    }

    private VacancyIngestionService CreateService(params RawVacancyDocument[] documents) => new(
        new InMemoryRawSource(documents),
        new DjinniRawJsonParser(),
        new VacancyNormalizer(),
        _repository,
        _cache,
        _time,
        NullLogger<VacancyIngestionService>.Instance);

    private static RawVacancyDocument Document(string name, params object[] jobs) =>
        new(name, RawData.DjinniDocument(jobs));
}
