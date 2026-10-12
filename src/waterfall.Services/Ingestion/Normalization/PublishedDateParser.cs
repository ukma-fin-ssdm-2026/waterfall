// AI: розбір дат публікації для нормалізації вакансій (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests, самоперевірка автора (§8)

using System.Globalization;
using System.Text.RegularExpressions;

namespace Waterfall.Services.Ingestion.Normalization;

/// <summary>
/// Розбирає дату публікації з тексту джерела у UTC: «сьогодні», «вчора», «2 дні тому», «3 години тому»,
/// «12.10.2026», «8 жовтня 2026», ISO 8601. Дати без часу трактуються як північ за київським часом.
/// </summary>
public static partial class PublishedDateParser
{
    private const int RegexTimeoutMs = 500;

    private static readonly TimeZoneInfo KyivTimeZone = ResolveKyivTimeZone();

    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["січня"] = 1,
        ["лютого"] = 2,
        ["березня"] = 3,
        ["квітня"] = 4,
        ["травня"] = 5,
        ["червня"] = 6,
        ["липня"] = 7,
        ["серпня"] = 8,
        ["вересня"] = 9,
        ["жовтня"] = 10,
        ["листопада"] = 11,
        ["грудня"] = 12,
        ["january"] = 1,
        ["february"] = 2,
        ["march"] = 3,
        ["april"] = 4,
        ["may"] = 5,
        ["june"] = 6,
        ["july"] = 7,
        ["august"] = 8,
        ["september"] = 9,
        ["october"] = 10,
        ["november"] = 11,
        ["december"] = 12,
        ["jan"] = 1,
        ["feb"] = 2,
        ["mar"] = 3,
        ["apr"] = 4,
        ["jun"] = 6,
        ["jul"] = 7,
        ["aug"] = 8,
        ["sep"] = 9,
        ["oct"] = 10,
        ["nov"] = 11,
        ["dec"] = 12
    };

    private static readonly string[] DayFirstFormats = ["dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy"];

    public static DateTimeOffset? Parse(string? text, DateTimeOffset now)
    {
        string? original = TextNormalizer.CleanLine(text);
        if (original is null)
        {
            return null;
        }

        // Нижній регістр — лише для слів («вчора», «жовтня»); ISO 8601 розбирається з оригіналу
        string value = original.ToLowerInvariant();

        switch (value)
        {
            case "щойно" or "just now" or "now":
                return now;
            case "сьогодні" or "today":
                return KyivMidnight(now);
            case "вчора" or "yesterday":
                return KyivMidnight(now).AddDays(-1);
            case "позавчора":
                return KyivMidnight(now).AddDays(-2);
        }

        Match relative = RelativeRegex().Match(value);
        if (relative.Success)
        {
            int amount = int.Parse(relative.Groups["amount"].Value, CultureInfo.InvariantCulture);
            return SubtractUnits(now, amount, relative.Groups["unit"].Value);
        }

        if (DateTime.TryParseExact(value, DayFirstFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dayFirst))
        {
            return FromKyivDate(DateOnly.FromDateTime(dayFirst));
        }

        Match named = NamedMonthRegex().Match(value);
        if (named.Success && Months.TryGetValue(named.Groups["month"].Value, out int month))
        {
            return FromNamedMonth(named, month, now);
        }

        if (IsoDateRegex().IsMatch(original))
        {
            if (original.Length == 10 && DateOnly.TryParseExact(original, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly isoDate))
            {
                return FromKyivDate(isoDate);
            }

            if (DateTimeOffset.TryParse(original, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset iso))
            {
                return iso.ToUniversalTime();
            }
        }

        return null;
    }

    private static DateTimeOffset? SubtractUnits(DateTimeOffset now, int amount, string unit)
    {
        if (unit.StartsWith("хв", StringComparison.Ordinal) || unit.StartsWith("min", StringComparison.Ordinal))
        {
            return now.AddMinutes(-amount);
        }

        if (unit.StartsWith("год", StringComparison.Ordinal) || unit.StartsWith("hour", StringComparison.Ordinal))
        {
            return now.AddHours(-amount);
        }

        if (unit.StartsWith("дн", StringComparison.Ordinal) || unit.StartsWith("ден", StringComparison.Ordinal)
            || unit.StartsWith("day", StringComparison.Ordinal))
        {
            return now.AddDays(-amount);
        }

        if (unit.StartsWith("тиж", StringComparison.Ordinal) || unit.StartsWith("week", StringComparison.Ordinal))
        {
            return now.AddDays(-7 * amount);
        }

        if (unit.StartsWith("міс", StringComparison.Ordinal) || unit.StartsWith("month", StringComparison.Ordinal))
        {
            return now.AddMonths(-amount);
        }

        return null;
    }

    private static DateTimeOffset? FromNamedMonth(Match named, int month, DateTimeOffset now)
    {
        int day = int.Parse(named.Groups["day"].Value, CultureInfo.InvariantCulture);
        int year = named.Groups["year"].Success
            ? int.Parse(named.Groups["year"].Value, CultureInfo.InvariantCulture)
            : TimeZoneInfo.ConvertTime(now, KyivTimeZone).Year;

        if (day > DateTime.DaysInMonth(year, month))
        {
            return null;
        }

        DateTimeOffset result = FromKyivDate(new DateOnly(year, month, day));

        // «8 жовтня» без року, прочитане в січні, означає жовтень минулого року
        if (!named.Groups["year"].Success && result > now)
        {
            result = FromKyivDate(new DateOnly(year - 1, month, day));
        }

        return result;
    }

    private static DateTimeOffset KyivMidnight(DateTimeOffset now)
    {
        DateTimeOffset kyivNow = TimeZoneInfo.ConvertTime(now, KyivTimeZone);
        return FromKyivDate(DateOnly.FromDateTime(kyivNow.DateTime));
    }

    private static DateTimeOffset FromKyivDate(DateOnly date)
    {
        DateTime localMidnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(localMidnight, KyivTimeZone.GetUtcOffset(localMidnight)).ToUniversalTime();
    }

    private static TimeZoneInfo ResolveKyivTimeZone()
    {
        // Europe/Kyiv — сучасна назва, Europe/Kiev — стара, FLE Standard Time — ідентифікатор Windows
        foreach (string id in new[] { "Europe/Kyiv", "Europe/Kiev", "FLE Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out TimeZoneInfo? zone))
            {
                return zone;
            }
        }

        return TimeZoneInfo.Utc;
    }

    [GeneratedRegex(@"^(?<amount>\d{1,3})\s*(?<unit>\p{L}+)\.?\s+(?:тому|ago)$", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex RelativeRegex();

    [GeneratedRegex(@"^(?<day>\d{1,2})\s+(?<month>\p{L}+)\.?(?:,?\s+(?<year>\d{4}))?$", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex NamedMonthRegex();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex IsoDateRegex();
}
