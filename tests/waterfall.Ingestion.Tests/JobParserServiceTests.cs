// AI: тести фонового парсера: djinni.co, work.ua і дампи (issue #62, Lab 06); перевірено: запуск dotnet test, самоперевірка автора (§8)

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Waterfall.Api.Workers;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Ingestion.Tests;

public sealed class JobParserServiceTests : IDisposable
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);
    private const string ValidDump = "{ \"source\": \"djinni\", \"jobs\": [ { \"id\": \"1\" } ] }";

    // Фікстура: окремий тимчасовий каталог з дампами на кожен тест
    private readonly string _directory = Directory.CreateTempSubdirectory("waterfall-dumps-").FullName;
    private readonly RecordingIngestionService _ingestion = new();
    private readonly RecordingLogger _logger = new();
    private readonly FakeWeb _web = new();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void ParseDump_ReadsJobs()
    {
        List<RawVacancy> jobs = JobParserService.ParseDump(File.ReadAllText(VacancyIngestionServiceTests.SampleDumpPath()));

        Assert.Equal(12, jobs.Count);
        Assert.Equal("720001", jobs[0].Id);
        Assert.Equal("$3500-4500", jobs[0].Salary);
        Assert.All(jobs, job => Assert.Equal(JobSource.Djinni, job.Source));
    }

    [Fact]
    public void ParseDump_WithoutJobs_ReturnsEmptyList()
    {
        Assert.Empty(JobParserService.ParseDump("{ \"source\": \"Djinni\" }"));
    }

    [Theory]
    [InlineData("{ не json")]
    [InlineData("null")]
    [InlineData("{ \"source\": \"workua\", \"jobs\": [] }")]
    public void ParseDump_RejectsBrokenEmptyOrForeignFiles(string json)
    {
        Assert.ThrowsAny<JsonException>(() => JobParserService.ParseDump(json));
    }

    [Fact]
    public async Task BrokenFile_DoesNotStopOtherFiles()
    {
        // Файли обробляються за алфавітом, тож зламаний іде першим
        File.WriteAllText(Path.Combine(_directory, "a-broken.json"), "{ не json");
        File.WriteAllText(Path.Combine(_directory, "b-ok.json"), ValidDump);
        JobParserService parser = CreateParser(_directory);

        await parser.StartAsync(CancellationToken.None);
        await _ingestion.WaitForCallsAsync(1);
        await parser.StopAsync(CancellationToken.None);

        Assert.Equal("1", Assert.Single(_ingestion.Received).Id);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("a-broken.json"));
    }

    [Fact]
    public async Task Parser_RereadsDirectoryOnSchedule()
    {
        File.WriteAllText(Path.Combine(_directory, "dump.json"), ValidDump);
        JobParserService parser = CreateParser(_directory, interval: TimeSpan.FromMilliseconds(50));

        await parser.StartAsync(CancellationToken.None);
        await _ingestion.WaitForCallsAsync(2);
        await parser.StopAsync(CancellationToken.None);

        // Зупинка штатна: завдання завершилося без винятку
        Assert.True(parser.ExecuteTask?.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DisabledParser_NeverReadsFiles()
    {
        File.WriteAllText(Path.Combine(_directory, "dump.json"), ValidDump);
        JobParserService parser = CreateParser(_directory, enabled: false);

        await parser.StartAsync(CancellationToken.None);
        await parser.ExecuteTask!.WaitAsync(WaitLimit);
        await parser.StopAsync(CancellationToken.None);

        Assert.Empty(_ingestion.Received);
        Assert.Empty(_web.Urls);
        Assert.Contains(_logger.Entries, e => e.Message.Contains("вимкнено"));
    }

    [Fact]
    public async Task MissingDirectory_IsLoggedAndSkipped()
    {
        JobParserService parser = CreateParser(Path.Combine(_directory, "немає-такої-папки"));

        await parser.StartAsync(CancellationToken.None);
        await _logger.WaitForWarningAsync();
        await parser.StopAsync(CancellationToken.None);

        Assert.Empty(_ingestion.Received);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("не знайдено"));
    }

    [Fact]
    public async Task Djinni_ReadsPagesUntilSiteRepeatsFirstPage()
    {
        _web.FirstPage = DjinniPageTests.Page(DjinniPageTests.Job(1), DjinniPageTests.Job(2));
        _web.Responses["https://djinni.co/jobs/?page=2"] = (HttpStatusCode.OK, DjinniPageTests.Page(DjinniPageTests.Job(2), DjinniPageTests.Job(3)));
        JobParserService parser = CreateParser(directory: null, djinniPages: 5);

        await parser.StartAsync(CancellationToken.None);
        await _ingestion.WaitForCallsAsync(1);
        await parser.StopAsync(CancellationToken.None);

        // Вакансія 2 з'їхала на другу сторінку, поки ми читали першу, — але передана й відкрита один раз
        Assert.Equal(["1", "2", "3"], _ingestion.Received.Select(job => job.Id));
        // Третя сторінка повторила першу (так Djinni відповідає за межами списку), тож далі не йдемо
        Assert.Equal(
            [
                "https://djinni.co/robots.txt",
                "https://djinni.co/jobs/", "https://djinni.co/jobs/1-job/", "https://djinni.co/jobs/2-job/",
                "https://djinni.co/jobs/?page=2", "https://djinni.co/jobs/3-job/",
                "https://djinni.co/jobs/?page=3"
            ],
            _web.Urls);
        Assert.StartsWith("WaterfallJobParser/", _web.UserAgent);
    }

    [Fact]
    public async Task Djinni_ReadsSkillsFromJobPage_OncePerVacancy()
    {
        _web.FirstPage = DjinniPageTests.Page(DjinniPageTests.Job(1), DjinniPageTests.Job(2));
        _web.Responses["https://djinni.co/jobs/1-job/"] = (HttpStatusCode.OK, DjinniPageTests.JobPage("TypeScript, React.js"));
        _web.Responses["https://djinni.co/jobs/2-job/"] = (HttpStatusCode.OK, DjinniPageTests.JobPage(skillsHtml: null));
        JobParserService parser = CreateParser(directory: null, interval: TimeSpan.FromMilliseconds(50), djinniPages: 1);

        await parser.StartAsync(CancellationToken.None);
        await _ingestion.WaitForCallsAsync(2);
        await parser.StopAsync(CancellationToken.None);

        // Другий збір бере навички з кешу, а не відкриває сторінку вакансії знову
        Assert.Equal(["TypeScript, React.js", null, "TypeScript, React.js", null], _ingestion.Received.Take(4).Select(job => job.Tags));
        Assert.Single(_web.Urls, url => url == "https://djinni.co/jobs/1-job/");
    }

    [Fact]
    public async Task Djinni_JobPageError_KeepsVacancyAndSparesSite()
    {
        _web.FirstPage = DjinniPageTests.Page(DjinniPageTests.Job(1), DjinniPageTests.Job(2));
        _web.Responses["https://djinni.co/jobs/1-job/"] = (HttpStatusCode.TooManyRequests, "");
        JobParserService parser = CreateParser(directory: null, djinniPages: 1);

        await parser.StartAsync(CancellationToken.None);
        await _ingestion.WaitForCallsAsync(1);
        await parser.StopAsync(CancellationToken.None);

        // Обидві вакансії збережено (без навичок), а сторінку другої вже не відкривали
        Assert.Equal(["1", "2"], _ingestion.Received.Select(job => job.Id));
        Assert.All(_ingestion.Received, job => Assert.Null(job.Tags));
        Assert.DoesNotContain("https://djinni.co/jobs/2-job/", _web.Urls);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("1-job") && e.Message.Contains("429"));
    }

    [Fact]
    public async Task Djinni_ForeignLinkInPageData_IsNotRequested()
    {
        string foreignJob = """{"@type": "JobPosting", "identifier": 9, "title": "Вакансія", "url": "https://evil.example/jobs/9/", "description": "Опис"}""";
        _web.FirstPage = DjinniPageTests.Page(foreignJob);
        JobParserService parser = CreateParser(directory: null, djinniPages: 1);

        await parser.StartAsync(CancellationToken.None);
        await _ingestion.WaitForCallsAsync(1);
        await parser.StopAsync(CancellationToken.None);

        // Сервіс не ходить за чужим посиланням, а саму вакансію відкине нормалізація (THR-03)
        Assert.DoesNotContain(_web.Urls, url => url.Contains("evil.example"));
        Assert.Equal("9", Assert.Single(_ingestion.Received).Id);
    }

    [Fact]
    public async Task Djinni_ReadsNoMoreThanConfiguredPages()
    {
        _web.FirstPage = DjinniPageTests.Page(DjinniPageTests.Job(1));
        _web.Responses["https://djinni.co/jobs/?page=2"] = (HttpStatusCode.OK, DjinniPageTests.Page(DjinniPageTests.Job(2)));
        _web.Responses["https://djinni.co/jobs/?page=3"] = (HttpStatusCode.OK, DjinniPageTests.Page(DjinniPageTests.Job(3)));
        JobParserService parser = CreateParser(directory: null, djinniPages: 2);

        await parser.StartAsync(CancellationToken.None);
        await _ingestion.WaitForCallsAsync(1);
        await parser.StopAsync(CancellationToken.None);

        Assert.Equal(["1", "2"], _ingestion.Received.Select(job => job.Id));
        Assert.DoesNotContain("https://djinni.co/jobs/?page=3", _web.Urls);
    }

    [Theory]
    [InlineData(false, "503")]
    [InlineData(true, "тайм-аут")] // HttpClient повідомляє про тайм-аут через TaskCanceledException
    public async Task Djinni_SiteError_KeepsEarlierPages(bool timeout, string error)
    {
        _web.FirstPage = DjinniPageTests.Page(DjinniPageTests.Job(1));
        if (timeout)
        {
            _web.TimeoutUrls.Add("https://djinni.co/jobs/?page=2");
        }
        else
        {
            _web.Responses["https://djinni.co/jobs/?page=2"] = (HttpStatusCode.ServiceUnavailable, "");
        }

        JobParserService parser = CreateParser(directory: null, djinniPages: 5);

        await parser.StartAsync(CancellationToken.None);
        await _ingestion.WaitForCallsAsync(1);
        await parser.StopAsync(CancellationToken.None);

        Assert.Equal("1", Assert.Single(_ingestion.Received).Id);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("?page=2") && e.Message.Contains(error));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "User-agent: *\nDisallow: /jobs")]
    [InlineData(HttpStatusCode.InternalServerError, "")] // за RFC 9309 збій robots.txt — заборона
    public async Task Djinni_RobotsTxtForbidsJobs_SiteIsSkipped(HttpStatusCode status, string robotsTxt)
    {
        _web.FirstPage = DjinniPageTests.Page(DjinniPageTests.Job(1));
        _web.Responses["https://djinni.co/robots.txt"] = (status, robotsTxt);
        JobParserService parser = CreateParser(directory: null, djinniPages: 5);

        await parser.StartAsync(CancellationToken.None);
        await _logger.WaitForWarningAsync();
        await parser.StopAsync(CancellationToken.None);

        Assert.Empty(_ingestion.Received);
        Assert.Equal(["https://djinni.co/robots.txt"], _web.Urls);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("robots.txt"));
    }

    [Fact]
    public async Task Djinni_PageWithoutJobs_IsLogged()
    {
        _web.FirstPage = "<html><body>Перевірка браузера…</body></html>";
        JobParserService parser = CreateParser(directory: null, djinniPages: 5);

        await parser.StartAsync(CancellationToken.None);
        await _logger.WaitForWarningAsync();
        await parser.StopAsync(CancellationToken.None);

        Assert.Empty(_ingestion.Received);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("не знайдено вакансій"));
    }

    [Fact]
    public async Task WorkUa_ReadsListAndJobPages()
    {
        _web.Responses["https://www.work.ua/jobs/?ss=1"] = (HttpStatusCode.OK, WorkUaPageTests.ListPage(WorkUaPageTests.Card(11), WorkUaPageTests.Card(12)));
        _web.Responses["https://www.work.ua/jobs/?ss=1&page=2"] = (HttpStatusCode.OK, WorkUaPageTests.ListPage()); // за останньою сторінкою список порожній
        _web.Responses["https://www.work.ua/jobs/11/"] = (HttpStatusCode.OK, WorkUaPageTests.JobPage(skills: ["Jira", "SQL"]));
        _web.Responses["https://www.work.ua/jobs/12/"] = (HttpStatusCode.OK, WorkUaPageTests.JobPage(skills: []));
        JobParserService parser = CreateParser(directory: null, workUaPages: 5);

        await parser.StartAsync(CancellationToken.None);
        await _ingestion.WaitForCallsAsync(1);
        await parser.StopAsync(CancellationToken.None);

        Assert.Equal(
            [
                "https://www.work.ua/robots.txt",
                "https://www.work.ua/jobs/?ss=1", "https://www.work.ua/jobs/11/", "https://www.work.ua/jobs/12/",
                "https://www.work.ua/jobs/?ss=1&page=2"
            ],
            _web.Urls);
        Assert.All(_ingestion.Received, job => Assert.Equal(JobSource.WorkUa, job.Source));
        Assert.Equal(["Jira, SQL", null], _ingestion.Received.Select(job => job.Tags));
        // Повний опис зі сторінки вакансії замість скороченого з картки
        Assert.All(_ingestion.Received, job => Assert.Contains("Досвід ручного тестування", job.DescriptionHtml));
    }

    [Fact]
    public async Task Dou_ReadsFeedAndNewJobPages()
    {
        _web.Responses["https://jobs.dou.ua/vacancies/feeds/"] = (HttpStatusCode.OK, DouPageTests.Feed(DouPageTests.Item(1), DouPageTests.Item(2)));
        _web.Responses["https://jobs.dou.ua/companies/acme/vacancies/1/"] = (HttpStatusCode.OK, DouPageTests.JobPage());
        JobParserService parser = CreateParser(directory: null, douPages: 5);

        await parser.StartAsync(CancellationToken.None);
        await _ingestion.WaitForCallsAsync(1);
        await parser.StopAsync(CancellationToken.None);

        // Стрічка одна: «сторінки 2» не просимо, навіть якщо в налаштуваннях більше
        Assert.Equal(
            [
                "https://jobs.dou.ua/robots.txt",
                "https://jobs.dou.ua/vacancies/feeds/",
                "https://jobs.dou.ua/companies/acme/vacancies/1/",
                "https://jobs.dou.ua/companies/acme/vacancies/2/"
            ],
            _web.Urls);
        Assert.All(_ingestion.Received, job => Assert.Equal(JobSource.Dou, job.Source));
        // Сторінка вакансії 1 уточнила поля, а сторінка 2 вакансією не була — лишилися дані зі стрічки
        Assert.Equal(["Middle Java Developer", "Вакансія 2"], _ingestion.Received.Select(job => job.Title));
        Assert.Equal(["Kvitka Bank", "Acme"], _ingestion.Received.Select(job => job.Company));
    }

    [Fact]
    public async Task Sites_AndDumps_AreProcessedSeparately()
    {
        _web.FirstPage = DjinniPageTests.Page(DjinniPageTests.Job(7));
        _web.Responses["https://www.work.ua/jobs/?ss=1"] = (HttpStatusCode.OK, WorkUaPageTests.ListPage(WorkUaPageTests.Card(8)));
        File.WriteAllText(Path.Combine(_directory, "dump.json"), ValidDump);
        JobParserService parser = CreateParser(_directory, djinniPages: 1, workUaPages: 1);

        await parser.StartAsync(CancellationToken.None);
        await _ingestion.WaitForCallsAsync(3);
        await parser.StopAsync(CancellationToken.None);

        // Кожне джерело — окремий виклик нормалізації: збій одного не зачепить інші
        Assert.Equal(["7", "8", "1"], _ingestion.Received.Select(job => job.Id));
        Assert.Equal([JobSource.Djinni, JobSource.WorkUa, JobSource.Djinni], _ingestion.Received.Select(job => job.Source));
    }

    [Fact]
    public async Task Crawler_ForgetsVacanciesThatLeftTheList()
    {
        SiteCrawler crawler = new(new DjinniParser(), pages: 1, TimeSpan.Zero, _web, _logger);

        _web.FirstPage = DjinniPageTests.Page(DjinniPageTests.Job(1));
        await crawler.CrawlAsync(CancellationToken.None);
        _web.FirstPage = DjinniPageTests.Page(DjinniPageTests.Job(2));
        await crawler.CrawlAsync(CancellationToken.None);
        _web.FirstPage = DjinniPageTests.Page(DjinniPageTests.Job(1));
        await crawler.CrawlAsync(CancellationToken.None);

        // Вакансія 1 зникала зі списку, тож пам'ять про неї стерто, і її сторінку прочитано знову: пам'ять не росте без кінця
        Assert.Equal(2, _web.Urls.Count(url => url == "https://djinni.co/jobs/1-job/"));
    }

    [Fact]
    public async Task Crawler_InterruptedCrawl_KeepsRememberedJobPages()
    {
        SiteCrawler crawler = new(new DjinniParser(), pages: 2, TimeSpan.Zero, _web, _logger);
        _web.FirstPage = DjinniPageTests.Page(DjinniPageTests.Job(1));
        _web.Responses["https://djinni.co/jobs/?page=2"] = (HttpStatusCode.OK, DjinniPageTests.Page(DjinniPageTests.Job(2)));
        await crawler.CrawlAsync(CancellationToken.None);

        // Друга сторінка списку не відкрилася, тож про вакансію 2 нічого не відомо, але забувати її сторінку зарано.
        // Збій — тайм-аут, а не відмова сайту, тож паузи немає і наступний обхід іде одразу
        _web.TimeoutUrls.Add("https://djinni.co/jobs/?page=2");
        await crawler.CrawlAsync(CancellationToken.None);
        _web.TimeoutUrls.Clear();
        List<RawVacancy> jobs = await crawler.CrawlAsync(CancellationToken.None);

        Assert.Equal(["1", "2"], jobs.Select(job => job.Id));
        Assert.Single(_web.Urls, url => url == "https://djinni.co/jobs/2-job/");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, true)]           // перевірка на ботів (Cloudflare)
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.InternalServerError, false)] // випадковий збій — не привід чекати годину
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task Crawler_PausesOnlyWhenSiteRefuses(HttpStatusCode status, bool paused)
    {
        ManualTime time = new();
        SiteCrawler crawler = new(new DjinniParser(), pages: 1, TimeSpan.Zero, _web, _logger, time);
        _web.Responses["https://djinni.co/jobs/"] = (status, "Перевірка браузера");

        await crawler.CrawlAsync(CancellationToken.None);
        time.Now += TimeSpan.FromMinutes(30); // наступний запуск за розкладом
        await crawler.CrawlAsync(CancellationToken.None);

        Assert.Equal(paused ? 1 : 2, _web.Urls.Count(url => url == "https://djinni.co/jobs/"));
        Assert.Equal(paused, _logger.Entries.Any(e => e.Message.Contains("відмовив у доступі")));
    }

    [Fact]
    public async Task Crawler_PauseDoublesWithEachRefusal_AndResetsAfterSuccess()
    {
        ManualTime time = new();
        SiteCrawler crawler = new(new DjinniParser(), pages: 1, TimeSpan.Zero, _web, _logger, time);
        _web.Responses["https://djinni.co/jobs/"] = (HttpStatusCode.Forbidden, "");

        await Crawl(minutesLater: 0);                              // відмова → пауза 1 год
        Assert.Equal(1, ListRequests());
        await Crawl(minutesLater: 59);                             // ще пауза
        Assert.Equal(1, ListRequests());
        await Crawl(minutesLater: 2);                              // минула година; знову відмова → 2 год
        Assert.Equal(2, ListRequests());
        await Crawl(minutesLater: 119);
        Assert.Equal(2, ListRequests());
        _web.Responses.Remove("https://djinni.co/jobs/");          // сайт знову пускає
        await Crawl(minutesLater: 2);
        Assert.Equal(3, ListRequests());

        _web.Responses["https://djinni.co/jobs/"] = (HttpStatusCode.Forbidden, "");
        await Crawl(minutesLater: 30);                             // успіх скинув паузу: знову 1 год, а не 4
        await Crawl(minutesLater: 61);
        Assert.Equal(5, ListRequests());

        async Task Crawl(int minutesLater)
        {
            time.Now += TimeSpan.FromMinutes(minutesLater);
            await crawler.CrawlAsync(CancellationToken.None);
        }

        int ListRequests() => _web.Urls.Count(url => url == "https://djinni.co/jobs/");
    }

    [Fact]
    public async Task Crawler_HonorsRetryAfter()
    {
        ManualTime time = new();
        SiteCrawler crawler = new(new DjinniParser(), pages: 1, TimeSpan.Zero, _web, _logger, time);
        _web.Responses["https://djinni.co/jobs/"] = (HttpStatusCode.TooManyRequests, "");
        _web.RetryAfter["https://djinni.co/jobs/"] = TimeSpan.FromHours(3); // довше за нашу першу паузу в 1 год

        await crawler.CrawlAsync(CancellationToken.None);
        time.Now += TimeSpan.FromHours(2);
        await crawler.CrawlAsync(CancellationToken.None);
        time.Now += TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1);
        await crawler.CrawlAsync(CancellationToken.None);

        Assert.Equal(2, _web.Urls.Count(url => url == "https://djinni.co/jobs/"));
    }

    [Theory]
    [InlineData("00:30:00", "00:00:02", 3, 3, true)]   // як у appsettings.json
    [InlineData("00:10:00", "00:00:00.5", 0, 10, true)] // самі межі
    [InlineData("00:00:10", "00:00:02", 3, 3, false)]  // «хоч кожні 10 секунд»
    [InlineData("00:30:00", "00:00:00.1", 3, 3, false)] // 10 запитів на секунду
    [InlineData("00:30:00", "00:00:02", 11, 3, false)]
    [InlineData("00:30:00", "00:00:02", 3, -1, false)]
    public void OptionsValidator_RejectsIntrusiveSettings(string interval, string delay, int djinniPages, int workUaPages, bool valid)
    {
        IngestionOptions options = new()
        {
            Interval = TimeSpan.Parse(interval, CultureInfo.InvariantCulture),
            RequestDelay = TimeSpan.Parse(delay, CultureInfo.InvariantCulture),
            DjinniPages = djinniPages,
            WorkUaPages = workUaPages
        };

        ValidateOptionsResult result = new IngestionOptionsValidator().Validate(null, options);

        Assert.Equal(valid, result.Succeeded);
    }

    [Fact]
    public async Task Crawler_SkipsJobPagesForbiddenByRobots()
    {
        _web.Responses["https://djinni.co/robots.txt"] = (HttpStatusCode.OK, "User-agent: *\nDisallow: /jobs/1-");
        _web.FirstPage = DjinniPageTests.Page(DjinniPageTests.Job(1), DjinniPageTests.Job(2));
        SiteCrawler crawler = new(new DjinniParser(), pages: 1, TimeSpan.Zero, _web, _logger);

        List<RawVacancy> jobs = await crawler.CrawlAsync(CancellationToken.None);

        // Вакансію 1 збережено зі списку, але її сторінку не відкривали
        Assert.Equal(["1", "2"], jobs.Select(job => job.Id));
        Assert.DoesNotContain("https://djinni.co/jobs/1-job/", _web.Urls);
        Assert.Contains("https://djinni.co/jobs/2-job/", _web.Urls);
    }

    [Fact]
    public async Task Crawler_MissingRobotsTxt_AllowsEverything()
    {
        // За RFC 9309 відсутній robots.txt (4xx) нічого не забороняє
        _web.Responses["https://djinni.co/robots.txt"] = (HttpStatusCode.NotFound, "");
        _web.FirstPage = DjinniPageTests.Page(DjinniPageTests.Job(1));
        SiteCrawler crawler = new(new DjinniParser(), pages: 1, TimeSpan.Zero, _web, _logger);

        Assert.Equal("1", Assert.Single(await crawler.CrawlAsync(CancellationToken.None)).Id);
    }

    private JobParserService CreateParser(string? directory, bool enabled = true, TimeSpan? interval = null, int djinniPages = 0, int workUaPages = 0, int douPages = 0)
    {
        ServiceProvider services = new ServiceCollection()
            .AddScoped<IVacancyIngestionService>(_ => _ingestion)
            .BuildServiceProvider();

        IngestionOptions options = new()
        {
            Enabled = enabled,
            RawDataDirectory = directory,
            Interval = interval ?? TimeSpan.FromHours(1),
            DjinniPages = djinniPages,
            WorkUaPages = workUaPages,
            DouPages = douPages,
            RequestDelay = TimeSpan.Zero
        };

        return new JobParserService(
            services.GetRequiredService<IServiceScopeFactory>(),
            _web,
            Options.Create(options),
            new HostingEnvironment { ContentRootPath = _directory },
            _logger);
    }

    /// <summary>
    /// Підробка djinni.co і work.ua: віддає підготовлені сторінки й запам'ятовує запити, не виходячи в мережу.
    /// Будь-яка непідготовлена адреса отримує FirstPage — так справжній Djinni відповідає за межами списку.
    /// </summary>
    private sealed class FakeWeb : HttpMessageHandler, IHttpClientFactory
    {
        private readonly List<string> _urls = [];

        public Dictionary<string, (HttpStatusCode Status, string Body)> Responses { get; } = new()
        {
            ["https://djinni.co/robots.txt"] = (HttpStatusCode.OK, RobotsTxtTests.Djinni),
            ["https://www.work.ua/robots.txt"] = (HttpStatusCode.OK, RobotsTxtTests.WorkUa),
            ["https://jobs.dou.ua/robots.txt"] = (HttpStatusCode.OK, RobotsTxtTests.Dou)
        };

        public string FirstPage { get; set; } = string.Empty;

        public HashSet<string> TimeoutUrls { get; } = [];

        public Dictionary<string, TimeSpan> RetryAfter { get; } = [];

        public string? UserAgent { get; private set; }

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

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            lock (_urls)
            {
                _urls.Add(url);
                UserAgent = request.Headers.UserAgent.ToString();
            }

            if (TimeoutUrls.Contains(url))
            {
                throw new TaskCanceledException("Запит перевищив тайм-аут.");
            }

            (HttpStatusCode status, string body) = Responses.TryGetValue(url, out var response) ? response : (HttpStatusCode.OK, FirstPage);
            HttpResponseMessage message = new(status) { Content = new StringContent(body) };
            if (RetryAfter.TryGetValue(url, out TimeSpan delay))
            {
                message.Headers.RetryAfter = new RetryConditionHeaderValue(delay);
            }

            return Task.FromResult(message);
        }
    }

    /// <summary>Годинник, який тест переводить вручну: перевіряємо паузи в годинах, не чекаючи їх.</summary>
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Заглушка сервісу нормалізації: запам'ятовує, які вакансії й скільки разів йому передали.</summary>
    private sealed class RecordingIngestionService : IVacancyIngestionService
    {
        private readonly object _lock = new();
        private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public List<RawVacancy> Received { get; } = [];

        public Task<IngestionResult> ProcessRawVacanciesAsync(IReadOnlyList<RawVacancy> rawItems, CancellationToken ct = default)
        {
            lock (_lock)
            {
                Received.AddRange(rawItems);
                _calls++;
                _changed.TrySetResult();
                _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            return Task.FromResult(new IngestionResult(rawItems.Count, 0, 0));
        }

        public async Task WaitForCallsAsync(int count)
        {
            while (true)
            {
                Task changed;
                lock (_lock)
                {
                    if (_calls >= count)
                    {
                        return;
                    }

                    changed = _changed.Task;
                }

                await changed.WaitAsync(WaitLimit);
            }
        }
    }

    /// <summary>Логер-шпигун: збирає записи, щоб тест перевірив, що саме парсер повідомив.</summary>
    private sealed class RecordingLogger : ILogger<JobParserService>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];
        private readonly TaskCompletionSource _logged = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_entries)
                {
                    return _entries.ToList();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
            {
                _entries.Add((logLevel, formatter(state, exception)));
            }

            if (logLevel >= LogLevel.Warning)
            {
                _logged.TrySetResult();
            }
        }

        public Task WaitForWarningAsync() => _logged.Task.WaitAsync(WaitLimit);
    }
}
