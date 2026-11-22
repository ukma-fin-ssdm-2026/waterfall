// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Net;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Time.Testing;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.SiteSearch;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.Services;
using Waterfall.Client.ViewModels;

namespace Waterfall.Client.Tests;

public sealed class CatalogMultiSiteSearchTests : IDisposable, IAsyncLifetime
{
    private readonly string _tempDbPath = Path.Combine(Path.GetTempPath(), $"waterfall-multisite-{Guid.NewGuid():N}.db");
    private readonly FakeGateway _gateway = new();
    private readonly FakeTimeProvider _time = new();
    private readonly StrongReferenceMessenger _messenger = new();
    private readonly EfLocalVacancyStore _localStore;

    public CatalogMultiSiteSearchTests()
    {
        _localStore = new EfLocalVacancyStore(_tempDbPath);
    }

    /// <summary>
    /// Перший доступ до локальної SQLite створює файл і модель EF Core; на повільному диску
    /// це може займати кілька секунд. Прогріваємо сховище до тесту, щоб таймаути
    /// всередині тестів не залежали від швидкості ініціалізації БД.
    /// </summary>
    public Task InitializeAsync() => _localStore.GetStatsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_tempDbPath)) File.Delete(_tempDbPath);
        }
        catch
        {
        }
    }

    [Fact]
    public void EnablingMultipleSites_PreservesIndependentPerSiteSettingsAndFromPage()
    {
        CatalogViewModel vm = new(_gateway, _messenger, _time);

        // Вмикаємо DOU, Djinni і Work.ua, а Robota.ua лишаємо вимкненим
        vm.IsDouParseEnabled = true;
        vm.IsDjinniParseEnabled = true;
        vm.IsWorkUaParseEnabled = true;
        vm.IsRobotaUaParseEnabled = false;

        // Налаштовуємо DOU
        vm.SelectedPlatform = "DOU";
        vm.DouSelectedCategory = "Python";
        vm.DouSearchQuery = "FastAPI";

        // Налаштовуємо Work.ua (власні сторінки, стартова сторінка і ключові слова)
        vm.SelectedPlatform = "WorkUa";
        vm.WorkUaPagesToParse = "2";
        vm.SiteFromPage = "4";
        vm.WorkUaSearchQuery = "QA";
        vm.WorkUaOpenJobPages = false;

        // Налаштовуємо Djinni (власні сторінки, стартова сторінка і категорія)
        vm.SelectedPlatform = "Djinni";
        vm.DjinniPagesToParse = "3";
        vm.SiteFromPage = "2";
        vm.DjinniSelectedCategory = "C# / .NET";
        vm.DjinniAllKeywords = "Blazor";

        // Перемикання платформ не скидає галочки вибору сайтів
        Assert.Equal(3, vm.SelectedParseSitesCount);
        Assert.Equal("Спарсити (3)", vm.FetchButtonText);
        Assert.Contains("DOU", vm.SelectedParseSitesSummary);
        Assert.Contains("Work.ua", vm.SelectedParseSitesSummary);
        Assert.Contains("Djinni", vm.SelectedParseSitesSummary);
        Assert.DoesNotContain("Robota.ua", vm.SelectedParseSitesSummary);

        List<SiteSearchRequest> requests = vm.BuildSiteSearchRequests();
        Assert.Equal(3, requests.Count);

        SiteSearchRequest dou = requests.Single(r => r.Source == JobSource.Dou);
        Assert.Equal(["Python"], dou.Categories!);
        Assert.Equal("FastAPI", dou.Keywords);

        SiteSearchRequest work = requests.Single(r => r.Source == JobSource.WorkUa);
        Assert.Equal((2, 4, "QA", true), (work.Pages, work.FromPage, work.Keywords, work.ListOnly));

        SiteSearchRequest djinni = requests.Single(r => r.Source == JobSource.Djinni);
        Assert.Equal((3, 2, "Blazor"), (djinni.Pages, djinni.FromPage, djinni.Keywords));
        Assert.Equal([".NET"], djinni.Categories!);
    }

    [Fact]
    public async Task MultiSiteFetch_CallsBatchEndpoint_ShowsPerSiteProgress_MergesAndPersistsLocally()
    {
        CatalogViewModel vm = new(_gateway, _messenger, _time, localVacancyStore: _localStore);
        vm.SelectAllParseSitesCommand.Execute(null);
        vm.IsRobotaUaParseEnabled = false; // 3 сайти

        Guid batchId = Guid.NewGuid();
        VacancyDto douJob = new() { Id = Guid.NewGuid(), ExternalId = "d1", Title = "DOU Python", Company = "A", Source = JobSource.Dou, SourceUrl = "https://jobs.dou.ua/1" };
        VacancyDto djinniJob = new() { Id = Guid.NewGuid(), ExternalId = "j1", Title = "Djinni .NET", Company = "B", Source = JobSource.Djinni, SourceUrl = "https://djinni.co/1" };
        VacancyDto workJob = new() { Id = Guid.NewGuid(), ExternalId = "w1", Title = "Work QA", Company = "C", Source = JobSource.WorkUa, SourceUrl = "https://www.work.ua/jobs/1/" };

        _gateway.MultiSiteSearchStatuses.Enqueue(new MultiSiteSearchStatusDto
        {
            Id = batchId,
            State = SiteSearchStatusDto.Running,
            PagesDone = 1,
            PagesTotal = 5,
            Found = 1,
            Sites =
            [
                new SiteSearchItemStatusDto { Source = JobSource.Dou, State = SiteSearchStatusDto.Completed, PagesDone = 1, PagesTotal = 1, Found = 1 },
                new SiteSearchItemStatusDto { Source = JobSource.WorkUa, State = SiteSearchStatusDto.Running, PagesDone = 0, PagesTotal = 3, Found = 0 },
                new SiteSearchItemStatusDto { Source = JobSource.Djinni, State = SiteSearchStatusDto.Queued, PagesDone = 0, PagesTotal = 1, Found = 0 }
            ]
        });
        _gateway.MultiSiteSearchStatuses.Enqueue(new MultiSiteSearchStatusDto
        {
            Id = batchId,
            State = SiteSearchStatusDto.Completed,
            PagesDone = 5,
            PagesTotal = 5,
            Found = 3,
            Sites =
            [
                new SiteSearchItemStatusDto { Source = JobSource.Dou, State = SiteSearchStatusDto.Completed, Found = 1 },
                new SiteSearchItemStatusDto { Source = JobSource.WorkUa, State = SiteSearchStatusDto.Completed, Found = 1 },
                new SiteSearchItemStatusDto { Source = JobSource.Djinni, State = SiteSearchStatusDto.Completed, Found = 1 }
            ],
            Vacancies = [douJob, djinniJob, workJob]
        });

        Task fetch = vm.FetchFromSitesCommand.ExecuteAsync(null);
        Assert.True(vm.IsSiteSearchRunning);
        Assert.Contains("DOU: ✓ 1", vm.SiteSearchStatusText, StringComparison.Ordinal);

        for (int i = 0; i < 50 && !fetch.IsCompleted; i++)
        {
            _time.Advance(CatalogViewModel.SiteSearchPollInterval);
            await Task.Delay(1);
        }
        await fetch.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(_gateway.MultiSiteSearchRequests);
        Assert.Equal(3, _gateway.MultiSiteSearchRequests[0].Requests.Count);
        Assert.Empty(_gateway.SiteSearchRequests);
        Assert.Equal(3, vm.Vacancies.Count);
        Assert.Contains("Спарсено 3 сайти", vm.StaleCacheText, StringComparison.Ordinal);

        // Перевіряємо збереження в локальній БД клієнта (IsSynced = true)
        IReadOnlyList<VacancyDto> stored = await _localStore.GetAllAsync();
        Assert.Equal(3, stored.Count);
        Assert.Empty(await _localStore.GetUnsyncedAsync());
    }

    [Fact]
    public async Task ClearParseSites_PreventsServerCallAndShowsHint()
    {
        CatalogViewModel vm = new(_gateway, _messenger, _time);
        vm.ClearParseSitesCommand.Execute(null);

        Assert.Equal(0, vm.SelectedParseSitesCount);
        await vm.FetchFromSitesCommand.ExecuteAsync(null);

        Assert.Empty(_gateway.SiteSearchRequests);
        Assert.Empty(_gateway.MultiSiteSearchRequests);
        Assert.Contains("Оберіть сайт", vm.StaleCacheText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerOffline_FallsBackToOfflineCollector_AndMarksVacanciesUnsynced()
    {
        StubOfflineCollector offline = new(
        [
            new VacancyDto
            {
                Id = Guid.NewGuid(),
                ExternalId = "offline-dou-1",
                Title = "Offline DOU Dev",
                Company = "AutonomousCo",
                Description = "Collected without API server",
                Category = "Python",
                Location = "Київ",
                Source = JobSource.Dou,
                SourceUrl = "https://jobs.dou.ua/vacancies/offline-1/"
            }
        ]);
        CatalogViewModel vm = new(_gateway, _messenger, _time, localVacancyStore: _localStore, offlineCollector: offline);
        _gateway.SiteSearchError = new ApiException(null, null, "Не вдалося з'єднатися з сервером.");

        await vm.FetchFromSitesCommand.ExecuteAsync(null);

        Assert.Single(vm.Vacancies);
        Assert.Equal("Offline DOU Dev", vm.Vacancies[0].Title);
        Assert.Contains("офлайн", vm.StaleCacheText, StringComparison.OrdinalIgnoreCase);

        var unsynced = await _localStore.GetUnsyncedAsync();
        Assert.Single(unsynced);
        Assert.Equal("Offline DOU Dev", unsynced[0].Title);
    }

    [Fact]
    public async Task RobotaUaSearchQuery_IsSentAsKeywordsWhenSharedEmpty()
    {
        CatalogViewModel vm = new(_gateway, _messenger, _time);
        vm.SelectedPlatform = "RobotaUa";
        vm.RobotaUaSearchQuery = "Бухгалтер";
        _gateway.SiteSearchStatuses.Enqueue(new SiteSearchStatusDto { State = SiteSearchStatusDto.Completed, Vacancies = [] });

        await vm.FetchFromSitesCommand.ExecuteAsync(null);

        SiteSearchRequest sent = Assert.Single(_gateway.SiteSearchRequests);
        Assert.Equal((JobSource.RobotaUa, "Бухгалтер"), (sent.Source, sent.Keywords));
    }

    private sealed class StubOfflineCollector(IReadOnlyList<VacancyDto> items) : IOfflineSiteCollector
    {
        public bool Supports(JobSource source) => source is JobSource.Dou or JobSource.Djinni;
        public Task<IReadOnlyList<VacancyDto>> CollectAsync(IReadOnlyList<SiteSearchRequest> requests, CancellationToken ct = default) =>
            Task.FromResult(items);
    }
}
