// AI: спільні помічники розбору HTML для парсерів сайтів, винесені з WorkUaParser, DouParser і DjinniParser (Лаб 8, Extract Class); перевірено: юніт-тести WorkUaPageTests, DouPageTests, DjinniPageTests, самоперевірка автора (§8)

using System.Net;
using HtmlAgilityPack;

namespace Waterfall.Api.Workers;

/// <summary>Розбір сторінки й витягання з неї тексту — однаково для всіх сайтів.</summary>
internal static class Html
{
    public static HtmlDocument Load(string html)
    {
        HtmlDocument document = new();
        document.LoadHtml(html);
        return document;
    }

    /// <summary>Текст першого вузла за XPath; null — вузла немає або він порожній.</summary>
    public static string? Text(HtmlNode node, string xpath)
    {
        string text = node.SelectSingleNode(xpath) is { } found ? Clean(found) : string.Empty;
        return text.Length > 0 ? text : null;
    }

    // Шматки тексту з різних тегів розділяємо пробілом: «Повна зайнятість<span>·</span>Стандартний графік» не злипнеться
    public static string Clean(HtmlNode node) =>
        Clean(string.Join(' ', node.DescendantsAndSelf().Where(part => part.NodeType == HtmlNodeType.Text).Select(part => part.InnerText)));

    // Декодуємо HTML-сутності й зводимо будь-які пробіли та переноси рядків до одного пробілу
    public static string Clean(string text) => string.Join(' ', WebUtility.HtmlDecode(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
