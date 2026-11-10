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
            NullLogger<SiteSearchService>.Instance);
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

    [Theory]
    [InlineData(JobSource.WorkUa, null, "Djinni і Dou")]
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
