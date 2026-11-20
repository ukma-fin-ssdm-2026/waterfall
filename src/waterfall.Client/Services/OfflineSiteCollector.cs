// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.SiteSearch;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Repository.Normalization;

namespace Waterfall.Client.Services;

/// <summary>
/// Офлайн-збирач вакансій (без підключення до сервера Waterfall API):
/// підтримує прямий збір із DOU RSS та Djinni HTML/JSON-LD через HttpClient,
/// щоб клієнт міг збирати вакансії автономно й синхронізувати їх із сервером після відновлення зв'язку.
/// </summary>
public sealed partial class OfflineSiteCollector : IOfflineSiteCollector
{
    private readonly HttpClient _http;

    public OfflineSiteCollector(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _http.DefaultRequestHeaders.Add("User-Agent", "WaterfallOfflineCollector/1.0 (+https://github.com/ukma-fin-ssdm-2026/waterfall)");
        }
    }

    public bool Supports(JobSource source) => source is JobSource.Dou or JobSource.Djinni;

    public async Task<IReadOnlyList<VacancyDto>> CollectAsync(IReadOnlyList<SiteSearchRequest> requests, CancellationToken ct = default)
    {
        List<VacancyDto> collected = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (SiteSearchRequest request in requests.Where(r => Supports(r.Source)))
        {
            IReadOnlyList<VacancyDto> items = request.Source == JobSource.Dou
                ? await CollectDouAsync(request, ct)
                : await CollectDjinniAsync(request, ct);

            foreach (VacancyDto item in items)
            {
                string key = $"{item.Source}:{item.ExternalId ?? item.SourceUrl}";
                if (seen.Add(key))
                {
                    collected.Add(item);
                }
            }
        }

        return collected;
    }

    private async Task<IReadOnlyList<VacancyDto>> CollectDouAsync(SiteSearchRequest request, CancellationToken ct)
    {
        List<string> query = [];
        if (request.Categories is { Count: > 0 } cats && !string.IsNullOrWhiteSpace(cats[0]))
        {
            query.Add($"category={Uri.EscapeDataString(cats[0].Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(request.Experience))
        {
            query.Add($"exp={Uri.EscapeDataString(request.Experience.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(request.City))
        {
            query.Add($"city={Uri.EscapeDataString(request.City.Trim())}");
        }

        if (request.Remote)
        {
            query.Add("remote=1");
        }

        if (!string.IsNullOrWhiteSpace(request.Keywords))
        {
            query.Add($"search={Uri.EscapeDataString(request.Keywords.Trim())}");
        }

        string url = "https://jobs.dou.ua/vacancies/feeds/" + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty);
        string xml = await _http.GetStringAsync(url, ct);
        string fallbackCategory = request.Categories is [string only] && !string.IsNullOrWhiteSpace(only) ? only.Trim() : "Інше";
        return ParseDouRss(xml, fallbackCategory);
    }

    private async Task<IReadOnlyList<VacancyDto>> CollectDjinniAsync(SiteSearchRequest request, CancellationToken ct)
    {
        List<VacancyDto> results = [];
        int pages = Math.Clamp(request.Pages, 1, 5);
        int fromPage = Math.Clamp(request.FromPage, 1, 50);

        for (int i = 0; i < pages; i++)
        {
            int pageNum = fromPage + i;
            List<string> query = [];
            foreach (string cat in request.Categories ?? [])
            {
                if (!string.IsNullOrWhiteSpace(cat))
                {
                    query.Add($"primary_keyword={Uri.EscapeDataString(cat.Trim())}");
                }
            }

            if (!string.IsNullOrWhiteSpace(request.Experience)) query.Add($"exp_level={Uri.EscapeDataString(request.Experience.Trim())}");
            if (!string.IsNullOrWhiteSpace(request.City)) query.Add($"location={Uri.EscapeDataString(request.City.Trim())}");
            string? emp = request.Remote ? "remote" : request.Employment;
            if (!string.IsNullOrWhiteSpace(emp)) query.Add($"employment={Uri.EscapeDataString(emp.Trim())}");
            if (request.SalaryFrom is > 0) query.Add($"salary={request.SalaryFrom.Value.ToString(CultureInfo.InvariantCulture)}");
            if (!string.IsNullOrWhiteSpace(request.Keywords)) query.Add($"all_keywords={Uri.EscapeDataString(request.Keywords.Trim())}");
            foreach (KeyValuePair<string, string> opt in request.Options ?? [])
            {
                if (!string.IsNullOrWhiteSpace(opt.Key) && !string.IsNullOrWhiteSpace(opt.Value))
                {
                    query.Add($"{Uri.EscapeDataString(opt.Key)}={Uri.EscapeDataString(opt.Value)}");
                }
            }

            if (pageNum > 1) query.Add($"page={pageNum.ToString(CultureInfo.InvariantCulture)}");

            string url = "https://djinni.co/jobs/" + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty);
            string html = await _http.GetStringAsync(url, ct);
            IReadOnlyList<VacancyDto> pageItems = ParseDjinniJsonLd(html);
            if (pageItems.Count == 0)
            {
                break;
            }

            results.AddRange(pageItems);
        }

        return results;
    }

    public static IReadOnlyList<VacancyDto> ParseDouRss(string xml, string fallbackCategory = "Інше")
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return [];
        }

        List<VacancyDto> items = [];
        DateTimeOffset now = DateTimeOffset.UtcNow;
        XDocument doc = XDocument.Parse(xml);

        foreach (XElement item in doc.Descendants("item"))
        {
            string rawTitle = item.Element("title")?.Value?.Trim() ?? string.Empty;
            string link = item.Element("link")?.Value?.Trim() ?? string.Empty;
            string description = StripHtml(item.Element("description")?.Value);
            string pubDateText = item.Element("pubDate")?.Value?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(rawTitle) || string.IsNullOrWhiteSpace(link))
            {
                continue;
            }

            // Заголовок DOU RSS має вигляд «Посада в Компанія, Місто»
            string title = rawTitle;
            string company = "Не вказано";
            string location = "Україна";

            int inIndex = rawTitle.LastIndexOf(" в ", StringComparison.Ordinal);
            if (inIndex > 0)
            {
                title = rawTitle[..inIndex].Trim();
                string rest = rawTitle[(inIndex + 3)..].Trim();
                int comma = rest.IndexOf(',');
                if (comma > 0)
                {
                    company = rest[..comma].Trim();
                    location = rest[(comma + 1)..].Trim();
                }
                else
                {
                    company = rest;
                }
            }

            string? externalId = DouVacancyIdRegex().Match(link) is { Success: true } match ? match.Groups[1].Value : link;
            DateTimeOffset published = DateTimeOffset.TryParse(pubDateText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
                ? parsed.ToUniversalTime()
                : now;

            items.Add(new VacancyDto
            {
                Id = Guid.NewGuid(),
                ExternalId = externalId,
                Title = title,
                Company = company,
                Description = string.IsNullOrWhiteSpace(description) ? title : description,
                Category = fallbackCategory,
                CategoryGroup = VacancyCategoryMap.Resolve(fallbackCategory),
                Location = location,
                WorkFormat = VacancyNormalizer.ResolveWorkFormat(location, null),
                ExperienceMonths = VacancyExperienceParser.ParseMonths(null, description),
                Currency = "USD",
                Source = JobSource.Dou,
                SourceUrl = link,
                PublishedAt = published,
                FirstSeenAt = now,
                LastSeenAt = now,
                IsActive = true
            });
        }

        return items;
    }

    public static IReadOnlyList<VacancyDto> ParseDjinniJsonLd(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        List<VacancyDto> results = [];
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (Match match in JsonLdRegex().Matches(html))
        {
            string json = match.Groups[1].Value.Trim();
            if (!json.Contains("JobPosting", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement el in root.EnumerateArray())
                    {
                        TryAddDjinniPosting(el, now, results);
                    }
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    TryAddDjinniPosting(root, now, results);
                }
            }
            catch (JsonException)
            {
                // Пропускаємо пошкоджений JSON-LD блок
            }
        }

        return results;
    }

    private static void TryAddDjinniPosting(JsonElement el, DateTimeOffset now, List<VacancyDto> results)
    {
        if (!el.TryGetProperty("@type", out JsonElement typeEl) ||
            !string.Equals(typeEl.GetString(), "JobPosting", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string title = GetString(el, "title") ?? string.Empty;
        string url = GetString(el, "url") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        string company = el.TryGetProperty("hiringOrganization", out JsonElement org)
            ? GetString(org, "name") ?? "Не вказано"
            : "Не вказано";
        string description = StripHtml(GetString(el, "description"));
        string category = GetString(el, "occupationalCategory") ?? "Інше";
        string? externalId = el.TryGetProperty("identifier", out JsonElement idEl)
            ? (idEl.ValueKind == JsonValueKind.Object ? GetString(idEl, "value") : idEl.ToString())
            : null;

        string location = string.Equals(GetString(el, "jobLocationType"), "TELECOMMUTE", StringComparison.OrdinalIgnoreCase)
            ? "Віддалено"
            : "Україна";

        DateTimeOffset published = DateTimeOffset.TryParse(GetString(el, "datePosted"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
            ? parsed.ToUniversalTime()
            : now;

        results.Add(new VacancyDto
        {
            Id = Guid.NewGuid(),
            ExternalId = string.IsNullOrWhiteSpace(externalId) ? url : externalId,
            Title = title,
            Company = company,
            Description = string.IsNullOrWhiteSpace(description) ? title : description,
            Category = category,
            CategoryGroup = VacancyCategoryMap.Resolve(category),
            Location = location,
            WorkFormat = VacancyNormalizer.ResolveWorkFormat(location, null),
            ExperienceMonths = VacancyExperienceParser.ParseMonths(null, description),
            Currency = "USD",
            Source = JobSource.Djinni,
            SourceUrl = url,
            PublishedAt = published,
            FirstSeenAt = now,
            LastSeenAt = now,
            IsActive = true
        });
    }

    private static string? GetString(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out JsonElement val) && val.ValueKind == JsonValueKind.String
            ? val.GetString()
            : null;

    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        string noTags = HtmlTagRegex().Replace(html, " ");
        string decoded = WebUtility.HtmlDecode(noTags);
        return MultiSpaceRegex().Replace(decoded, " ").Trim();
    }

    [GeneratedRegex(@"/vacancies/(\d+)/?", RegexOptions.None, 500)]
    private static partial Regex DouVacancyIdRegex();

    [GeneratedRegex(@"<script[^>]*type\s*=\s*[""']application/ld\+json[""'][^>]*>(.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    private static partial Regex JsonLdRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.None, 500)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.None, 500)]
    private static partial Regex MultiSpaceRegex();
}
