// AI: розбір RSS-стрічки та сторінок вакансій jobs.dou.ua; перевірено: юніт-тести DouPageTests на розмітці справжніх сторінок (жовтень 2026), ручний запуск API проти jobs.dou.ua, самоперевірка автора (§8)

using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using HtmlAgilityPack;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Api.Workers;

/// <summary>
/// jobs.dou.ua: список — офіційна RSS-стрічка DOU з 50 найсвіжішими вакансіями,
/// а сторінка вакансії дає окремими полями назву, компанію, зарплату, місто й категорію.
/// Окремого поля навичок на DOU немає, тож навичок у вакансій DOU не буде.
/// </summary>
public sealed class DouParser : IJobSiteParser
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly Regex VacancyUrl = new(@"^https://jobs\.dou\.ua/companies/[^/]+/vacancies/(?<id>\d+)/", RegexOptions.IgnoreCase, RegexTimeout);
    // Зарплата в заголовку стрічки: «$2500–3000», «від $5000», «до 4000 €»
    private static readonly Regex Salary = new(@"^(?:від|до)?\s*[$€₴\d]", RegexOptions.IgnoreCase, RegexTimeout);

    public string Name => "jobs.dou.ua";

    public Uri Home { get; } = new("https://jobs.dou.ua/");

    // Стрічка одна, без наступних сторінок
    public string? ListPageUrl(int page) => page == 1 ? "https://jobs.dou.ua/vacancies/feeds/" : null;

    /// <summary>
    /// Елементи RSS: посилання, точний час публікації й повний опис. Назву, компанію, зарплату й місця беремо з заголовка
    /// «Middle Java Developer в ПУМБ, $2500–3000, Київ, віддалено» — це запасний варіант: сторінка вакансії дасть їх окремими полями.
    /// </summary>
    public List<RawVacancy> ParseListPage(string xml)
    {
        List<RawVacancy> jobs = [];
        foreach (XElement item in LoadXml(xml).Descendants("item"))
        {
            string link = item.Element("link")?.Value.Trim() ?? string.Empty;
            Match url = VacancyUrl.Match(link);
            if (!url.Success)
            {
                continue;
            }

            (string title, string? company, string? salary, string? places) = SplitTitle(Clean(item.Element("title")?.Value ?? string.Empty));
            jobs.Add(new RawVacancy
            {
                Source = JobSource.Dou,
                Id = url.Groups["id"].Value,
                // Без «?utm_source=jobsrss»
                Url = url.Value,
                Title = title,
                Company = company,
                Salary = salary,
                Location = places,
                Posted = FormatPosted(item.Element("pubDate")?.Value),
                DescriptionHtml = item.Element("description")?.Value
            });
        }

        return jobs;
    }

    public RawVacancy ParseJobPage(string html)
    {
        HtmlNode? vacancy = Load(html).DocumentNode.SelectSingleNode("//div[contains(concat(' ', normalize-space(@class), ' '), ' b-vacancy ')]");
        if (vacancy is null)
        {
            return new RawVacancy();
        }

        // «Всі вакансії / Java / за кордоном»: другий пункт — категорія
        List<string> breadcrumbs = vacancy.SelectNodes(".//li[contains(@class,'breadcrumbs')]/a")?.Select(Clean).ToList() ?? [];
        List<string> sections = vacancy.SelectNodes(".//div[contains(concat(' ', normalize-space(@class), ' '), ' vacancy-section ')]")?
            .Select(section => section.InnerHtml)
            .ToList() ?? [];

        return new RawVacancy
        {
            Title = Text(vacancy, ".//h1"),
            Company = Text(vacancy, ".//div[contains(@class,'b-compinfo')]//div[contains(@class,'l-n')]/a[1]"),
            Salary = Text(vacancy, ".//div[contains(@class,'sh-info')]//span[contains(@class,'salary')]"),
            Location = Text(vacancy, ".//div[contains(@class,'sh-info')]//span[contains(@class,'place')]"),
            Category = breadcrumbs.Count >= 2 ? breadcrumbs[1] : null,
            DescriptionHtml = sections.Count > 0 ? string.Join("\n", sections) : null
        };
    }

    /// <summary>«Посада в Компанія, $2500–3000, Київ, віддалено» → посада, компанія, зарплата, місця.</summary>
    private static (string Title, string? Company, string? Salary, string? Places) SplitTitle(string text)
    {
        // «в» трапляється й у назві посади, а в назві компанії — навряд, тож ділимо за останнім
        int at = text.LastIndexOf(" в ", StringComparison.Ordinal);
        if (at < 0)
        {
            return (text, null, null, null);
        }

        List<string> rest = text[(at + 3)..].Split(", ").ToList();
        string company = rest[0];
        rest.RemoveAt(0);
        string? salary = rest.Count > 0 && Salary.IsMatch(rest[0]) ? rest[0] : null;
        if (salary is not null)
        {
            rest.RemoveAt(0);
        }

        return (text[..at], company, salary, rest.Count > 0 ? string.Join(", ", rest) : null);
    }

    // «Sun, 25 Oct 2026 21:04:06 +0200» → ISO 8601 зі зсувом, який розуміє нормалізація
    private static string? FormatPosted(string? pubDate) =>
        DateTimeOffset.TryParse(pubDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset date)
            ? date.ToString("O", CultureInfo.InvariantCulture)
            : null;

    // Стрічку пише сайт, тож DTD і зовнішні сутності вимкнено (захист від XXE)
    private static XDocument LoadXml(string xml)
    {
        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using StringReader text = new(xml);
        using XmlReader reader = XmlReader.Create(text, settings);
        return XDocument.Load(reader);
    }

    private static HtmlDocument Load(string html)
    {
        HtmlDocument document = new();
        document.LoadHtml(html);
        return document;
    }

    private static string? Text(HtmlNode node, string xpath)
    {
        string text = node.SelectSingleNode(xpath) is { } found ? Clean(found) : string.Empty;
        return text.Length > 0 ? text : null;
    }

    private static string Clean(HtmlNode node) =>
        Clean(string.Join(' ', node.DescendantsAndSelf().Where(part => part.NodeType == HtmlNodeType.Text).Select(part => part.InnerText)));

    // У заголовках стрічки HTML-сутності закодовано двічі («&amp;nbsp;»), тож після XML лишаються «&nbsp;» і «&amp;»
    private static string Clean(string text) => string.Join(' ', WebUtility.HtmlDecode(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
