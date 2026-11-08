// AI: розбір robota.ua (schema.org JobPosting у JSON-LD і посилання на вакансії в списку) (issue #62); перевірено: юніт-тести RobotaUaPageTests на розмітці за schema.org, ручний запит до robota.ua (листопад 2026: robots.txt — 403 від Cloudflare, список без JS — порожня оболонка, тож справжніх вакансій парсер поки не отримує), самоперевірка автора (§8)

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Api.Workers;

/// <summary>robota.ua: вакансії читаються зі структурованих JobPosting-даних та посилань у списку.</summary>
public sealed class RobotaUaParser : IJobSiteParser, IBrowserOnlySite
{
    private const string BaseHost = "https://robota.ua";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    // Дозволяємо як /vacancy123, так і /vacancy/123, а також обрізаємо будь-які query-параметри (?, &, #)
    private static readonly Regex VacancyUrl = new(@"/vacancy/?(?<id>\d+)(?:[/?#]|$)", RegexOptions.IgnoreCase, RegexTimeout);
    public string Name => "robota.ua";

    public Uri Home { get; } = new($"{BaseHost}/");

    // Публічний список вакансій; Robota.ua додає параметри сторінки через «;», а не «?»
    private const string ListingUrl = $"{BaseHost}/zapros/ukraine";

    public string? ListPageUrl(int page) => page > 0
        ? page == 1 ? ListingUrl : $"{ListingUrl};page={page}"
        : null;

    public List<RawVacancy> ParseListPage(string html)
    {
        File.WriteAllText("robota_debug.html", html);

        HtmlNode document = Html.Load(html).DocumentNode;
        List<RawVacancy> jobs = ReadStructuredJobs(document).ToList();
        HashSet<string> ids = jobs.Select(job => job.Id).OfType<string>().ToHashSet(StringComparer.Ordinal);

        foreach (HtmlNode link in document.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
        {
            string href = WebUtility.HtmlDecode(link.GetAttributeValue("href", string.Empty));
            
            bool sitePath = href.StartsWith('/') && !href.StartsWith("//", StringComparison.Ordinal);
            if (!sitePath
                && Uri.TryCreate(href, UriKind.Absolute, out Uri? absolute)
                && (absolute.Scheme is not ("http" or "https")
                    || !(absolute.Host.Equals("robota.ua", StringComparison.OrdinalIgnoreCase)
                        || absolute.Host.EndsWith(".robota.ua", StringComparison.OrdinalIgnoreCase))))
            {
                continue;
            }

            Match match = VacancyUrl.Match(href);
            if (!match.Success || !ids.Add(match.Groups["id"].Value))
            {
                continue;
            }

            // Шукаємо заголовок: або всередині лінка (h2, span), або атрибут title, або текст самого лінка
            string title = Html.Clean(link.SelectSingleNode(".//h2 | .//h3 | .//span[contains(@class, 'title')]") ?? link);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = link.GetAttributeValue("title", string.Empty).Trim();
            }

            // Якщо лінк виявився кнопкою без тексту — пропускаємо
            if (title.Length == 0)
            {
                continue;
            }

            // Забираємо чистий URL без сміття від трекерів (?searchId=...)
            string cleanHref = href.Split('?')[0].Split('#')[0];

            jobs.Add(new RawVacancy
            {
                Source = JobSource.RobotaUa,
                Id = match.Groups["id"].Value,
                Url = AbsoluteUrl(cleanHref),
                Title = title
            });
        }

        return jobs;
    }

    public RawVacancy ParseJobPage(string html)
    {
        HtmlNode document = Html.Load(html).DocumentNode;

        // 1. Пробуємо structured data (якщо є)
        RawVacancy? structured = ReadStructuredJobs(document).FirstOrDefault();
        if (structured != null && !string.IsNullOrWhiteSpace(structured.DescriptionHtml))
        {
            return structured;
        }

        // 2. Шукаємо контейнер опису за актуальними селекторами Robota.ua
        HtmlNode? descNode = document.SelectSingleNode("//*[@itemprop='description']")
            ?? document.SelectSingleNode("//alliance-vacancy-description")
            ?? document.SelectSingleNode("//*[contains(@class, 'vacancy-description') or contains(@class, 'card-description')]")
            ?? document.SelectSingleNode("//main//*[contains(@class, 'description')]")
            ?? document.SelectSingleNode("//main//div[contains(@class, 'santa-typo')]")
            ?? document.SelectSingleNode("//main");

        string? title = Html.Text(document, "//h1");
        string? desc = descNode?.InnerHtml?.Trim();

        return new RawVacancy
        {
            Title = !string.IsNullOrWhiteSpace(title) ? title : null,
            DescriptionHtml = !string.IsNullOrWhiteSpace(desc) ? desc : "Опис вакансії на сайті robota.ua"
        };
    }

    private static IEnumerable<RawVacancy> ReadStructuredJobs(HtmlNode document)
    {
        foreach (HtmlNode script in document.SelectNodes("//script[@type='application/ld+json']") ?? Enumerable.Empty<HtmlNode>())
        {
            using JsonDocument json = JsonDocument.Parse(script.InnerText);
            foreach (JsonElement item in Flatten(json.RootElement))
            {
                if (!HasType(item, "JobPosting"))
                {
                    continue;
                }

                string? url = Text(item, "url") ?? Text(item, "@id");
                string? id = Identifier(item) ?? VacancyId(url);
                if (id is null)
                {
                    continue;
                }

                yield return new RawVacancy
                {
                    Source = JobSource.RobotaUa,
                    Id = id,
                    Url = AbsoluteUrl(url),
                    Title = Text(item, "title"),
                    Company = OrganizationName(item),
                    Salary = FormatSalary(item),
                    Location = Location(item),
                    Posted = Text(item, "datePosted"),
                    Experience = Experience(item),
                    Employment = Employment(item),
                    Category = Text(item, "occupationalCategory") ?? Text(item, "industry"),
                    DescriptionHtml = Text(item, "description")
                };
            }
        }
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                foreach (JsonElement item in Flatten(child))
                {
                    yield return item;
                }
            }

            yield break;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        yield return element;
        if (element.TryGetProperty("@graph", out JsonElement graph))
        {
            foreach (JsonElement item in Flatten(graph))
            {
                yield return item;
            }
        }
    }

    private static bool HasType(JsonElement item, string expected) =>
        Values(item, "@type").Any(value => value.ValueKind == JsonValueKind.String
            && string.Equals(value.GetString(), expected, StringComparison.OrdinalIgnoreCase));

    private static string? Identifier(JsonElement item)
    {
        JsonElement identifier = Values(item, "identifier").FirstOrDefault();
        if (identifier.ValueKind == JsonValueKind.Object)
        {
            return Text(identifier, "value") ?? Text(identifier, "name");
        }

        return StringValue(identifier);
    }

    private static string? OrganizationName(JsonElement item)
    {
        JsonElement organization = Values(item, "hiringOrganization").FirstOrDefault();
        return StringValue(organization) ?? Text(organization, "name");
    }

    private static string? FormatSalary(JsonElement item)
    {
        JsonElement salary = Values(item, "baseSalary").FirstOrDefault();
        JsonElement value = Values(salary, "value").FirstOrDefault();
        string? currency = Text(salary, "currency")?.ToUpperInvariant();
        string? amount = (Number(value, "minValue"), Number(value, "maxValue")) switch
        {
            ({ } low, { } high) => $"{Whole(low)}-{Whole(high)}",
            ({ } low, null) => $"від {Whole(low)}",
            (null, { } high) => $"до {Whole(high)}",
            _ => Number(value, "value") is { } exact ? Whole(exact) : null
        };

        if (amount is null)
        {
            return null;
        }

        return currency is "USD" or "EUR" or "UAH" ? $"{amount} {currency}" : amount;
    }

    private static string? Location(JsonElement item)
    {
        List<string> places = Values(item, "jobLocation")
            .SelectMany(location => Values(location, "address"))
            .Select(address => string.Join(", ", new[] { Text(address, "addressLocality"), Text(address, "addressRegion"), Text(address, "addressCountry") }
                .Where(value => !string.IsNullOrWhiteSpace(value))))
            .Where(place => place.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool remote = Text(item, "jobLocationType")?.Equals("TELECOMMUTE", StringComparison.OrdinalIgnoreCase) == true;
        return places.Count == 0
            ? remote ? "Віддалено" : null
            : remote ? $"{string.Join("; ", places)} / Віддалено" : string.Join("; ", places);
    }

    private static string? Employment(JsonElement item)
    {
        string[] values = Values(item, "employmentType")
            .Select(StringValue)
            .OfType<string>()
            .Select(value => value.ToUpperInvariant() switch
            {
                "FULL_TIME" => "Повна зайнятість",
                "PART_TIME" => "Часткова зайнятість",
                "CONTRACTOR" => "Контракт",
                "TEMPORARY" => "Тимчасова робота",
                _ => value
            })
            .ToArray();
        return values.Length > 0 ? string.Join(", ", values) : null;
    }

    private static string? Experience(JsonElement item)
    {
        JsonElement requirement = Values(item, "experienceRequirements").FirstOrDefault();
        if (StringValue(requirement) is { } text)
        {
            return text;
        }

        decimal? months = Number(requirement, "monthsOfExperience");
        if (months is null)
        {
            return null;
        }

        if (months == 0)
        {
            return "Без досвіду";
        }

        if (months % 12 != 0)
        {
            return $"{Whole(months.Value)} міс. досвіду";
        }

        decimal years = months.Value / 12;
        string unit = years % 10 == 1 && years % 100 != 11
            ? "рік"
            : years % 10 is >= 2 and <= 4 && years % 100 is not (>= 12 and <= 14)
                ? "роки"
                : "років";
        return $"{Whole(years)} {unit} досвіду";
    }

    private static decimal? Number(JsonElement item, string name) =>
        decimal.TryParse(Text(item, name), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number) ? number : null;

    private static string Whole(decimal number) => number.ToString("0.##", CultureInfo.InvariantCulture);

    private static string? Text(JsonElement item, string name) =>
        Values(item, name).Select(StringValue).FirstOrDefault(value => value is not null);

    private static IEnumerable<JsonElement> Values(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(name, out JsonElement value)
        && value.ValueKind != JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [value]
            : [];

    private static string? StringValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        _ => null
    };

    private static string? VacancyId(string? url) =>
        url is null ? null : VacancyUrl.Match(url) is { Success: true } match ? match.Groups["id"].Value : null;

    private static string? AbsoluteUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? absolute)
            || absolute.Scheme is not ("http" or "https"))
        {
            return $"{BaseHost}/{url.TrimStart('/')}";
        }

        return absolute.Host.Equals("robota.ua", StringComparison.OrdinalIgnoreCase)
            || absolute.Host.EndsWith(".robota.ua", StringComparison.OrdinalIgnoreCase)
                ? absolute.GetLeftPart(UriPartial.Path)
                : url;
    }
}
