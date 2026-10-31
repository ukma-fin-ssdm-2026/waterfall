// AI: нормалізація сирих вакансій і збереження в БД (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests, ручний запуск API, самоперевірка автора (§8)

using System.Globalization;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services;

/// <summary>
/// Чистить і стандартизує сирі вакансії (назва, опис, зарплата, дата, навички)
/// та зберігає їх через <see cref="IVacancyRepository.BulkUpsertAsync"/>.
/// Збираємо з трьох сайтів — Djinni, Work.ua і DOU.
/// </summary>
public class VacancyIngestionService : IVacancyIngestionService
{
    // Для кожного сайту: домен, на який мають вести посилання (THR-03), і валюта зарплати, якщо сайт її не вказав
    private static readonly Dictionary<JobSource, (string Host, string Currency)> KnownSources = new()
    {
        [JobSource.Djinni] = ("djinni.co", "USD"), // Djinni публікує зарплати в доларах
        [JobSource.WorkUa] = ("work.ua", "UAH"),
        [JobSource.Dou] = ("dou.ua", "USD")        // на DOU зарплати теж здебільшого в доларах
    };

    // Більше значення в «N … тому» не буває реальною датою публікації. До того ж, навіть 10 000 місяців тому ще не виводять дату за межі календаря.
    private const int MaxTimeAgo = 10_000;

    // Тайм-аут захищає від зависання регулярного виразу на шкідливому вводі (ReDoS)
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly Regex ScriptOrStyle = new(@"<(script|style)\b.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
    private static readonly Regex LineBreakTag = new(@"<(br|/p|/li|/div|/h\d)\b[^>]*>", RegexOptions.IgnoreCase, RegexTimeout);
    private static readonly Regex AnyTag = new(@"<[^>]*>", RegexOptions.None, RegexTimeout);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.None, RegexTimeout);
    private static readonly Regex Number = new(@"\d+", RegexOptions.None, RegexTimeout);
    private static readonly Regex TimeAgo = new(@"(\d+)\s*(хв|год|дн|ден|тиж|міс)", RegexOptions.IgnoreCase, RegexTimeout);

    // Кирилицю й «+» лишаємо як є: інакше «Комунікація» чи «C++» лягли б у БД як К… і їх не знаходив би пошук каталогу,
    // що шукає підрядок у SkillsJson. Це безпечно, бо цей JSON не вставляється в HTML: API віддає навички окремим масивом.
    private static readonly JsonSerializerOptions SkillsJsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly IVacancyRepository _repository;
    private readonly ILogger<VacancyIngestionService> _logger;

    public VacancyIngestionService(IVacancyRepository repository, ILogger<VacancyIngestionService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<IngestionResult> ProcessRawVacanciesAsync(IReadOnlyList<RawVacancy> rawItems, CancellationToken ct = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<VacancyDB> vacancies = [];

        foreach (RawVacancy raw in rawItems)
        {
            (VacancyDB? vacancy, string? error) = Normalize(raw, now);
            if (vacancy is null)
            {
                _logger.LogWarning("Вакансію {ExternalId} пропущено: {Reason}.", raw.Id, error);
                continue;
            }

            vacancies.Add(vacancy);
        }

        (int created, int updated) = await _repository.BulkUpsertAsync(vacancies, ct);
        IngestionResult result = new(created, updated, rawItems.Count - vacancies.Count);

        _logger.LogInformation(
            "Збережено вакансії: нових {Created}, оновлених {Updated}, пропущено {Skipped}.",
            result.Created, result.Updated, result.Skipped);
        return result;
    }

    /// <summary>
    /// Перетворює сиру вакансію на запис БД. Якщо вакансія непридатна (немає назви, опису
    /// чи посилання веде не на сайт-джерело), повертає null і причину.
    /// </summary>
    public static (VacancyDB? Vacancy, string? Error) Normalize(RawVacancy raw, DateTimeOffset now)
    {
        if (!KnownSources.TryGetValue(raw.Source, out (string Host, string Currency) source))
        {
            return (null, $"невідоме джерело {raw.Source}");
        }

        if (string.IsNullOrWhiteSpace(raw.Id))
        {
            return (null, "немає id вакансії");
        }

        string title = CleanText(raw.Title);
        // 2 символи — мінімум: є справжні назви посад на кшталт QA, PM чи UX, а «-» чи «.» — сміття
        if (title.Length < 2)
        {
            return (null, "немає назви посади");
        }

        string description = CleanText(raw.DescriptionHtml);
        if (description.Length == 0)
        {
            return (null, "немає опису");
        }

        // Фішингове посилання не має потрапити в каталог під виглядом вакансії з Djinni чи Work.ua (THR-03)
        string? url = NormalizeUrl(raw.Url, source.Host);
        if (url is null)
        {
            return (null, $"посилання веде не на {source.Host}");
        }

        (decimal? salaryMin, decimal? salaryMax, string currency) = ParseSalary(raw.Salary, source.Currency);

        string? fullAddress = !string.IsNullOrWhiteSpace(raw.FullAddress)
            ? Cut(CleanText(raw.FullAddress), 300)
            : ResolveFullAddress(raw.Location);

        VacancyDB vacancy = new()
        {
            Id = Guid.NewGuid(),
            Source = raw.Source.ToString(),
            ExternalId = Cut(raw.Id.Trim(), 100),
            SourceUrl = url,
            Title = Cut(title, 200),
            Company = Cut(OrDefault(CleanText(raw.Company), "Не вказано"), 200),
            Description = description,
            Category = Cut(OrDefault(CleanText(raw.Category), "Інше"), 100),
            Location = Cut(OrDefault(CleanText(raw.Location), "Не вказано"), 200),
            FullAddress = fullAddress,
            Schedule = NullIfEmpty(Cut(CleanText(raw.Employment), 100)),
            Experience = NullIfEmpty(Cut(CleanText(raw.Experience), 100)),
            SalaryMin = salaryMin,
            SalaryMax = salaryMax,
            Currency = currency,
            // Навички — лише ті, що вказав роботодавець; якщо поля немає, список порожній
            SkillsJson = JsonSerializer.Serialize(ParseSkills(raw.Tags), SkillsJsonOptions),
            PublishedAt = ParsePublishedAt(raw.Posted, now),
            FirstSeenAt = now,
            LastSeenAt = now,
            IsActive = true
        };

        return (vacancy, null);
    }

    /// <summary>
    /// «$3500-4500» → 3500–4500 USD, «до 2500» → лише максимум, «від 2000» → лише мінімум.
    /// Погодинні ставки й «за домовленістю» дають порожню зарплату.
    /// </summary>
    /// <param name="salary">Зарплата так, як її написано на сайті.</param>
    /// <param name="defaultCurrency">Валюта, якщо в рядку її не видно: у Djinni це долари, у Work.ua — гривні.</param>
    public static (decimal? Min, decimal? Max, string Currency) ParseSalary(string? salary, string defaultCurrency = "USD")
    {
        string text = (salary ?? string.Empty).ToLowerInvariant();
        string currency = DetectCurrency(text, defaultCurrency);

        // Погодинну ставку не можна порівнювати з місячними зарплатами в каталозі
        if (text.Contains("/год") || text.Contains("/hour"))
        {
            return (null, null, currency);
        }

        // Пробіли прибираємо, щоб «45 000» читалося як одне число
        List<decimal> numbers = Number.Matches(Spaces.Replace(text, string.Empty))
            .Select(match => decimal.Parse(match.Value, CultureInfo.InvariantCulture))
            .ToList();

        if (numbers.Count == 0)
        {
            return (null, null, currency);
        }

        if (numbers.Count >= 2)
        {
            // Вилку записуємо від меншого до більшого, навіть якщо на сайті її перевернули
            return (Math.Min(numbers[0], numbers[1]), Math.Max(numbers[0], numbers[1]), currency);
        }

        decimal amount = numbers[0];
        if (text.Contains("до") || text.Contains("up to"))
        {
            return (null, amount, currency);
        }

        if (text.Contains("від") || text.Contains("from"))
        {
            return (amount, null, currency);
        }

        return (amount, amount, currency);
    }

    /// <summary>Валюта за позначкою в рядку зарплати (уже в нижньому регістрі); якщо позначки немає — типова для сайту.</summary>
    private static string DetectCurrency(string text, string defaultCurrency) =>
        text.Contains('$') || text.Contains("usd") ? "USD" :
        text.Contains('€') || text.Contains("eur") ? "EUR" :
        text.Contains('₴') || text.Contains("грн") || text.Contains("uah") ? "UAH" :
        defaultCurrency;

    /// <summary>
    /// «сьогодні», «вчора», «3 дні тому», «5 годин тому», «10 хв. тому», «08.10.2026» або ISO 8601.
    /// Невідомий формат вважаємо сьогоднішньою датою, дату з майбутнього — теж.
    /// </summary>
    public static DateTimeOffset ParsePublishedAt(string? posted, DateTimeOffset now)
    {
        string text = (posted ?? string.Empty).Trim().ToLowerInvariant();

        if (text.Contains("сьогодні") || text.Contains("щойно"))
        {
            return now;
        }

        if (text.Contains("вчора"))
        {
            return now.AddDays(-1);
        }

        Match timeAgo = TimeAgo.Match(text);
        if (timeAgo.Success)
        {
            // Абсурдно велике число — помилка на сайті, а не дата: як і з незрозумілим форматом, беремо момент збору
            if (!int.TryParse(timeAgo.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value > MaxTimeAgo)
            {
                return now;
            }

            return SubtractTimeAgo(now, timeAgo.Groups[2].Value, value);
        }

        // dd.MM.yyyy перевіряємо першим: інакше «08.10.2026» прочиталося б як 10 серпня
        if (DateTimeOffset.TryParseExact(text, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset date)
            || DateTimeOffset.TryParse(posted, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date))
        {
            // Зберігаємо в UTC: БД (DateTimeOffsetToBinaryConverter) сортує за місцевим часом, тож «20:43 +03:00» стала б пізнішою за «18:00 UTC»
            return date > now ? now : date.ToUniversalTime();
        }

        return now;
    }


    /// <summary>«3 дні тому» → відняти від моменту збору 3 дні. Одиниця — початок слова, знайдений regex'ом TimeAgo.</summary>
    private static DateTimeOffset SubtractTimeAgo(DateTimeOffset now, string unit, int value)
    {
        return unit switch
        {
            "хв" => now.AddMinutes(-value),
            "год" => now.AddHours(-value),
            "дн" or "ден" => now.AddDays(-value),
            "тиж" => now.AddDays(-7 * value),
            "міс" => now.AddMonths(-value),
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Невідома одиниця часу")
        };
    }

    /// <summary>
    /// HTML → звичайний текст: script і style видаляються разом із вмістом,
    /// абзаци й пункти списків стають окремими рядками, зайві пробіли зникають.
    /// </summary>
    public static string CleanText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        string text = ScriptOrStyle.Replace(html, string.Empty);
        text = LineBreakTag.Replace(text, "\n");
        text = AnyTag.Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);

        IEnumerable<string> lines = text
            .Split('\n')
            .Select(line => Spaces.Replace(line, " ").Trim())
            .Where(line => line.Length > 0);
        return string.Join("\n", lines);
    }

    /// <summary>Приймає лише https-посилання на сайт-джерело і відкидає з нього трекінгові параметри.</summary>
    private static string? NormalizeUrl(string? url, string sourceHost)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        bool fromSource = uri.Host == sourceHost || uri.Host.EndsWith("." + sourceHost, StringComparison.Ordinal);
        return fromSource ? Cut(uri.GetLeftPart(UriPartial.Path), 1000) : null;
    }

    private static List<string> ParseSkills(string? tags) =>
        (tags ?? string.Empty)
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToList();

    private static string OrDefault(string value, string fallback) => value.Length > 0 ? value : fallback;

    private static string? NullIfEmpty(string value) => value.Length > 0 ? value : null;

    // Обрізаємо до довжини колонки в БД (MaxLength у VacancyDB)
    private static string Cut(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private static string? ResolveFullAddress(string? location)
    {
        string raw = (location ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Не вказано точну адресу";
        }

        string loc = raw.ToLowerInvariant();
        if (loc.Contains("lviv") || loc.Contains("львів"))
        {
            return "Львів, не вказано точну адресу";
        }
        if (loc.Contains("dnipro") || loc.Contains("дніпро"))
        {
            return "Дніпро, не вказано точну адресу";
        }
        if (loc.Contains("odesa") || loc.Contains("одеса"))
        {
            return "Одеса, не вказано точну адресу";
        }
        if (loc.Contains("kharkiv") || loc.Contains("харків"))
        {
            return "Харків, не вказано точну адресу";
        }
        if (loc.Contains("kyiv") || loc.Contains("київ"))
        {
            return "Київ, не вказано точну адресу";
        }
        if (loc.Contains("remote") || loc.Contains("віддалено"))
        {
            return "Віддалено, не вказано точну адресу";
        }

        string firstPart = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)[0];
        return $"{firstPart}, не вказано точну адресу";
    }
}
