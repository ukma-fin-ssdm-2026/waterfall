// AI: розпізнавання сторінки перевірки Cloudflare незалежно від мови браузера (issue #62); перевірено: тести RobotaUaParserTests, ручна перевірка robota.ua 14.11.2026, самоперевірка автора (§8)

using HtmlAgilityPack;

namespace Waterfall.Api.Workers;

/// <summary>
/// Сторінка перевірки на ботів (Cloudflare), яку сайт показує замість вмісту. Заголовок вкладки залежить
/// від мови браузера: «Just a moment...» англійською, «Трохи зачекайте…» українською, тож перевіряємо
/// і заголовок, і елементи самої перевірки (id challenge-* та cf-chl-*).
/// </summary>
public static class BotCheckPage
{
    private static readonly string[] Titles = ["just a moment", "трохи зачекайте", "один момент", "attention required"];

    /// <summary>
    /// JS-умова для браузера: true, коли на сторінці вже не перевірка. Звичайні сторінки Cloudflare теж може
    /// доповнювати своїми скриптами (/cdn-cgi/challenge-platform), тож за скриптами не судимо.
    /// </summary>
    public const string PassedScript = """
        () => {
            const title = (document.title || '').toLowerCase();
            const titles = ['just a moment', 'трохи зачекайте', 'один момент', 'attention required'];
            return !titles.some(t => title.includes(t))
                && !document.querySelector('[id^="challenge-"], [id^="cf-chl"]');
        }
        """;

    /// <summary>Чи це сторінка перевірки, а не вміст сайту.</summary>
    public static bool Is(HtmlNode document)
    {
        string title = (Html.Text(document, "//title") ?? string.Empty).ToLowerInvariant();
        return Titles.Any(title.Contains)
            || document.SelectSingleNode("//*[starts-with(@id, 'challenge-') or starts-with(@id, 'cf-chl')]") is not null;
    }
}
