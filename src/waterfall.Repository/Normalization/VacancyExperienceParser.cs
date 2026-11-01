// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Globalization;
using System.Text.RegularExpressions;

namespace Waterfall.Repository.Normalization;

/// <summary>
/// Витягує з тексту вакансії мінімальний необхідний досвід у місяцях. Потрібен як запасний варіант:
/// Djinni віддає точне число місяців, DOU — мітку з фасетної стрічки, а Work.ua та решта описів мають лише
/// текст на кшталт «Досвід роботи від 2 років», «2+ years» чи «без досвіду».
/// </summary>
public static class VacancyExperienceParser
{
    // Тайм-аут захищає від зависання регулярного виразу на шкідливому вводі (ReDoS)
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    // «без досвіду», «без опыту», «no experience», «0 років досвіду»
    private static readonly Regex NoExperience = new(
        @"(?:без|без\s+жодного)\s+(?:досвіду|опыту)|no\s+experience|without\s+experience|0\s*(?:років|роки|рік|року|years?|yrs?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    // «1 рік», «2+ роки», «від 3 років», «2-3 роки», «6 міс. досвіду», «2+ years», «3 years of experience»
    private static readonly Regex Experience = new(
        @"(?<num>\d{1,3})(?:\s*[-–—]\s*(?<num2>\d{1,3}))?\s*\+?\s*(?<unit>років|роки|рік|року|р\.|years|year|yrs|yr|місяців|місяці|місяць|міс|months|month)(?![\p{L}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly HashSet<string> YearUnits =
        new(["рік", "роки", "років", "року", "р.", "year", "years", "yr", "yrs"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Мінімальний досвід у місяцях: спершу з поля досвіду, а якщо там нічого — з опису.
    /// null означає «досвід не вказано», а не «досвід не потрібен»: 0 — це саме «без досвіду».
    /// </summary>
    public static int? ParseMonths(string? experience, string? description = null) =>
        FromText(experience) ?? FromText(description);

    /// <summary>Мінімальний досвід із довільного тексту; «до N років» (верхня межа) не є мінімумом і дає null.</summary>
    public static int? FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string normalized = text.ToLowerInvariant();
        if (NoExperience.IsMatch(normalized))
        {
            return 0;
        }

        // «до 2 років» описує верхню межу, тож нижньої межі з нього не виводимо
        if (normalized.Contains("до ", StringComparison.Ordinal) && !normalized.Contains("від ", StringComparison.Ordinal))
        {
            return null;
        }

        Match match = Experience.Match(normalized);
        if (!match.Success)
        {
            return null;
        }

        int number = int.Parse(match.Groups["num"].Value, CultureInfo.InvariantCulture);
        if (match.Groups["num2"].Success)
        {
            // Діапазон «2-3 роки»: мінімум — менше число, навіть якщо на сайті його перевернули
            number = Math.Min(number, int.Parse(match.Groups["num2"].Value, CultureInfo.InvariantCulture));
        }

        string unit = match.Groups["unit"].Value;
        return YearUnits.Contains(unit) ? number * 12 : number;
    }
}
