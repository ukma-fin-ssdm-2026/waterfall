// AI: розбір сторінок work.ua; перевірено: юніт-тести WorkUaPageTests на розмітці справжніх сторінок (жовтень 2026), ручний запуск API проти work.ua, самоперевірка автора (§8)

using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Api.Workers;

/// <summary>
/// work.ua, усі вакансії по всій Україні. Картка в списку дає назву, компанію, зарплату, місто й дату публікації,
/// а сторінка вакансії — повний опис, графік, досвід, категорію й навички.
/// </summary>
public sealed class WorkUaParser : IJobSiteParser
{
    private const string BaseHost = "https://www.work.ua";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly Regex JobUrl = new(@"^/jobs/(?<id>\d+)/?$", RegexOptions.IgnoreCase, RegexTimeout);
    // «Серчер, Research Specialist, вакансія від 19 жовтня 2026» — дата є лише в підказці до посилання в картці
    private static readonly Regex PostedDate = new(@",\s*вакансія від (?<day>\d{1,2}) (?<month>\p{L}+) (?<year>\d{4})", RegexOptions.IgnoreCase, RegexTimeout);
    private static readonly string[] Months = ["січня", "лютого", "березня", "квітня", "травня", "червня", "липня", "серпня", "вересня", "жовтня", "листопада", "грудня"];

    public string Name => "work.ua";

    public Uri Home { get; } = new($"{BaseHost}/");

    // Сама /jobs/ — сторінка пошуку без вакансій; ss=1 без міста й категорії — результати пошуку по всій Україні
    public string? ListPageUrl(int page) => page == 1 ? $"{BaseHost}/jobs/?ss=1" : $"{BaseHost}/jobs/?ss=1&page={page}";

    public List<RawVacancy> ParseListPage(string html)
    {
        HtmlNodeCollection? cards = Load(html).DocumentNode.SelectNodes("//div[contains(concat(' ', normalize-space(@class), ' '), ' job-link ')]");
        List<RawVacancy> jobs = [];
        foreach (HtmlNode card in cards ?? Enumerable.Empty<HtmlNode>())
        {
            HtmlNode? link = card.SelectSingleNode(".//h2/a[@href]");
            Match id = JobUrl.Match(link?.GetAttributeValue("href", string.Empty) ?? string.Empty);
            if (link is null || !id.Success)
            {
                continue;
            }

            string linkTitle = WebUtility.HtmlDecode(link.GetAttributeValue("title", string.Empty));
            string title = Clean(link);
            jobs.Add(new RawVacancy
            {
                Source = JobSource.WorkUa,
                Id = id.Groups["id"].Value,
                Url = $"{BaseHost}/jobs/{id.Groups["id"].Value}/",
                // Якщо текст посилання порожній, назва є в підказці до нього: «Назва, вакансія від …»
                Title = title.Length > 0 ? title : Clean(PostedDate.Replace(linkTitle, string.Empty)),
                Company = Text(card, ".//span[contains(@class,'glyphicon-company')]/following-sibling::span[1]//span[contains(@class,'strong-600')]"),
                // Мітка «Вища за середню» стоїть окремо й суми не має; сума — одразу після значка гривні
                Salary = Text(card, ".//span[contains(@class,'glyphicon-hryvnia-fill')]/following-sibling::span[1][contains(@class,'strong-600')]"),
                // «Тернопіль, » + посилання «1,0 км від центру»: беремо лише місто
                Location = Text(card, ".//span[contains(@class,'glyphicon-map-marker')]/following-sibling::span[1]")?.TrimEnd(',', ' '),
                Posted = ParsePosted(linkTitle),
                // Скорочений опис картки знадобиться, якщо сторінка вакансії не відкриється
                DescriptionHtml = card.SelectSingleNode(".//p[contains(@class,'ellipsis')]")?.InnerHtml
            });
        }

        return jobs;
    }

    public RawVacancy ParseJobPage(string html)
    {
        HtmlNode page = Load(html).DocumentNode;
        // Навички — мітки під «Ключові вимоги і навички»; роботодавець може їх не вказати
        List<string> skills = page.SelectNodes("//li[contains(concat(' ', normalize-space(@class), ' '), ' label-skill ')]")?
            .Select(Clean)
            .Where(skill => skill.Length > 0)
            .ToList() ?? [];
        // «Вакансії › у Києві › Тестувальник»: останній пункт — категорія
        List<string> breadcrumbs = page.SelectNodes("//*[@itemtype='http://schema.org/BreadcrumbList']//*[@itemprop='itemListElement']//*[@itemprop='name']")?
            .Select(Clean)
            .ToList() ?? [];

        return new RawVacancy
        {
            DescriptionHtml = page.SelectSingleNode("//div[@id='job-description']")?.InnerHtml,
            Employment = Text(page, "//li[span[contains(@class,'glyphicon-schedule')]]"),
            // «Досвід роботи від 1 року.» серед ключових вимог
            Experience = page.SelectNodes("//h3[contains(., 'Ключові вимоги')]/following-sibling::ul[1]/li")?
                .Select(requirement => Clean(requirement).TrimEnd('.'))
                .FirstOrDefault(requirement => requirement.StartsWith("Досвід", StringComparison.OrdinalIgnoreCase)),
            Category = breadcrumbs.Count >= 3 ? breadcrumbs[^1] : null,
            Tags = skills.Count > 0 ? string.Join(", ", skills) : null
        };
    }

    /// <summary> «…, вакансія від 19 жовтня 2026» → «19.10.2026» (нормалізація) </summary>
    private static string? ParsePosted(string linkTitle)
    {
        Match date = PostedDate.Match(linkTitle);
        int month = date.Success ? Array.IndexOf(Months, date.Groups["month"].Value.ToLowerInvariant()) + 1 : 0;
        int day = date.Success ? int.Parse(date.Groups["day"].Value, CultureInfo.InvariantCulture) : 0;
        return month > 0 ? $"{day:00}.{month:00}.{date.Groups["year"].Value}" : null;
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

    // Шматки тексту з різних тегів розділяємо пробілом: «Повна зайнятість<span>·</span>Стандартний графік» не злипнеться
    private static string Clean(HtmlNode node) =>
        Clean(string.Join(' ', node.DescendantsAndSelf().Where(part => part.NodeType == HtmlNodeType.Text).Select(part => part.InnerText)));


    private static string Clean(string html) => string.Join(' ', WebUtility.HtmlDecode(html).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
