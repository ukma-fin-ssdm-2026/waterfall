// AI: очищення тексту для нормалізації вакансій (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests, самоперевірка автора (§8)

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Waterfall.Services.Ingestion.Normalization;

/// <summary>
/// Очищення тексту з джерел: HTML-теги й сутності, керівні та невидимі символи, зайві пробіли.
/// Опис вакансії зберігається як звичайний текст, а не розмітка (контрзахід THR-03 з threat-model.md).
/// </summary>
public static partial class TextNormalizer
{
    // Тайм-аут обмежує час роботи регулярних виразів на зловмисно підібраному вході (захист від ReDoS)
    private const int RegexTimeoutMs = 500;

    /// <summary>
    /// Однорядкове поле: прибирає теги, декодує HTML-сутності й зводить будь-які пробіли до одного.
    /// Повертає null для порожнього результату.
    /// </summary>
    public static string? CleanLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string text = WebUtility.HtmlDecode(TagRegex().Replace(value, " "));
        return CollapseWhitespace(text);
    }

    /// <summary>
    /// Перетворює HTML-опис на звичайний текст: вміст script і style відкидається,
    /// блокові теги стають переносами рядків, пункти списку — рядками з «• ».
    /// </summary>
    public static string? HtmlToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        string text = ScriptOrStyleRegex().Replace(html, " ");
        text = ListItemRegex().Replace(text, "\n• ");
        text = BlockBreakRegex().Replace(text, "\n");
        text = TagRegex().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);

        List<string> lines = [];
        foreach (string line in text.Split('\n'))
        {
            string? cleaned = CollapseWhitespace(line);
            if (cleaned is not null && cleaned != "•")
            {
                lines.Add(cleaned);
            }
        }

        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    /// <summary>
    /// Обрізає рядок до вказаної довжини, позначаючи обрізання символом «…».
    /// </summary>
    public static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, maxLength - 1).TrimEnd(), "…");
    }

    private static string? CollapseWhitespace(string value)
    {
        StringBuilder builder = new(value.Length);
        bool pendingSpace = false;

        foreach (char ch in value)
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            // Невидимі символи форматування (zero-width space, BOM, м'який перенос) лише заважають пошуку
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.Format)
            {
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    [GeneratedRegex(@"<[^>]*>", RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"<script\b[^>]*>.*?</script\s*>|<style\b[^>]*>.*?</style\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex ScriptOrStyleRegex();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"<\s*/?\s*(?:br|p|div|li|ul|ol|h[1-6]|tr)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMs)]
    private static partial Regex BlockBreakRegex();
}
