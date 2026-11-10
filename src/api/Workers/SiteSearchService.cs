// AI: пошук на сайтах на запит користувача: адреси з фільтрами, черга за сайтами, прогрес, кеш і ліміти (issue #62); перевірено: юніт-тести SiteSearchServiceTests і SiteSearchUrlsTests, ручний запуск проти djinni.co і jobs.dou.ua, самоперевірка автора (§8)

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
/// Пошук на запит (issue #62): користувач обрав сайт і фільтри — сервер читає лише сторінки списку з цими фільтрами
/// тим самим ввічливим <see cref="SiteCrawler"/> (robots.txt, пауза між запитами), зберігає вакансії через
/// <see cref="IVacancyIngestionService"/> і віддає їх клієнту. Поки йде обхід, клієнт бачить прогрес.
/// До одного сайту одночасно йде один пошук; такий самий пошук протягом <see cref="CacheLifetime"/> віддається з бази.
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
        TimeProvider? time = null)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
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
        _ = Task.Run(() => RunAsync(job, request));
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

    /// <summary>Адреса сторінки списку з фільтрами: Djinni — сторінка пошуку, DOU — офіційна RSS-стрічка (25 найсвіжіших).</summary>
    public static string? ListPageUrl(SiteSearchRequest request, int page)
    {
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

            Add("category", request.Categories?.FirstOrDefault());
            Add("exp", request.Experience);
            Add("city", request.City);
            Add("remote", request.Remote ? "1" : null);
            Add("search", request.Keywords);
            url = "https://jobs.dou.ua/vacancies/feeds/";
        }

        return query.Count == 0
            ? url
            : url + "?" + string.Join("&", query.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
    }

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
                IJobSiteParser site = new FilteredSiteParser(job.Source == JobSource.Djinni ? new DjinniParser() : new DouParser(), page => ListPageUrl(request, page));
                SiteCrawler crawler = new(site, PagesFor(request), _options.RequestDelay, _httpClientFactory, _logger, _time, readJobPages: false);
                List<RawVacancy> raw = await crawler.CrawlAsync(_stopping, job.Progress);

                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IVacancyIngestionService>().ProcessRawVacanciesAsync(raw, _stopping);

                List<string> ids = raw.Select(item => item.Id?.Trim()).OfType<string>().Where(id => id.Length > 0).Distinct().ToList();
                job.Complete(ids, ids.Count);
                _cache[job.Key] = (_time.GetUtcNow(), ids);
                _logger.LogInformation("Пошук на {Source}: знайдено {Count} вакансій.", job.Source, ids.Count);
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

        // Порядок сайту: найновіші першими. Вакансії, які нормалізація відкинула, у базі відсутні й просто випадають
        Dictionary<string, int> order = ids.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index, StringComparer.Ordinal);
        return found
            .OrderBy(v => order.GetValueOrDefault(v.ExternalId ?? string.Empty, int.MaxValue))
            .Select(v => VacancyService.MapToDto(v, saved.Contains(v.Id)))
            .ToList();
    }

    private static int PagesFor(SiteSearchRequest request) => request.Source == JobSource.Dou ? 1 : request.Pages;

    private static string CacheKey(SiteSearchRequest request) => string.Join('|',
        request.Source,
        string.Join(',', (request.Categories ?? []).Select(c => c.Trim()).Order(StringComparer.Ordinal)),
        request.Experience?.Trim(),
        request.City?.Trim().ToLowerInvariant(),
        request.Remote,
        request.Employment,
        request.SalaryFrom,
        request.Keywords?.Trim().ToLowerInvariant(),
        PagesFor(request));

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
public sealed class FilteredSiteParser(IJobSiteParser inner, Func<int, string?> listPageUrl) : IJobSiteParser
{
    public string Name => inner.Name;

    public Uri Home => inner.Home;

    public string? ListPageUrl(int page) => listPageUrl(page);

    public List<RawVacancy> ParseListPage(string html) => inner.ParseListPage(html);

    public RawVacancy ParseJobPage(string html) => inner.ParseJobPage(html);
}
