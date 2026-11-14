// AI: тести пошуку на сайтах на запит (адреси з фільтрами, прогрес, збереження, кеш, ліміти) (issue #62); перевірено: запуск dotnet test, самоперевірка автора (§8)

using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Waterfall.Api.Workers;
using Waterfall.Repository.Data;
using Waterfall.Repository.Interfaces;
using Waterfall.Repository.Repositories;
using Waterfall.Services;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.SiteSearch;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Ingestion.Tests;

/// <summary>
/// Пошук на сайтах на запит: справжні нормалізація й SQLite у пам'яті, підроблені сайти.
/// Фікстура: нова база й новий сервіс на кожен тест.
/// </summary>
public sealed class SiteSearchServiceTests : IDisposable
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);
    private static readonly Guid User = Guid.NewGuid();

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly SitesStub _sites = new();
    private readonly BrowserStub _browser = new();
    private readonly ServiceProvider _services;
    private readonly SiteSearchService _search;

    public SiteSearchServiceTests()
    {
        _connection.Open();
        _services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<WaterfallDbContext>(options => options.UseSqlite(_connection))
            .AddScoped<IVacancyRepository, EfVacancyRepository>()
            .AddScoped<ISavedVacancyRepository, EfSavedVacancyRepository>()
            .AddScoped<IVacancyIngestionService, VacancyIngestionService>()
            .BuildServiceProvider();
        using (IServiceScope scope = _services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<WaterfallDbContext>().Database.EnsureCreated();
        }

        _search = new SiteSearchService(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _sites,
            Options.Create(new IngestionOptions { RequestDelay = TimeSpan.Zero }),
            new Lifetime(),
            NullLogger<SiteSearchService>.Instance,
            _browser);
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void DjinniUrl_CarriesAllFilters_AndPage()
    {
        SiteSearchRequest request = new()
        {
            Source = JobSource.Djinni,
            Categories = ["Python", ".NET"],
            Experience = "2y",
            City = "kyiv",
            Remote = true,
            SalaryFrom = 2000,
            Keywords = "Django REST",
            Pages = 3
        };

        Assert.Equal(
            "https://djinni.co/jobs/?primary_keyword=Python&primary_keyword=.NET&exp_level=2y&location=kyiv&employment=remote&salary=2000&all_keywords=Django%20REST",
            SiteSearchService.ListPageUrl(request, 1));
        Assert.EndsWith("&page=2", SiteSearchService.ListPageUrl(request, 2), StringComparison.Ordinal);
    }

    [Fact]
    public void DouUrl_IsTheRssFeedWithFilters_AndHasNoSecondPage()
    {
        SiteSearchRequest request = new() { Source = JobSource.Dou, Categories = ["Python"], Experience = "1-3", City = "Київ", Remote = true, Keywords = "Django" };

        Assert.Equal(
            "https://jobs.dou.ua/vacancies/feeds/?category=Python&exp=1-3&city=%D0%9A%D0%B8%D1%97%D0%B2&remote=1&search=Django",
            SiteSearchService.ListPageUrl(request, 1));
        Assert.Null(SiteSearchService.ListPageUrl(request, 2));
    }

    [Fact]
    public void EmptyFilters_GiveThePlainList()
    {
        Assert.Equal("https://djinni.co/jobs/", SiteSearchService.ListPageUrl(new SiteSearchRequest { Source = JobSource.Djinni }, 1));
        Assert.Equal("https://jobs.dou.ua/vacancies/feeds/", SiteSearchService.ListPageUrl(new SiteSearchRequest { Source = JobSource.Dou }, 1));
    }

    [Fact]
    public void WorkUaUrl_PutsCityCategoryAndQueryInPath_AndOtherFiltersInQuery()
    {
        SiteSearchRequest request = new()
        {
            Source = JobSource.WorkUa,
            City = "kyiv",
            Categories = ["it"],
            Keywords = "Python  Developer",
            Options = new Dictionary<string, string> { ["student"] = "1", ["employment"] = "74" },
            Pages = 2
        };

        Assert.Equal("https://www.work.ua/jobs-kyiv-it-python+developer/?employment=74&student=1", SiteSearchService.ListPageUrl(request, 1));
        Assert.Equal("https://www.work.ua/jobs-kyiv-it-python+developer/?employment=74&student=1&page=2", SiteSearchService.ListPageUrl(request, 2));
    }

    [Fact]
    public void WorkUaUrl_RemoteIsACity_AndNoFiltersSearchesAllUkraine()
    {
        Assert.Equal("https://www.work.ua/jobs-remote-c%23/", SiteSearchService.ListPageUrl(new SiteSearchRequest { Source = JobSource.WorkUa, Remote = true, Keywords = "C#" }, 1));
        Assert.Equal("https://www.work.ua/jobs/?ss=1&page=3", SiteSearchService.ListPageUrl(new SiteSearchRequest { Source = JobSource.WorkUa }, 3));
    }

    [Fact]
    public void RobotaUaUrl_PutsQueryAndCityInPath_AndFiltersAfterParams()
    {
        SiteSearchRequest request = new()
        {
            Source = JobSource.RobotaUa,
            City = "kyiv",
            Keywords = "Python developer",
            Remote = true,
            Options = new Dictionary<string, string> { ["salary"] = "40000", ["experienceType"] = "true" }
        };

        Assert.Equal("https://robota.ua/zapros/python-developer/kyiv/params;experienceType=true;salary=40000;scheduleIds=3", SiteSearchService.ListPageUrl(request, 1));
        Assert.Equal("https://robota.ua/zapros/ukraine", SiteSearchService.ListPageUrl(new SiteSearchRequest { Source = JobSource.RobotaUa }, 1));
        Assert.Equal("https://robota.ua/zapros/ukraine/params;page=2", SiteSearchService.ListPageUrl(new SiteSearchRequest { Source = JobSource.RobotaUa }, 2));
    }

    [Fact]
    public async Task WorkUaSearch_ReadsEachJobPageInBrowser_WithoutRobotsTxt()
    {
        SiteSearchRequest request = new() { Source = JobSource.WorkUa, City = "kyiv", Keywords = "python" };
        _browser.Pages[SiteSearchService.ListPageUrl(request, 1)!] = WorkUaPageTests.ListPage(WorkUaPageTests.Card(101, "Python Developer"), WorkUaPageTests.Card(102, "Data Engineer"));
        _browser.Pages["https://www.work.ua/jobs/101/"] = WorkUaPageTests.JobPage(["Python", "Django"]);
        _browser.Pages["https://www.work.ua/jobs/102/"] = WorkUaPageTests.JobPage(["SQL"]);

        SiteSearchStatusDto done = await WaitDoneAsync(_search.Start(request, User).Status!.Id);

        Assert.Equal(["Python Developer", "Data Engineer"], done.Vacancies!.Select(v => v.Title));
        VacancyDto python = done.Vacancies!.Single(v => v.Title == "Python Developer");
        // Повний опис і навички зі сторінки вакансії, а не «Короткий опис» з картки списку
        Assert.Contains("Досвід ручного тестування", python.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("Короткий опис", python.Description, StringComparison.Ordinal);
        Assert.Contains("Django", python.Skills);
        Assert.Equal(
            [SiteSearchService.ListPageUrl(request, 1)!, "https://www.work.ua/jobs/101/", "https://www.work.ua/jobs/102/"],
            _browser.Urls); // robots.txt не читаємо
        Assert.Empty(_sites.Requested); // звичайним HTTP на Work.ua не ходимо
    }

    [Fact]
    public async Task WorkUaListOnly_ReadsOnlyListPages_AndIsCachedApartFromFullSearch()
    {
        SiteSearchRequest quick = new() { Source = JobSource.WorkUa, Keywords = "python", ListOnly = true };
        _browser.Pages[SiteSearchService.ListPageUrl(quick, 1)!] = WorkUaPageTests.ListPage(WorkUaPageTests.Card(101, "Python Developer"));
        _browser.Pages["https://www.work.ua/jobs/101/"] = WorkUaPageTests.JobPage(["Python"]);

        SiteSearchStatusDto done = await WaitDoneAsync(_search.Start(quick, User).Status!.Id);

        Assert.Contains("Короткий опис", Assert.Single(done.Vacancies!).Description, StringComparison.Ordinal);
        Assert.Equal([SiteSearchService.ListPageUrl(quick, 1)!], _browser.Urls); // сторінку вакансії не відкривали

        // Той самий пошук з повними даними не береться зі швидкого кешу
        SiteSearchStatusDto full = _search.Start(new SiteSearchRequest { Source = JobSource.WorkUa, Keywords = "python" }, User).Status!;
        Assert.False(full.FromCache);
        await WaitDoneAsync(full.Id);
        Assert.Contains("https://www.work.ua/jobs/101/", _browser.Urls);
    }

    [Fact]
    public async Task WorkUaListOnly_AfterFullSearch_DoesNotWipeStoredDetails()
    {
        SiteSearchRequest full = new() { Source = JobSource.WorkUa, Keywords = "python" };
        SiteSearchRequest quick = new() { Source = JobSource.WorkUa, Keywords = "python", ListOnly = true };
        _browser.Pages[SiteSearchService.ListPageUrl(full, 1)!] = WorkUaPageTests.ListPage(WorkUaPageTests.Card(101, "Python Developer"));
        _browser.Pages["https://www.work.ua/jobs/101/"] = WorkUaPageTests.JobPage(["Python", "Django"]);
        await WaitDoneAsync(_search.Start(full, User).Status!.Id);

        // Швидкий пошук бачить ту саму вакансію лише в картці списку
        _browser.Pages[SiteSearchService.ListPageUrl(quick, 1)!] = WorkUaPageTests.ListPage(WorkUaPageTests.Card(101, "Senior Python Developer"));
        SiteSearchStatusDto done = await WaitDoneAsync(_search.Start(quick, User).Status!.Id);

        VacancyDto vacancy = Assert.Single(done.Vacancies!);
        Assert.Equal("Senior Python Developer", vacancy.Title); // те, що список дає повністю, оновлюється
        Assert.Contains("Досвід ручного тестування", vacancy.Description, StringComparison.Ordinal);
        Assert.Contains("Django", vacancy.Skills);
        Assert.Contains("Повна зайнятість", vacancy.Schedule, StringComparison.Ordinal);
    }

    [Fact]
    public void ListOnly_IsRejectedForRobotaUa()
    {
        SiteSearchRequest request = new() { Source = JobSource.RobotaUa, ListOnly = true };

        List<ValidationResult> results = [];
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

        Assert.Contains(results, r => r.ErrorMessage!.Contains("listOnly", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RobotaUaSearch_ReadsEachJobPageInBrowser_BecauseTheListHasNoDescription()
    {
        SiteSearchRequest request = new() { Source = JobSource.RobotaUa, Keywords = "qa" };
        _browser.Pages[SiteSearchService.ListPageUrl(request, 1)!] = """<main><a href="/company1/vacancy42"><h2>QA Engineer</h2></a></main>""";
        _browser.Pages["https://robota.ua/company1/vacancy42"] = """<main><h1>QA Engineer</h1><div itemprop="description">Тестування продукту</div></main>""";

        SiteSearchStatusDto done = await WaitDoneAsync(_search.Start(request, User).Status!.Id);

        VacancyDto vacancy = Assert.Single(done.Vacancies!);
        Assert.Equal(("QA Engineer", JobSource.RobotaUa), (vacancy.Title, vacancy.Source));
        Assert.Contains("Тестування продукту", vacancy.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(_browser.Urls, url => url.EndsWith("/robots.txt", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(JobSource.WorkUa, "Київ", null, "коди з адреси")]
    [InlineData(JobSource.RobotaUa, null, "a&b", "options")]
    public void WorkUaAndRobotaUa_AcceptOnlySiteCodes(JobSource source, string? city, string? optionValue, string error)
    {
        SiteSearchRequest request = new()
        {
            Source = source,
            City = city,
            Options = optionValue is null ? null : new Dictionary<string, string> { ["salary"] = optionValue }
        };

        List<ValidationResult> results = [];
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

        Assert.Contains(results, r => r.ErrorMessage!.Contains(error, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(JobSource.Unknown, null, "Djinni, Dou, WorkUa і RobotaUa")]
    [InlineData(JobSource.Djinni, "2 роки", "experience")]
    [InlineData(JobSource.Djinni, "2y&salary=9", "experience")]
    public void Request_RejectsUnsupportedSiteAndForeignExperienceCodes(JobSource source, string? experience, string error)
    {
        SiteSearchRequest request = new() { Source = source, Experience = experience };

        List<ValidationResult> results = [];
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

        Assert.Contains(results, r => r.ErrorMessage!.Contains(error, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DjinniSearch_ReadsOnlyListPages_SavesVacancies_AndReturnsThemNewestFirst()
    {
        SiteSearchRequest request = new() { Source = JobSource.Djinni, Categories = ["Python"], Pages = 3 };
        _sites.Pages[SiteSearchService.ListPageUrl(request, 1)!] = DjinniPageTests.Page(DjinniPageTests.Job(2), DjinniPageTests.Job(1));
        _sites.Pages[SiteSearchService.ListPageUrl(request, 2)!] = DjinniPageTests.Page(DjinniPageTests.Job(3));
        // За межами списку Djinni знову показує першу сторінку — обхід на цьому зупиняється
        _sites.Pages[SiteSearchService.ListPageUrl(request, 3)!] = DjinniPageTests.Page(DjinniPageTests.Job(2), DjinniPageTests.Job(1));

        SiteSearchStart start = _search.Start(request, User);
        SiteSearchStatusDto done = await WaitDoneAsync(start.Status!.Id);

        Assert.Equal(SiteSearchStates.Completed, done.State);
        Assert.False(done.FromCache);
        Assert.Equal(3, done.Found);
        Assert.Equal(["Вакансія 2", "Вакансія 1", "Вакансія 3"], done.Vacancies!.Select(v => v.Title));
        Assert.All(done.Vacancies!, v => Assert.Equal(JobSource.Djinni, v.Source));
        Assert.DoesNotContain(_sites.Requested, url => url.Contains("-job/", StringComparison.Ordinal)); // сторінки окремих вакансій не читаємо
    }

    [Fact]
    public async Task SameSearchWithinThreeHours_ComesFromCache_WithoutRequests()
    {
        SiteSearchRequest request = new() { Source = JobSource.Dou, Categories = ["QA"] };
        _sites.Pages[SiteSearchService.ListPageUrl(request, 1)!] = DouPageTests.Feed(DouPageTests.Item(10), DouPageTests.Item(11));
        await WaitDoneAsync(_search.Start(request, User).Status!.Id);
        int requests = _sites.Requested.Count;

        SiteSearchStart again = _search.Start(new SiteSearchRequest { Source = JobSource.Dou, Categories = [" QA "] }, Guid.NewGuid());

        Assert.True(again.Status!.FromCache);
        Assert.Equal(SiteSearchStates.Completed, again.Status.State);
        Assert.Equal(requests, _sites.Requested.Count);
    }

    [Fact]
    public async Task DouSearchByCategory_SavesThatCategory_BecauseTheFeedHasNone()
    {
        // RSS DOU не містить категорії, а стрічка з category=C++ — це лише вакансії розділу C++
        SiteSearchRequest request = new() { Source = JobSource.Dou, Categories = ["C++"] };
        _sites.Pages[SiteSearchService.ListPageUrl(request, 1)!] = DouPageTests.Feed(DouPageTests.Item(20), DouPageTests.Item(21));

        SiteSearchStatusDto done = await WaitDoneAsync(_search.Start(request, User).Status!.Id);

        Assert.Equal(2, done.Vacancies!.Count);
        Assert.All(done.Vacancies!, v => Assert.Equal(("C++", "Software Development"), (v.Category, v.CategoryGroup)));
    }

    [Fact]
    public void RequestedCategory_DoesNotReplaceTheSitesOwn()
    {
        SiteSearchRequest request = new() { Source = JobSource.Djinni, Categories = ["C++"] };
        List<RawVacancy> raw = [new() { Id = "1", Category = "Embedded" }, new() { Id = "2" }];

        Assert.Equal(["Embedded", "C++"], SiteSearchService.WithRequestedCategory(raw, request).Select(v => v.Category));
    }

    [Fact]
    public void SeveralRequestedCategories_AreNotGuessed()
    {
        SiteSearchRequest request = new() { Source = JobSource.Djinni, Categories = ["MAUI", "Xamarin"] };
        List<RawVacancy> raw = [new() { Id = "1" }];

        Assert.Null(SiteSearchService.WithRequestedCategory(raw, request).Single().Category);
    }

    [Fact]
    public async Task WhileSearchRuns_ProgressIsVisible_AndUserCannotStartAnother()
    {
        SiteSearchRequest request = new() { Source = JobSource.Djinni, Pages = 2 };
        string first = SiteSearchService.ListPageUrl(request, 1)!;
        string second = SiteSearchService.ListPageUrl(request, 2)!;
        _sites.Pages[first] = DjinniPageTests.Page(DjinniPageTests.Job(1));
        _sites.Pages[second] = DjinniPageTests.Page(DjinniPageTests.Job(2));
        _sites.Hold(second);

        Guid id = _search.Start(request, User).Status!.Id;
        SiteSearchStatusDto running = await WaitAsync(id, status => status.PagesDone == 1);
        SiteSearchStart another = _search.Start(new SiteSearchRequest { Source = JobSource.Dou }, User);
        _sites.Release(second);
        SiteSearchStatusDto done = await WaitDoneAsync(id);

        Assert.Equal((SiteSearchStates.Running, 1, 2, 1), (running.State, running.PagesDone, running.PagesTotal, running.Found));
        Assert.Null(another.Status);
        Assert.Contains("ще триває", another.Rejection, StringComparison.Ordinal);
        Assert.Equal(2, done.Found);
    }

    [Fact]
    public async Task SiteThatAnswersNothing_CompletesWithExplanation()
    {
        SiteSearchRequest request = new() { Source = JobSource.Djinni, Categories = ["Rust"] };
        _sites.Pages[SiteSearchService.ListPageUrl(request, 1)!] = DjinniPageTests.Page();

        SiteSearchStatusDto done = await WaitDoneAsync(_search.Start(request, User).Status!.Id);

        Assert.Equal(SiteSearchStates.Completed, done.State);
        Assert.Empty(done.Vacancies!);
        Assert.Contains("нічого не знайдено", done.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnotherUser_CannotReadSomeoneElsesSearch()
    {
        SiteSearchRequest request = new() { Source = JobSource.Dou };
        _sites.Pages[SiteSearchService.ListPageUrl(request, 1)!] = DouPageTests.Feed(DouPageTests.Item(5));
        Guid id = _search.Start(request, User).Status!.Id;
        await WaitDoneAsync(id);

        Assert.Null(await _search.GetAsync(id, Guid.NewGuid()));
    }

    private Task<SiteSearchStatusDto> WaitDoneAsync(Guid id) =>
        WaitAsync(id, status => status.State is SiteSearchStates.Completed or SiteSearchStates.Failed);

    private async Task<SiteSearchStatusDto> WaitAsync(Guid id, Func<SiteSearchStatusDto, bool> condition)
    {
        using CancellationTokenSource limit = new(WaitLimit);
        while (true)
        {
            SiteSearchStatusDto status = (await _search.GetAsync(id, User, limit.Token))!;
            if (condition(status))
            {
                return status;
            }

            await Task.Delay(10, limit.Token);
        }
    }

    /// <summary>Підробка сайтів: віддає підготовлені сторінки, robots.txt дозволяє все, решта — 404; може притримати відповідь.</summary>
    private sealed class SitesStub : HttpMessageHandler, IHttpClientFactory
    {
        private readonly Dictionary<string, TaskCompletionSource> _held = [];
        private readonly List<string> _requested = [];

        public Dictionary<string, string> Pages { get; } = [];

        public IReadOnlyList<string> Requested
        {
            get
            {
                lock (_requested)
                {
                    return _requested.ToList();
                }
            }
        }

        public void Hold(string url) => _held[url] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release(string url) => _held[url].SetResult();

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            lock (_requested)
            {
                _requested.Add(url);
            }

            if (_held.TryGetValue(url, out TaskCompletionSource? gate))
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            if (url.EndsWith("/robots.txt", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
            }

            return Pages.TryGetValue(url, out string? body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    /// <summary>Підробка браузера автора: віддає підготовлену розмітку й запам'ятовує відкриті адреси.</summary>
    private sealed class BrowserStub : IBrowserFetcher
    {
        private readonly List<string> _urls = [];

        public Dictionary<string, string> Pages { get; } = [];

        public IReadOnlyList<string> Urls
        {
            get
            {
                lock (_urls)
                {
                    return _urls.ToList();
                }
            }
        }

        public Task<string> GetPageAsync(string url, CancellationToken ct = default)
        {
            lock (_urls)
            {
                _urls.Add(url);
            }

            return Task.FromResult(Pages.TryGetValue(url, out string? html) ? html : string.Empty);
        }
    }

    private sealed class Lifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
