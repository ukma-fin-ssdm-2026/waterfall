// AI: ввічливий обхід сайтів-джерел вакансій; перевірено: юніт-тести JobParserServiceTests з підробленими сайтами, ручний запуск API проти djinni.co і work.ua, самоперевірка автора (§8)

using System.Net;
using System.Net.Http.Headers;
using Waterfall.Services.Interfaces;

namespace Waterfall.Api.Workers;

/// <summary>
/// Те, чим сайти відрізняються: адреси сторінок і розбір їхньої розмітки.
/// Обхід, паузи між запитами й robots.txt бере на себе <see cref="SiteCrawler"/>.
/// </summary>
public interface IJobSiteParser
{
    /// <summary>Назва для журналу, наприклад «djinni.co».</summary>
    string Name { get; }

    /// <summary>Головна сторінка: звідси береться robots.txt, і лише на цей хост сервіс ходить по сторінки вакансій.</summary>
    Uri Home { get; }

    /// <summary>Адреса N-ї сторінки списку вакансій (нумерація з 1); null — такої сторінки на сайті немає.</summary>
    string? ListPageUrl(int page);

    /// <summary>Вакансії зі сторінки списку; порожній список — вакансій немає.</summary>
    List<RawVacancy> ParseListPage(string html);

    /// <summary>Те, чого немає в списку, зі сторінки однієї вакансії; поля, яких на сторінці немає, лишаються null.</summary>
    RawVacancy ParseJobPage(string html);
}

/// <summary>
/// Обходить один сайт, не навантажуючи його (THR-11, PERF-003): перевіряє robots.txt, робить паузу перед кожним запитом,
/// читає кілька сторінок списку, а для нових вакансій — ще й їхні сторінки.
/// Сторінку вакансії читає один раз: поки вакансія лишається в списку, дані з неї беруться з пам'яті.
/// </summary>
public sealed class SiteCrawler
{
    // Чесно називаємо себе сайту (THR-11): за адресою репозиторію видно, хто робить запити
    private const string UserAgent = "WaterfallJobParser/1.0 (+https://github.com/ukma-fin-ssdm-2026/waterfall)";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    // Сторінка зі списком важить 300–400 КБ; у кілька разів більша відповідь — не та сторінка, на яку чекаємо
    private const long MaxPageSize = 5 * 1024 * 1024;

    // Сайт відмовив (403 — перевірка на ботів, 429 — забагато запитів, 503 — перевантаження): даємо йому відпочити годину,
    // за кожну наступну відмову поспіль — удвічі довше, але не більше доби
    private static readonly TimeSpan FirstPause = TimeSpan.FromHours(1);
    private static readonly TimeSpan LongestPause = TimeSpan.FromDays(1);
    private static readonly HttpStatusCode[] RefusalCodes = [HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable];

    private readonly IJobSiteParser _site;
    private readonly int _pages;
    private readonly TimeSpan _requestDelay;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    // Дані зі сторінок вакансій, що були в списку під час останнього обходу
    private Dictionary<string, RawVacancy> _jobPages = [];

    private TimeSpan _pause = TimeSpan.Zero;
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;

    public SiteCrawler(IJobSiteParser site, int pages, TimeSpan requestDelay, IHttpClientFactory httpClientFactory, ILogger logger, TimeProvider? time = null)
    {
        _site = site;
        _pages = pages;
        _requestDelay = requestDelay;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public string Name => _site.Name;

    /// <summary>Сайт вимкнено, якщо в налаштуваннях для нього 0 сторінок.</summary>
    public bool Enabled => _pages > 0;

    /// <summary>
    /// Читає перші сторінки списку вакансій і сторінки нових вакансій.
    /// Якщо сайт перестав відповідати посеред обходу, повертає те, що встигли зібрати.
    /// </summary>
    public async Task<List<RawVacancy>> CrawlAsync(CancellationToken ct)
    {
        if (_time.GetUtcNow() < _pausedUntil)
        {
            _logger.LogInformation("{Site} пропускаємо до {Until:u}: сайт нещодавно відмовив у доступі.", _site.Name, _pausedUntil);
            return [];
        }

        using HttpClient http = _httpClientFactory.CreateClient(nameof(JobParserService));
        http.Timeout = RequestTimeout;
        http.MaxResponseContentBufferSize = MaxPageSize;
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        // Заголовки на сторінках («Необхідні навички») залежать від мови
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("uk");

        // robots.txt читаємо щоразу: сайт може будь-коли закрити розділ для ботів
        RobotsTxt? robots = await LoadRobotsAsync(http, ct);
        if (robots is null)
        {
            return [];
        }

        CrawlState crawl = new();
        for (int page = 1; page <= _pages; page++)
        {
            string? url = _site.ListPageUrl(page);
            if (url is null)
            {
                break;
            }

            if (!robots.IsAllowed(new Uri(url).PathAndQuery))
            {
                _logger.LogWarning("robots.txt {Site} не дозволяє ботам читати {Url}, тож далі не йдемо.", _site.Name, url);
                break;
            }

            List<RawVacancy> jobs;
            try
            {
                jobs = _site.ParseListPage(await GetPageAsync(http, url, ct));
            }
            catch (Exception ex) when (IsSiteFailure(ex, ct))
            {
                _logger.LogWarning("Сторінка {Url} недоступна ({Error}); обробляємо {Count} вакансій, зібраних раніше.", url, ex.Message, crawl.Items.Count);
                crawl.Refusal = ex as SiteRefusedException;
                crawl.Interrupted = true;
                break;
            }

            // За межами списку сайт показує першу сторінку (Djinni) чи порожню (Work.ua): нових вакансій немає — зупиняємося.
            // Так само відкидаємо вакансію, що з'їхала на наступну сторінку, поки ми читали попередню (артефакт пагінації).
            List<RawVacancy> fresh = jobs.Where(job => job.Id is null || crawl.SeenIds.Add(job.Id)).ToList();
            if (fresh.Count == 0)
            {
                if (page == 1)
                {
                    _logger.LogWarning("На {Url} не знайдено вакансій: можливо, {Site} змінив розмітку сторінки.", url, _site.Name);
                }

                break;
            }

            foreach (RawVacancy job in fresh)
            {
                RawVacancy? details = null;
                if (job.Id is not null && !_jobPages.TryGetValue(job.Id, out details) && crawl.ReadJobPages && IsAllowedJobPage(job.Url, robots))
                {
                    details = await ReadJobPageAsync(http, job.Url!, crawl, ct);
                }

                if (details is not null)
                {
                    crawl.JobPages[job.Id!] = details;
                }

                crawl.Items.Add(details is null ? job : WithJobPage(job, details));
            }
        }

        // Пам'ятаємо лише вакансії з цього обходу: решта зникла зі списку й більше не знадобиться.
        // Якщо ж обхід обірвався, старі записи ще можуть знадобитися наступного разу.
        if (crawl.Interrupted)
        {
            foreach ((string id, RawVacancy details) in crawl.JobPages)
            {
                _jobPages[id] = details;
            }
        }
        else
        {
            _jobPages = crawl.JobPages;
        }

        if (crawl.Refusal is null)
        {
            _pause = TimeSpan.Zero;
        }
        else
        {
            PauseAfterRefusal(crawl.Refusal.RetryAfter);
        }

        _logger.LogInformation("З {Site} завантажено вакансій: {Count}.", _site.Name, crawl.Items.Count);
        return crawl.Items;
    }

    /// <summary>Дані зі сторінки вакансії; null — сторінка не відкрилася.</summary>
    private async Task<RawVacancy?> ReadJobPageAsync(HttpClient http, string url, CrawlState crawl, CancellationToken ct)
    {
        try
        {
            return _site.ParseJobPage(await GetPageAsync(http, url, ct));
        }
        catch (Exception ex) when (IsSiteFailure(ex, ct))
        {
            // Вакансія корисна й без сторінки: збережемо те, що є в списку.
            // Решту сторінок вакансій у цьому обході не чіпаємо, щоб не тиснути на сайт, який уже збоїть.
            _logger.LogWarning("Сторінка вакансії {Url} недоступна ({Error}); решту вакансій беремо лише зі списку.", url, ex.Message);
            crawl.Refusal = ex as SiteRefusedException;
            crawl.ReadJobPages = false;
            return null;
        }
    }

    /// <summary>Дані зі сторінки вакансії доповнюють і уточнюють дані зі списку (наприклад, повний опис замість скороченого).</summary>
    public static RawVacancy WithJobPage(RawVacancy fromList, RawVacancy fromPage) => fromList with
    {
        Title = fromPage.Title ?? fromList.Title,
        Company = fromPage.Company ?? fromList.Company,
        Salary = fromPage.Salary ?? fromList.Salary,
        Location = fromPage.Location ?? fromList.Location,
        Posted = fromPage.Posted ?? fromList.Posted,
        Experience = fromPage.Experience ?? fromList.Experience,
        Employment = fromPage.Employment ?? fromList.Employment,
        Category = fromPage.Category ?? fromList.Category,
        DescriptionHtml = fromPage.DescriptionHtml ?? fromList.DescriptionHtml,
        Tags = fromPage.Tags ?? fromList.Tags
    };

    /// <summary>
    /// Відсутній robots.txt (4xx) дозволяє все, а збій сервера (5xx) — ні: тоді сайт пропускаємо (null)
    /// й даємо йому відпочити. Так само, якщо сайт попросив не поспішати (429).
    /// </summary>
    private async Task<RobotsTxt?> LoadRobotsAsync(HttpClient http, CancellationToken ct)
    {
        Uri robotsUrl = new(_site.Home, "/robots.txt");
        using HttpResponseMessage response = await http.GetAsync(robotsUrl, ct);
        if (response.IsSuccessStatusCode)
        {
            return RobotsTxt.Parse(await response.Content.ReadAsStringAsync(ct));
        }

        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("{Url} недоступний (код {Status}), тож сайт пропускаємо.", robotsUrl, (int)response.StatusCode);
            PauseAfterRefusal(response.Headers.RetryAfter);
            return null;
        }

        return RobotsTxt.AllowAll;
    }

    private async Task<string> GetPageAsync(HttpClient http, string url, CancellationToken ct)
    {
        // Пауза перед кожним запитом, щоб не навантажувати сайт і не отримати бан (THR-11, PERF-003)
        await Task.Delay(_requestDelay, ct);
        using HttpResponseMessage response = await http.GetAsync(url, ct);
        if (RefusalCodes.Contains(response.StatusCode))
        {
            throw new SiteRefusedException(response);
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Не звертаємося до сайту, поки не мине пауза. Якщо сайт сам сказав у Retry-After, скільки чекати, і це довше,
    /// ніж ми чекали б самі, слухаємося сайту.
    /// </summary>
    private void PauseAfterRefusal(RetryConditionHeaderValue? retryAfter)
    {
        _pause = _pause == TimeSpan.Zero ? FirstPause : TimeSpan.FromTicks(Math.Min(_pause.Ticks * 2, LongestPause.Ticks));
        DateTimeOffset now = _time.GetUtcNow();
        DateTimeOffset askedUntil = retryAfter?.Date ?? now + (retryAfter?.Delta ?? TimeSpan.Zero);
        _pausedUntil = now + _pause > askedUntil ? now + _pause : askedUntil;
        _logger.LogWarning("{Site} відмовив у доступі, тож наступна спроба не раніше {Until:u}.", _site.Name, _pausedUntil);
    }

    // Ходимо лише на сам сайт, навіть якщо в даних сторінки опиниться чуже посилання, і лише туди, куди дозволяє robots.txt
    private bool IsAllowedJobPage(string? url, RobotsTxt robots) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host == _site.Home.Host
        && robots.IsAllowed(uri.PathAndQuery);

    // Збій мережі, код 4xx/5xx або тайм-аут запиту (TaskCanceledException без скасування ct)
    private static bool IsSiteFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested);

    /// <summary>Сайт прямо відмовив (403, 429, 503) — на відміну від випадкового збою, це привід зробити паузу.</summary>
    private sealed class SiteRefusedException(HttpResponseMessage response)
        : HttpRequestException($"сайт відмовив: {(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode)
    {
        public RetryConditionHeaderValue? RetryAfter { get; } = response.Headers.RetryAfter;
    }

    /// <summary>Те, що накопичується за один обхід сайту.</summary>
    private sealed class CrawlState
    {
        public List<RawVacancy> Items { get; } = [];

        // Id вакансій, уже бачених у цьому обході
        public HashSet<string> SeenIds { get; } = [];

        // Дані зі сторінок вакансій цього обходу; після обходу вони замінять _jobPages
        public Dictionary<string, RawVacancy> JobPages { get; } = [];

        // false — сторінка вакансії вже не відкрилася, тож решту вакансій беремо лише зі списку
        public bool ReadJobPages { get; set; } = true;

        // Сторінка списку не відкрилася, і обхід обірвався
        public bool Interrupted { get; set; }

        // Останній збій, якщо це була пряма відмова сайту (403, 429, 503): привід для паузи
        public SiteRefusedException? Refusal { get; set; }
    }
}
