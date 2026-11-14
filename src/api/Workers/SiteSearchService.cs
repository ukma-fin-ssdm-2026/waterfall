// AI: пошук на сайтах на запит користувача: адреси з фільтрами, черга за сайтами, прогрес, кеш і ліміти (issue #62); перевірено: юніт-тести SiteSearchServiceTests і SiteSearchUrlsTests, ручний запуск проти djinni.co і jobs.dou.ua, самоперевірка автора (§8)
// AI: позначка «переглянуто» для знайдених на сайтах вакансій (issue #62, на прохання Дениса); перевірено: тести SiteSearchServiceTests, самоперевірка автора (§8)
// AI: стартова сторінка fromPage, кілька значень фільтра Work.ua через «+» (issue #62, на прохання Дениса); перевірено: тести SiteSearchServiceTests, самоперевірка автора (§8)
// AI: додаткові фільтри Djinni (english_level, company_type, domain, region, country) в адресі (issue #62); перевірено: тести SiteSearchServiceTests, самоперевірка автора (§8)
// AI: Work.ua і Robota.ua через браузер автора (IBrowserFetcher) без robots.txt, адреси з фільтрами звірено в браузері 10.11.2026 (issue #62); перевірено: юніт-тести з підробленим браузером, самоперевірка автора (§8)

using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Options;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.SiteSearch;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Api.Workers;

/// <summary>Пошук на сайтах на запит: запуск і стан пошуку для шкали прогресу.</summary>
public interface ISiteSearchService
{
    /// <summary>Запускає пошук (або одразу віддає результат з кешу); якщо користувач перевищив ліміт, повертає причину відмови.</summary>
    SiteSearchStart Start(SiteSearchRequest request, Guid userId);

    /// <summary>Стан пошуку; знайдені вакансії — коли він завершився. null — такого пошуку цього користувача немає.</summary>
    Task<SiteSearchStatusDto?> GetAsync(Guid id, Guid userId, CancellationToken ct = default);
}

public sealed record SiteSearchStart(SiteSearchStatusDto? Status, string? Rejection);

/// <summary>
/// Пошук на запит (issue #62): користувач обрав сайт і фільтри — сервер читає сторінки списку з цими фільтрами
/// через <see cref="SiteCrawler"/> (пауза між запитами), зберігає вакансії через <see cref="IVacancyIngestionService"/>
/// і віддає їх клієнту. Поки йде обхід, клієнт бачить прогрес. Djinni і DOU читаються звичайним HTTP з robots.txt;
/// Work.ua і Robota.ua — браузером (<see cref="IBrowserFetcher"/>) без robots.txt разом зі сторінкою кожної вакансії,
/// бо повного опису, навичок і графіка в їхньому списку немає (Work.ua за бажанням — лише за списком). До одного сайту одночасно йде один пошук; такий самий пошук протягом
/// <see cref="CacheLifetime"/> віддається з бази.
/// </summary>
public sealed class SiteSearchService : ISiteSearchService
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(3);
    public const int SearchesPerHour = 20;

    // Скільки тримаємо в пам'яті завершений пошук, щоб клієнт встиг забрати результат
    private static readonly TimeSpan JobLifetime = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IngestionOptions _options;
    private readonly ILogger<SiteSearchService> _logger;
    private readonly IBrowserFetcher? _browserFetcher;
    private readonly TimeProvider _time;
    private readonly CancellationToken _stopping;

    private readonly ConcurrentDictionary<Guid, SearchJob> _jobs = new();
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, IReadOnlyList<string> Ids)> _cache = new();
    private readonly ConcurrentDictionary<Guid, List<DateTimeOffset>> _userSearches = new();
    private readonly ConcurrentDictionary<JobSource, SemaphoreSlim> _siteGates = new();

    public SiteSearchService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IOptions<IngestionOptions> options,
        IHostApplicationLifetime lifetime,
        ILogger<SiteSearchService> logger,
        IBrowserFetcher? browserFetcher = null,
        TimeProvider? time = null)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _browserFetcher = browserFetcher;
        _time = time ?? TimeProvider.System;
        _stopping = lifetime.ApplicationStopping;
    }

    public SiteSearchStart Start(SiteSearchRequest request, Guid userId)
    {
        DateTimeOffset now = _time.GetUtcNow();
        ForgetOldJobs(now);

        JobSource source = request.Source!.Value;
        string key = CacheKey(request);
        if (_cache.TryGetValue(key, out (DateTimeOffset At, IReadOnlyList<string> Ids) cached) && now - cached.At < CacheLifetime)
        {
            SearchJob fromCache = new(userId, source, key, request.Pages, now) { FromCache = true };
            fromCache.Complete(cached.Ids, cached.Ids.Count);
            _jobs[fromCache.Id] = fromCache;
            return new SiteSearchStart(fromCache.Snapshot(), null);
        }

        if (_jobs.Values.Any(job => job.UserId == userId && job.IsActive))
        {
            return new SiteSearchStart(null, "Попередній пошук ще триває: дочекайтеся його завершення.");
        }

        List<DateTimeOffset> recent = _userSearches.GetOrAdd(userId, _ => []);
        lock (recent)
        {
            recent.RemoveAll(at => now - at > TimeSpan.FromHours(1));
            if (recent.Count >= SearchesPerHour)
            {
                return new SiteSearchStart(null, $"Не більше {SearchesPerHour} пошуків на сайтах на годину. Спробуйте пізніше або скористайтеся фільтрами каталогу.");
            }

            recent.Add(now);
        }

        SearchJob job = new(userId, source, key, request.Pages, now);
        _jobs[job.Id] = job;
        // Скасування при зупинці сервера RunAsync обробляє сам через _stopping: так завдання встигне позначити пошук як невдалий
        _ = Task.Run(() => RunAsync(job, request), CancellationToken.None);
        return new SiteSearchStart(job.Snapshot(), null);
    }

    public async Task<SiteSearchStatusDto?> GetAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        if (!_jobs.TryGetValue(id, out SearchJob? job) || job.UserId != userId)
        {
            return null;
        }

        SiteSearchStatusDto status = job.Snapshot();
        if (status.State != SiteSearchStates.Completed)
        {
            return status;
        }

        return status with { Vacancies = await LoadVacanciesAsync(job.Source, job.ExternalIds, userId, ct) };
    }

    /// <summary>
    /// Адреса сторінки списку з фільтрами: Djinni — сторінка пошуку, DOU — офіційна RSS-стрічка (25 найсвіжіших),
    /// Work.ua — /jobs-{місто}-{сфера}-{запит}/?фільтри, Robota.ua — /zapros/{запит}/{місто}/params;фільтри.
    /// </summary>
    public static string? ListPageUrl(SiteSearchRequest request, int page)
    {
        if (request.Source == JobSource.WorkUa)
        {
            return WorkUaListPageUrl(request, page);
        }

        if (request.Source == JobSource.RobotaUa)
        {
            return RobotaUaListPageUrl(request, page);
        }

        List<KeyValuePair<string, string>> query = [];
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                query.Add(new(name, value.Trim()));
            }
        }

        string url;
        if (request.Source == JobSource.Djinni)
        {
            foreach (string category in request.Categories ?? [])
            {
                Add("primary_keyword", category);
            }

            Add("exp_level", request.Experience);
            Add("location", request.City);
            Add("employment", request.Remote ? "remote" : request.Employment);
            Add("salary", request.SalaryFrom is > 0 ? request.SalaryFrom.Value.ToString(CultureInfo.InvariantCulture) : null);
            Add("all_keywords", request.Keywords);
            // Англійська, тип компанії, домен, регіон, країна (english_level=no_english, company_type=product…)
            foreach (KeyValuePair<string, string> option in SortedOptions(request))
            {
                Add(option.Key, option.Value);
            }

            Add("page", page > 1 ? page.ToString(CultureInfo.InvariantCulture) : null);
            url = "https://djinni.co/jobs/";
        }
        else
        {
            // Стрічка DOU одна: 25 найсвіжіших за фільтрами, другої сторінки немає
            if (page > 1)
            {
                return null;
            }

            Add("category", FirstCategory(request));
            Add("exp", request.Experience);
            Add("city", request.City);
            Add("remote", request.Remote ? "1" : null);
            Add("search", request.Keywords);
            url = "https://jobs.dou.ua/vacancies/feeds/";
        }

        return WithQuery(url, query);
    }

    /// <summary>Адреса з параметрами запиту; без параметрів — сама адреса.</summary>
    private static string WithQuery(string url, List<KeyValuePair<string, string>> query) => query.Count == 0
        ? url
        : url + "?" + string.Join("&", query.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

    private static string? FirstCategory(SiteSearchRequest request) =>
        request.Categories is { Count: > 0 } categories ? categories[0] : null;

    /// <summary>
    /// Work.ua: місто, сфера й запит — у шляху (/jobs-kyiv-it-python/, слова запиту через «+»), решта фільтрів — параметри.
    /// Віддалена робота на Work.ua — це «місто» remote.
    /// </summary>
    private static string WorkUaListPageUrl(SiteSearchRequest request, int page)
    {
        string? city = !string.IsNullOrWhiteSpace(request.City) ? request.City.Trim() : request.Remote ? "remote" : null;
        List<string> path = new[] { city, FirstCategory(request)?.Trim(), SlugOf(request.Keywords, "+") }
            .OfType<string>()
            .Where(part => part.Length > 0)
            .ToList();

        // Без міста, сфери й запиту /jobs/ — сторінка пошуку без вакансій; ss=1 дає результати по всій Україні
        List<KeyValuePair<string, string>> query = path.Count == 0 ? [new("ss", "1")] : [];
        query.AddRange(SortedOptions(request));
        if (page > 1)
        {
            query.Add(new("page", page.ToString(CultureInfo.InvariantCulture)));
        }

        string url = path.Count == 0 ? "https://www.work.ua/jobs/" : $"https://www.work.ua/jobs-{string.Join('-', path)}/";
        // Кілька значень одного фільтра Work.ua пише через «+» (category=1+9, language=1+41)
        return query.Count == 0
            ? url
            : url + "?" + string.Join("&", query.Select(p => $"{Uri.EscapeDataString(p.Key)}={string.Join('+', p.Value.Split(',').Select(Uri.EscapeDataString))}"));
    }

    /// <summary>
    /// Robota.ua: /zapros/{запит через «-»}/{місто, без нього — ukraine}, фільтри й сторінка — після «/params;» через «;».
    /// Віддалена робота — scheduleIds=3.
    /// </summary>
    private static string RobotaUaListPageUrl(SiteSearchRequest request, int page)
    {
        string city = string.IsNullOrWhiteSpace(request.City) ? "ukraine" : request.City.Trim();
        string? keywords = SlugOf(request.Keywords, "-");
        List<KeyValuePair<string, string>> parameters = SortedOptions(request).ToList();
        if (request.Remote && parameters.All(p => p.Key != "scheduleIds"))
        {
            parameters.Add(new("scheduleIds", "3"));
        }

        if (page > 1)
        {
            parameters.Add(new("page", page.ToString(CultureInfo.InvariantCulture)));
        }

        string url = keywords is null ? $"https://robota.ua/zapros/{city}" : $"https://robota.ua/zapros/{keywords}/{city}";
        return parameters.Count == 0 ? url : url + "/params;" + string.Join(";", parameters.Select(p => $"{p.Key}={p.Value}"));
    }

    /// <summary>Пошуковий запит для шляху адреси: слова в нижньому регістрі, кожне закодоване, між ними — роздільник сайту.</summary>
    private static string? SlugOf(string? keywords, string separator)
    {
        string[] words = (keywords ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? null : string.Join(separator, words.Select(word => Uri.EscapeDataString(word.ToLowerInvariant())));
    }

    // Порядок фільтрів у адресі сталий: однаковий пошук — однакова адреса
    private static IEnumerable<KeyValuePair<string, string>> SortedOptions(SiteSearchRequest request) =>
        (request.Options ?? new Dictionary<string, string>()).OrderBy(option => option.Key, StringComparer.Ordinal);

    private async Task RunAsync(SearchJob job, SiteSearchRequest request)
    {
        SemaphoreSlim gate = _siteGates.GetOrAdd(job.Source, _ => new SemaphoreSlim(1, 1));
        try
        {
            // До одного сайту — один пошук за раз, решта чекає в черзі (state = queued)
            await gate.WaitAsync(_stopping);
            try
            {
                job.Run(PagesFor(request));
                SiteCrawler crawler = new(
                    // Обхід рахує сторінки з 1, а читати треба з fromPage
                    FilteredSiteParser.For(ParserFor(job.Source), page => ListPageUrl(request, page + request.FromPage - 1)),
                    PagesFor(request),
                    _options.RequestDelay,
                    _httpClientFactory,
                    _logger,
                    _time,
                    _browserFetcher,
                    readJobPages: ReadsJobPages(request),
                    // Work.ua і Robota.ua обходимо браузером без robots.txt (рішення команди, 10.11.2026)
                    followRobotsTxt: job.Source is JobSource.Djinni or JobSource.Dou);
                List<RawVacancy> raw = WithRequestedCategory(await crawler.CrawlAsync(_stopping, job.Progress), request);

                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IVacancyIngestionService>().ProcessRawVacanciesAsync(raw, _stopping);

                List<string> ids = raw.Select(item => item.Id?.Trim()).OfType<string>().Where(id => id.Length > 0).Distinct().ToList();
                job.Complete(ids, ids.Count);
                _cache[job.Key] = (_time.GetUtcNow(), ids);
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Пошук на {Source}: знайдено {Count} вакансій.", job.Source, ids.Count);
                }
            }
            finally
            {
                gate.Release();
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            job.Fail("Сервер зупиняється: спробуйте пошук пізніше.");
        }
        catch (Exception ex)
        {
            // Необроблений виняток у фоновому завданні загубився б, а клієнт чекав би вічно
            _logger.LogError(ex, "Пошук на {Source} не вдався.", job.Source);
            job.Fail("Не вдалося прочитати сайт. Спробуйте пізніше.");
        }
    }

    private async Task<IReadOnlyList<VacancyDto>> LoadVacanciesAsync(JobSource source, IReadOnlyList<string> ids, Guid userId, CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IReadOnlyList<VacancyDB> found = await scope.ServiceProvider.GetRequiredService<IVacancyRepository>().GetBySourceIdsAsync(source.ToString(), ids, ct);
        HashSet<Guid> saved = await scope.ServiceProvider.GetRequiredService<ISavedVacancyRepository>()
            .GetSavedVacancyIdsAsync(found.Select(v => v.Id).ToList(), userId, ct);
        // Перегляди — для фільтра «Приховати переглянуті» в застосунку
        HashSet<Guid> viewed = await scope.ServiceProvider.GetRequiredService<IVacancyViewRepository>()
            .GetViewedVacancyIdsAsync(found.Select(v => v.Id).ToList(), userId, ct);

        // Порядок сайту: найновіші першими. Вакансії, які нормалізація відкинула, у базі відсутні й просто випадають
        Dictionary<string, int> order = ids.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index, StringComparer.Ordinal);
        return found
            .OrderBy(v => order.GetValueOrDefault(v.ExternalId ?? string.Empty, int.MaxValue))
            .Select(v => VacancyService.MapToDto(v, saved.Contains(v.Id)) with { IsViewed = viewed.Contains(v.Id) })
            .ToList();
    }

    /// <summary>
    /// Категорія з запиту для вакансій, у яких сайт її не вказав. RSS DOU категорії не містить (вона є лише на сторінці
    /// вакансії, яку пошук не відкриває), але стрічка з category=C++ складається тільки з вакансій цієї категорії.
    /// Категорію, яку дав сам сайт (Djinni — у JSON-LD), не чіпаємо. Кілька категорій у запиті — не вгадуємо, яка чия.
    /// </summary>
    public static List<RawVacancy> WithRequestedCategory(List<RawVacancy> raw, SiteSearchRequest request)
    {
        if (request.Categories is not [string only] || string.IsNullOrWhiteSpace(only))
        {
            return raw;
        }

        return raw
            .Select(item => string.IsNullOrWhiteSpace(item.Category) ? item with { Category = only.Trim() } : item)
            .ToList();
    }

    private static int PagesFor(SiteSearchRequest request) => request.Source == JobSource.Dou ? 1 : request.Pages;

    private static IJobSiteParser ParserFor(JobSource source) => source switch
    {
        JobSource.Djinni => new DjinniParser(),
        JobSource.WorkUa => new WorkUaParser(),
        JobSource.RobotaUa => new RobotaUaParser(),
        _ => new DouParser()
    };

    /// <summary>
    /// У списку Robota.ua лише назва й посилання, а Work.ua — 1–2 рядки опису без навичок, досвіду й графіка:
    /// повні дані є тільки на сторінці вакансії. Для Work.ua користувач може обрати швидкий пошук лише за списком
    /// (<see cref="SiteSearchRequest.ListOnly"/>). Djinni і DOU дають повний опис уже в списку.
    /// </summary>
    internal static bool ReadsJobPages(SiteSearchRequest request) =>
        request.Source == JobSource.RobotaUa || request.Source == JobSource.WorkUa && !request.ListOnly;

    private static string CacheKey(SiteSearchRequest request) => string.Join('|',
        request.Source,
        string.Join(',', (request.Categories ?? []).Select(c => c.Trim()).Order(StringComparer.Ordinal)),
        request.Experience?.Trim(),
        request.City?.Trim().ToLowerInvariant(),
        request.Remote,
        request.Employment,
        request.SalaryFrom,
        request.Keywords?.Trim().ToLowerInvariant(),
        string.Join(',', SortedOptions(request).Select(option => $"{option.Key}={option.Value}")),
        PagesFor(request),
        request.FromPage,
        // Швидкий результат без описів не має підміняти повний (і навпаки)
        ReadsJobPages(request));

    private void ForgetOldJobs(DateTimeOffset now)
    {
        foreach ((Guid id, SearchJob job) in _jobs)
        {
            if (!job.IsActive && now - job.CreatedAt > JobLifetime)
            {
                _jobs.TryRemove(id, out _);
            }
        }
    }

    /// <summary>Один пошук: стан змінює фонове завдання, читає контролер, тож усе під блокуванням.</summary>
    private sealed class SearchJob(Guid userId, JobSource source, string key, int pages, DateTimeOffset createdAt)
    {
        private readonly object _lock = new();
        private string _state = SiteSearchStates.Queued;
        private int _pagesDone;
        private int _pagesTotal = pages;
        private int _found;
        private string? _message;

        public Guid Id { get; } = Guid.NewGuid();
        public Guid UserId { get; } = userId;
        public JobSource Source { get; } = source;
        public string Key { get; } = key;
        public DateTimeOffset CreatedAt { get; } = createdAt;
        public bool FromCache { get; init; }
        public IReadOnlyList<string> ExternalIds { get; private set; } = [];

        public bool IsActive
        {
            get
            {
                lock (_lock)
                {
                    return _state is SiteSearchStates.Queued or SiteSearchStates.Running;
                }
            }
        }

        public void Run(int pagesTotal)
        {
            lock (_lock)
            {
                _state = SiteSearchStates.Running;
                _pagesTotal = pagesTotal;
            }
        }

        public void Progress(int pagesDone, int found)
        {
            lock (_lock)
            {
                _pagesDone = pagesDone;
                _found = found;
            }
        }

        public void Complete(IReadOnlyList<string> ids, int found)
        {
            lock (_lock)
            {
                ExternalIds = ids;
                _found = found;
                _pagesDone = _pagesTotal;
                _state = SiteSearchStates.Completed;
                _message = found == 0 ? "За цими фільтрами на сайті нічого не знайдено або сайт зараз не відповідає." : null;
            }
        }

        public void Fail(string message)
        {
            lock (_lock)
            {
                _state = SiteSearchStates.Failed;
                _message = message;
            }
        }

        public SiteSearchStatusDto Snapshot()
        {
            lock (_lock)
            {
                return new SiteSearchStatusDto
                {
                    Id = Id,
                    State = _state,
                    PagesDone = _pagesDone,
                    PagesTotal = _pagesTotal,
                    Found = _found,
                    FromCache = FromCache,
                    Message = _message
                };
            }
        }
    }
}

/// <summary>Розбір сторінок — як у звичайного парсера сайту, а адреси списку — з фільтрами пошуку.</summary>
public class FilteredSiteParser(IJobSiteParser inner, Func<int, string?> listPageUrl) : IJobSiteParser
{
    /// <summary>Обгортка зберігає позначку <see cref="IBrowserOnlySite"/>: інакше Work.ua чи Robota.ua пішли б звичайним HTTP, а не браузером.</summary>
    public static FilteredSiteParser For(IJobSiteParser inner, Func<int, string?> listPageUrl) =>
        inner is IBrowserOnlySite ? new BrowserOnly(inner, listPageUrl) : new FilteredSiteParser(inner, listPageUrl);

    public string Name => inner.Name;

    public Uri Home => inner.Home;

    public string? ListPageUrl(int page) => listPageUrl(page);

    public List<RawVacancy> ParseListPage(string html) => inner.ParseListPage(html);

    public RawVacancy ParseJobPage(string html) => inner.ParseJobPage(html);

    private sealed class BrowserOnly(IJobSiteParser inner, Func<int, string?> listPageUrl)
        : FilteredSiteParser(inner, listPageUrl), IBrowserOnlySite;
}
