// AI: нормалізація сирих вакансій до єдиного формату (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests, самоперевірка автора (§8)

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Waterfall.Services.Ingestion.Normalization;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Ingestion;

/// <summary>
/// Перетворює сирий запис джерела на <see cref="NormalizedVacancy"/>: перевіряє обов'язкові поля,
/// зводить зарплату, дату, локацію, навички й досвід до єдиного формату та обрізає поля до меж схеми БД.
/// </summary>
public sealed partial class VacancyNormalizer
{
    public const string NotSpecified = "Не вказано";
    public const string OtherCategory = "Інше";

    // Межі полів збігаються з атрибутами сутності VacancyDB
    private const int MaxTitleLength = 200;
    private const int MaxCompanyLength = 200;
    private const int MaxCategoryLength = 100;
    private const int MaxScheduleLength = 100;
    private const int MaxExperienceLength = 100;
    private const int MaxExternalIdLength = 100;
    private const int MaxUrlLength = 1000;
    private const int MaxDescriptionLength = 10_000;
    private const int RegexTimeoutMs = 500;

    private static readonly Dictionary<string, JobSource> SourceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["djinni"] = JobSource.Djinni,
        ["work.ua"] = JobSource.WorkUa,
        ["workua"] = JobSource.WorkUa,
        ["robota.ua"] = JobSource.RobotaUa,
        ["robotaua"] = JobSource.RobotaUa,
        ["dou"] = JobSource.Dou
    };

    // Посилання на вакансію приймається лише з домену її джерела (контрзахід THR-03 з threat-model.md)
    private static readonly Dictionary<JobSource, string[]> AllowedHosts = new()
    {
        [JobSource.Djinni] = ["djinni.co"],
        [JobSource.WorkUa] = ["work.ua"],
        [JobSource.RobotaUa] = ["robota.ua"],
        [JobSource.Dou] = ["dou.ua"]
    };

    public NormalizationResult Normalize(RawVacancy raw, DateTimeOffset now)
    {
        if (!SourceNames.TryGetValue(raw.Source, out JobSource source))
        {
            return NormalizationResult.Invalid($"невідоме джерело «{raw.Source}»");
        }

        string? title = TextNormalizer.CleanLine(raw.Title);
        if (title is null || title.Length < 3)
        {
            return NormalizationResult.Invalid("немає назви посади або вона коротша за 3 символи");
        }

        if (!TryNormalizeUrl(raw.Url, source, out string? url, out string? urlError))
        {
            return NormalizationResult.Invalid(urlError);
        }

        string? externalId = TextNormalizer.CleanLine(raw.ExternalId) ?? ExtractIdFromUrl(url);
        if (externalId is null)
        {
            return NormalizationResult.Invalid("немає ідентифікатора вакансії на сайті-джерелі");
        }

        if (externalId.Length > MaxExternalIdLength)
        {
            return NormalizationResult.Invalid($"ідентифікатор довший за {MaxExternalIdLength} символів");
        }

        string? description = TextNormalizer.HtmlToPlainText(raw.DescriptionHtml);
        if (description is null)
        {
            return NormalizationResult.Invalid("порожній опис вакансії");
        }

        IReadOnlyList<string> skills = SkillsNormalizer.Normalize(raw.Tags);
        SalaryInfo salary = SalaryParser.Parse(raw.Salary, DefaultCurrency(source));

        // Дата з майбутнього — помилка джерела або розбору, а не реальна публікація
        DateTimeOffset publishedAt = PublishedDateParser.Parse(raw.Posted, now) ?? now;
        if (publishedAt > now)
        {
            publishedAt = now;
        }

        string? schedule = TextNormalizer.CleanLine(raw.Employment);

        return NormalizationResult.Valid(new NormalizedVacancy
        {
            Source = source,
            ExternalId = externalId,
            SourceUrl = url,
            Title = TextNormalizer.Truncate(title, MaxTitleLength),
            Company = TextNormalizer.Truncate(TextNormalizer.CleanLine(raw.Company) ?? NotSpecified, MaxCompanyLength),
            Description = TextNormalizer.Truncate(description, MaxDescriptionLength),
            Category = TextNormalizer.Truncate(TextNormalizer.CleanLine(raw.Category) ?? InferCategory(title, skills), MaxCategoryLength),
            Location = LocationNormalizer.Normalize(raw.Location),
            Schedule = schedule is null ? null : TextNormalizer.Truncate(schedule, MaxScheduleLength),
            Experience = NormalizeExperience(raw.Experience),
            SalaryMin = salary.Min,
            SalaryMax = salary.Max,
            Currency = salary.Currency,
            Skills = skills,
            PublishedAt = publishedAt
        });
    }

    private static bool TryNormalizeUrl(
        string? raw,
        JobSource source,
        [NotNullWhen(true)] out string? url,
        [NotNullWhen(false)] out string? error)
    {
        url = null;
        string? text = TextNormalizer.CleanLine(raw);

        if (text is null
            || !Uri.TryCreate(text, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            error = "посилання на вакансію відсутнє або некоректне";
            return false;
        }

        // Домен перевіряємо першим: у https://djinni.co@evil.example справжній хост — evil.example
        string host = uri.IdnHost.ToLowerInvariant();
        string[] allowed = AllowedHosts[source];
        if (!allowed.Any(domain => host == domain || host.EndsWith("." + domain, StringComparison.Ordinal)))
        {
            error = $"посилання веде на сторонній домен {host}; для {source} дозволено лише {string.Join(", ", allowed)}";
            return false;
        }

        // Логін у посиланні чи нестандартний порт — типові прийоми маскування фішингових адрес
        if (!string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
        {
            error = "посилання містить облікові дані або нестандартний порт";
            return false;
        }

        // Параметри запиту й фрагмент відкидаємо: це трекінг (utm_*), а не адреса вакансії
        string normalized = uri.GetLeftPart(UriPartial.Path);
        if (normalized.Length > MaxUrlLength)
        {
            error = $"посилання довше за {MaxUrlLength} символів";
            return false;
        }

        url = normalized;
        error = null;
        return true;
    }

    private static string? ExtractIdFromUrl(string url)
    {
        // https://djinni.co/jobs/720001-senior-net-developer/ → 720001
        string? lastSegment = new Uri(url).Segments
            .Select(segment => segment.Trim('/'))
            .LastOrDefault(segment => segment.Length > 0);

        if (lastSegment is null)
        {
            return null;
        }

        Match match = LeadingDigitsRegex().Match(lastSegment);
        return match.Success ? match.Value : null;
    }

    private static string DefaultCurrency(JobSource source) =>
        source is JobSource.Djinni or JobSource.Dou ? "USD" : "UAH";

    private static string InferCategory(string title, IReadOnlyList<string> skills)
    {
        string titleText = title.ToLowerInvariant();
        string allText = (title + " " + string.Join(" ", skills)).ToLowerInvariant();

        if (QaRegex().IsMatch(titleText))
        {
            return "QA";
        }

        if (DevOpsRegex().IsMatch(titleText))
        {
            return "DevOps";
        }

        if (allText.Contains("c#") || allText.Contains(".net"))
        {
            return ".NET";
        }

        if (allText.Contains("python"))
        {
            return "Python";
        }

        if (JavaRegex().IsMatch(allText))
        {
            return "Java";
        }

        if (FrontendRegex().IsMatch(allText))
        {
            return "Frontend";
        }

        if (MobileRegex().IsMatch(allText))
        {
            return "Mobile";
        }

        return OtherCategory;
    }

    private static string? NormalizeExperience(string? raw)
    {
        string? text = TextNormalizer.CleanLine(raw);
        if (text is null)
        {
            return null;
        }

        string lower = text.ToLowerInvariant();
        if (NoExperienceRegex().IsMatch(lower))
        {
            return "Без досвіду";
        }

        Match years = YearsRegex().Match(lower);
        if (years.Success)
        {
            int count = int.Parse(years.Groups["years"].Value, CultureInfo.InvariantCulture);
            return $"{count}+ {YearsWord(count)}";
        }

        return TextNormalizer.Truncate(text, MaxExperienceLength);
    }

    private static string YearsWord(int count)
    {
        int lastDigit = count % 10;
        int lastTwoDigits = count % 100;

        if (lastDigit == 1 && lastTwoDigits != 11)
        {
            return "рік";
        }

        if (lastDigit is >= 2 and <= 4 && (lastTwoDigits is < 12 or > 14))
        {
            return "роки";
        }

        return "років";
    }

    [GeneratedRegex(@"^\d+", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex LeadingDigitsRegex();

    [GeneratedRegex(@"(?<!\p{L})(?:qa|aqa|tester|тестувальник)(?!\p{L})|(?<!\p{L})test", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex QaRegex();

    [GeneratedRegex(@"(?<!\p{L})(?:devops|sre|platform engineer)(?!\p{L})", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex DevOpsRegex();

    [GeneratedRegex(@"(?<!\p{L})java(?!\p{L})", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex JavaRegex();

    [GeneratedRegex(@"(?<!\p{L})(?:frontend|front-end|react|angular|vue)(?!\p{L})", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex FrontendRegex();

    [GeneratedRegex(@"(?<!\p{L})(?:android|ios|flutter|kotlin|swift)(?!\p{L})", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex MobileRegex();

    [GeneratedRegex(@"без\s+досвіду|no\s+experience", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex NoExperienceRegex();

    [GeneratedRegex(@"(?<years>\d{1,2})\s*\+?\s*(?:рік|року|роки|років|year|years|yrs)(?!\p{L})", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex YearsRegex();
}
