// AI: розбір robots.txt для фонового збору вакансій; перевірено: юніт-тести RobotsTxtTests на справжніх robots.txt djinni.co і work.ua, самоперевірка автора (§8)

using System.Text.RegularExpressions;

namespace Waterfall.Api.Workers;

/// <summary>
/// Правила robots.txt для загальних ботів («User-agent: *») за RFC 9309: «*» у правилі означає будь-які символи,
/// «$» у кінці — кінець адреси. Якщо адресі відповідають кілька правил, перемагає найдовше, а за рівної довжини — Allow.
/// </summary>
public sealed class RobotsTxt
{
    /// <summary>robots.txt немає (сайт відповів 4xx): за RFC 9309 дозволено все.</summary>
    public static readonly RobotsTxt AllowAll = new([]);

    private readonly List<Rule> _rules;

    private RobotsTxt(List<Rule> rules)
    {
        _rules = rules;
    }

    public static RobotsTxt Parse(string text)
    {
        List<Rule> rules = [];
        bool readingAgents = false;
        bool forAllBots = false;
        foreach (string line in text.Split('\n'))
        {
            string[] field = line.Split('#')[0].Split(':', 2, StringSplitOptions.TrimEntries);
            if (field.Length < 2)
            {
                continue;
            }

            if (field[0].Equals("User-agent", StringComparison.OrdinalIgnoreCase))
            {
                // Кілька рядків User-agent поспіль — одна група з правилами для всіх цих ботів
                forAllBots = (readingAgents && forAllBots) || field[1] == "*";
                readingAgents = true;
                continue;
            }

            readingAgents = false;
            bool allow = field[0].Equals("Allow", StringComparison.OrdinalIgnoreCase);
            bool disallow = field[0].Equals("Disallow", StringComparison.OrdinalIgnoreCase);
            // Порожній Disallow нічого не забороняє
            if (forAllBots && (allow || disallow) && field[1].Length > 0)
            {
                rules.Add(new Rule(allow, field[1].Length, ToRegex(field[1])));
            }
        }

        return new RobotsTxt(rules);
    }

    /// <summary>Чи можна ботам читати адресу: шлях разом із параметрами, наприклад «/jobs/?ss=1&amp;page=2».</summary>
    public bool IsAllowed(string pathAndQuery)
    {
        Rule? decisive = null;
        foreach (Rule rule in _rules.Where(rule => rule.Pattern.IsMatch(pathAndQuery)))
        {
            if (decisive is null || rule.Length > decisive.Length || (rule.Length == decisive.Length && rule.Allow))
            {
                decisive = rule;
            }
        }

        return decisive?.Allow ?? true;
    }

    // «/*/?*sort=» → ^/.*/\?.*sort=, «/resumes/$» → ^/resumes/$.
    // Правила пише сайт, тож NonBacktracking: перевірка лінійна за довжиною адреси, хоч би скільки «*» було в правилі.
    private static Regex ToRegex(string rule)
    {
        bool toEnd = rule.EndsWith('$');
        string[] parts = (toEnd ? rule[..^1] : rule).Split('*');
        string pattern = "^" + string.Join(".*", parts.Select(Regex.Escape)) + (toEnd ? "$" : string.Empty);
        return new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    }

    private sealed record Rule(bool Allow, int Length, Regex Pattern);
}
