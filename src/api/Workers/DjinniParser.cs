// AI: розбір сторінок djinni.co (issue #62); перевірено: юніт-тести DjinniPageTests на розмітці справжніх сторінок (жовтень 2026), ручний запуск API проти djinni.co, самоперевірка автора (§8)

using System.Globalization;
using System.Net;
using System.Text.Json;
using HtmlAgilityPack;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Api.Workers;

/// <summary>
/// djinni.co: список вакансій читаємо з даних для пошуковиків, які Djinni вбудовує в сторінку
/// (schema.org JobPosting у &lt;script type="application/ld+json"&gt;), тож не залежимо від верстки карток.
/// Зі сторінки вакансії беремо лише необов'язкове поле «Необхідні навички».
/// </summary>
public sealed class DjinniParser : IJobSiteParser
{
    private static readonly TimeZoneInfo KyivTime = FindKyivTime();

    public string Name => "djinni.co";

    public Uri Home { get; } = new("https://djinni.co/");

    public string? ListPageUrl(int page) => page == 1 ? "https://djinni.co/jobs/" : $"https://djinni.co/jobs/?page={page}";

    /// <summary>Значення переводимо у формат, який уже розуміє нормалізація: «2500-5000 USD», «1 рік досвіду» тощо.</summary>
    public List<RawVacancy> ParseListPage(string html)
    {
        HtmlNodeCollection? scripts = Html.Load(html).DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        List<RawVacancy> jobs = [];
        foreach (HtmlNode script in scripts ?? Enumerable.Empty<HtmlNode>())
        {
            using JsonDocument json = JsonDocument.Parse(script.InnerText);
            jobs.AddRange(Items(json.RootElement)
                .Where(item => Text(item, "@type") == "JobPosting")
                .Select(ToRawVacancy));
        }

        return jobs;
    }

    /// <summary>
    /// Поле «Необхідні навички»: «TypeScript, React.js, Node.js». Роботодавець заповнює його за бажанням
    /// (у жовтні 2026 — приблизно в кожній четвертій вакансії); якщо поля немає, Tags лишається null.
    /// Довгий список Djinni ділить на видиму частину й приховану за кнопкою «+ ще 3» — беремо обидві, а кнопку ні.
    /// </summary>
    public RawVacancy ParseJobPage(string html)
    {
        List<string> parts = Html.Load(html).DocumentNode
            .SelectNodes("//h2[normalize-space()='Необхідні навички']/following-sibling::div[1]//span")?
            .Select(part => Html.Clean(part.InnerText))
            .Where(part => part.Length > 0)
            .ToList() ?? [];
        return new RawVacancy { Tags = parts.Count > 0 ? string.Join(", ", parts) : null };
    }

    private static RawVacancy ToRawVacancy(JsonElement job) => new()
    {
        Source = JobSource.Djinni,
        Id = Text(job, "identifier"),
        Url = Text(job, "url"),
        Title = Text(job, "title"),
        Company = Items(job, "hiringOrganization").Select(organization => Text(organization, "name")).FirstOrDefault(),
        Salary = FormatSalary(job),
        Location = FormatLocation(job),
        Posted = FormatPosted(Text(job, "datePosted")),
        Experience = FormatExperience(job),
        Employment = FormatEmployment(job),
        Category = Text(job, "category"),
        // Опис у JSON-LD — звичайний текст; кодуємо його в HTML, щоб нормалізація не прийняла «List<T>» за тег
        DescriptionHtml = Text(job, "description") is { } description
            ? WebUtility.HtmlEncode(description).Replace("\n", "<br>", StringComparison.Ordinal)
            : null
        // Навичок (Tags) у JSON-LD немає: їх дає сторінка вакансії (ParseJobPage)
    };

    /// <summary>schema.org MonetaryAmount → «2500-5000 USD», «до 5000 USD» чи «від 2000 EUR».</summary>
    private static string? FormatSalary(JsonElement job)
    {
        JsonElement salary = Items(job, "baseSalary").FirstOrDefault();
        JsonElement value = Items(salary, "value").FirstOrDefault();
        string? currency = Text(salary, "currency")?.ToUpperInvariant();
        string unit = Text(value, "unitText") ?? "MONTH";

        // Каталог порівнює місячні зарплати, а нормалізація знає лише USD, EUR і UAH: решту не вгадуємо
        if (currency is not ("USD" or "EUR" or "UAH") || !unit.Equals("MONTH", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? amount = (Number(value, "minValue"), Number(value, "maxValue")) switch
        {
            ({ } low, { } high) => $"{Whole(low)}-{Whole(high)}",
            (null, { } high) => $"до {Whole(high)}",
            ({ } low, null) => $"від {Whole(low)}",
            _ => Number(value, "value") is { } exact ? Whole(exact) : null
        };
        return amount is null ? null : $"{amount} {currency}";
    }

    /// <summary>«Віддалено», «Україна (Київ, Львів)» або «Польща / Віддалено».</summary>
    private static string? FormatLocation(JsonElement job)
    {
        List<string> places = Items(job, "jobLocation")
            .Select(place => Place(Items(place, "address").FirstOrDefault()))
            .OfType<string>()
            .Distinct()
            .ToList();
        string? where = places.Count > 0 ? string.Join("; ", places) : null;

        if (Text(job, "jobLocationType") != "TELECOMMUTE")
        {
            return where;
        }

        return where is null ? "Віддалено" : $"{where} / Віддалено";

        static string? Place(JsonElement address)
        {
            string? country = Text(address, "addressCountry");
            string cities = string.Join(", ", Texts(address, "addressLocality"));
            return cities.Length == 0 ? country : country is null ? cities : $"{country} ({cities})";
        }
    }

    /// <summary>
    /// Djinni пише datePosted за київським часом без зсуву («2026-10-24T20:43:05.94»). Додаємо зсув явно,
    /// інакше нормалізація прочитала б цей час як UTC, тобто на 2–3 години пізніше.
    /// </summary>
    private static string? FormatPosted(string? datePosted) =>
        DateTime.TryParse(datePosted, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime date)
        && date.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(date, KyivTime.GetUtcOffset(date)).ToString("O", CultureInfo.InvariantCulture)
            : datePosted;

    /// <summary>monthsOfExperience → «Без досвіду», «6 міс. досвіду», «1 рік досвіду», «5 років досвіду».</summary>
    private static string? FormatExperience(JsonElement job)
    {
        JsonElement requirements = Items(job, "experienceRequirements").FirstOrDefault();
        if (requirements.ValueKind == JsonValueKind.String)
        {
            return requirements.GetString();
        }

        decimal? months = Number(requirements, "monthsOfExperience");
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

        int years = (int)(months.Value / 12);
        string word = (years % 10, years % 100) switch
        {
            (1, not 11) => "рік",
            (2 or 3 or 4, not (12 or 13 or 14)) => "роки",
            _ => "років"
        };
        return $"{years} {word} досвіду";
    }

    /// <summary>FULL_TIME → «Повна зайнятість» тощо; невідомий тип показуємо як є.</summary>
    private static string? FormatEmployment(JsonElement job)
    {
        List<string> types = Texts(job, "employmentType")
            .Select(type => type switch
            {
                "FULL_TIME" => "Повна зайнятість",
                "PART_TIME" => "Часткова зайнятість",
                "CONTRACTOR" => "Контракт",
                "TEMPORARY" => "Тимчасова робота",
                "INTERN" => "Стажування",
                _ => type
            })
            .ToList();
        return types.Count > 0 ? string.Join(" · ", types) : null;
    }

    // У schema.org майже будь-яке поле може бути як одним значенням, так і масивом
    private static IEnumerable<JsonElement> Items(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [value];

    private static IEnumerable<JsonElement> Items(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind != JsonValueKind.Null
            ? Items(value)
            : [];

    private static string? Text(JsonElement element, string name)
    {
        JsonElement value = Items(element, name).FirstOrDefault();
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static List<string> Texts(JsonElement element, string name) =>
        Items(element, name)
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()!)
            .Where(text => text.Length > 0)
            .ToList();

    private static decimal? Number(JsonElement element, string name) =>
        decimal.TryParse(Text(element, name), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number) ? number : null;

    private static string Whole(decimal number) => number.ToString("0", CultureInfo.InvariantCulture);

    // IANA-назва працює на Linux і на Windows з ICU, «FLE Standard Time» — запасна назва Windows.
    // Без бази часових поясів лишаємо UTC: дата буде неточною на 2–3 години, але не зламає імпорт.
    private static TimeZoneInfo FindKyivTime()
    {
        string[] ids = ["Europe/Kyiv", "Europe/Kiev", "FLE Standard Time"];
        foreach (string id in ids)
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out TimeZoneInfo? zone))
            {
                return zone;
            }
        }

        return TimeZoneInfo.Utc;
    }
}
