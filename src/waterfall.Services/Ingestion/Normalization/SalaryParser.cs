// AI: розбір текстових зарплат для нормалізації вакансій (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests, самоперевірка автора (§8)

using System.Globalization;
using System.Text.RegularExpressions;

namespace Waterfall.Services.Ingestion.Normalization;

/// <summary>
/// Зарплата після розбору: межі вилки (будь-яка може бути відсутня) і ISO-код валюти.
/// </summary>
public readonly record struct SalaryInfo(decimal? Min, decimal? Max, string Currency);

/// <summary>
/// Розбирає зарплату з тексту джерела: «$3500-4500», «від 40 000 грн», «до $4000», «€2.5k–3.5k»,
/// «1500 USD», «за домовленістю». Погодинні ставки й суми поза межами схеми відкидаються.
/// </summary>
public static partial class SalaryParser
{
    /// <summary>Та сама верхня межа, що й у валідації API (salaryMin / salaryMax).</summary>
    public const decimal MaxSalary = 1_000_000m;

    private const int RegexTimeoutMs = 500;

    public static SalaryInfo Parse(string? text, string defaultCurrency)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new SalaryInfo(null, null, defaultCurrency);
        }

        string value = text.ToLowerInvariant()
            .Replace(' ', ' ')   // нерозривний пробіл
            .Replace(' ', ' ')   // вузький нерозривний пробіл
            .Replace(' ', ' ');  // тонкий пробіл

        string currency = DetectCurrency(value) ?? defaultCurrency;

        // Погодинну ставку не можна чесно перевести в місячну зарплату, тож вилку не заповнюємо
        if (HourlyRegex().IsMatch(value))
        {
            return new SalaryInfo(null, null, currency);
        }

        List<decimal> amounts = [];
        foreach (Match match in AmountRegex().Matches(value))
        {
            if (TryParseAmount(match, out decimal amount))
            {
                amounts.Add(amount);
            }

            if (amounts.Count == 2)
            {
                break;
            }
        }

        if (amounts.Count == 0)
        {
            return new SalaryInfo(null, null, currency);
        }

        decimal? min;
        decimal? max;
        if (amounts.Count == 2)
        {
            (min, max) = amounts[0] <= amounts[1] ? (amounts[0], amounts[1]) : (amounts[1], amounts[0]);
        }
        else if (UpToRegex().IsMatch(value))
        {
            (min, max) = (null, amounts[0]);
        }
        else if (FromRegex().IsMatch(value))
        {
            (min, max) = (amounts[0], null);
        }
        else
        {
            (min, max) = (amounts[0], amounts[0]);
        }

        if (!IsInRange(min) || !IsInRange(max))
        {
            return new SalaryInfo(null, null, currency);
        }

        return new SalaryInfo(min, max, currency);
    }

    private static string? DetectCurrency(string value)
    {
        if (value.Contains('$') || value.Contains("usd") || value.Contains("дол"))
        {
            return "USD";
        }

        if (value.Contains('€') || value.Contains("eur") || value.Contains("євро"))
        {
            return "EUR";
        }

        if (value.Contains('₴') || value.Contains("грн") || value.Contains("uah"))
        {
            return "UAH";
        }

        return null;
    }

    private static bool TryParseAmount(Match match, out decimal amount)
    {
        string number = match.Groups["number"].Value;

        // «40 000» і «1,500» — розряди тисяч; «2.5» і «2,5» — десятковий дріб
        string normalized = GroupedThousandsRegex().IsMatch(number)
            ? number.Replace(" ", string.Empty).Replace(",", string.Empty)
            : number.Replace(',', '.');

        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount))
        {
            return false;
        }

        if (match.Groups["multiplier"].Success)
        {
            amount *= 1000m;
        }

        return amount > 0;
    }

    private static bool IsInRange(decimal? value) => value is null || (value > 0 && value <= MaxSalary);

    // Число з розрядами («40 000», «1,500») або просте число з дробом; за ним — необов'язкове «k»/«к»/«тис»,
    // яке не повинно бути початком слова («4500 кожного місяця» — не 4,5 млн)
    [GeneratedRegex(@"(?<number>\d{1,3}(?:[ ,]\d{3})+|\d+(?:[.,]\d+)?)\s?(?<multiplier>(?:k|к|тис)(?!\p{L}))?",
        RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex AmountRegex();

    [GeneratedRegex(@"^\d{1,3}(?:[ ,]\d{3})+$", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex GroupedThousandsRegex();

    [GeneratedRegex(@"/\s*(?:год|година|hour|hr|h)(?!\p{L})|на\s+годину|per\s+hour|погодинн",
        RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex HourlyRegex();

    [GeneratedRegex(@"(?<!\p{L})(?:до|up\s*to|max)(?!\p{L})", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex UpToRegex();

    [GeneratedRegex(@"(?<!\p{L})(?:від|from|min)(?!\p{L})", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex FromRegex();
}
