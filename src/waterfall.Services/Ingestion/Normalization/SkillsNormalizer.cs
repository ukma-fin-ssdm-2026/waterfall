// AI: нормалізація навичок вакансій (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests, самоперевірка автора (§8)

namespace Waterfall.Services.Ingestion.Normalization;

/// <summary>
/// Розбиває рядок навичок на список, прибирає порожні й дублікати та зводить поширені синоніми
/// до однієї назви («k8s» → «Kubernetes»), щоб пошук і аналітика попиту не розщеплювали одну технологію.
/// </summary>
public static class SkillsNormalizer
{
    /// <summary>Ті самі межі, що й у валідації API для поля skills.</summary>
    public const int MaxSkills = 30;
    public const int MaxSkillLength = 50;

    private static readonly char[] Separators = [',', ';', '|', '•', '\n', '\r'];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["c#"] = "C#",
        ["c sharp"] = "C#",
        ["csharp"] = "C#",
        [".net"] = ".NET",
        ["dotnet"] = ".NET",
        ["asp.net core"] = "ASP.NET Core",
        ["asp .net core"] = "ASP.NET Core",
        ["js"] = "JavaScript",
        ["javascript"] = "JavaScript",
        ["ts"] = "TypeScript",
        ["typescript"] = "TypeScript",
        ["postgres"] = "PostgreSQL",
        ["postgresql"] = "PostgreSQL",
        ["k8s"] = "Kubernetes",
        ["kubernetes"] = "Kubernetes",
        ["mssql"] = "SQL Server",
        ["ms sql"] = "SQL Server",
        ["sql server"] = "SQL Server",
        ["react"] = "React",
        ["reactjs"] = "React",
        ["react.js"] = "React",
        ["node"] = "Node.js",
        ["nodejs"] = "Node.js",
        ["node.js"] = "Node.js"
    };

    public static IReadOnlyList<string> Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        List<string> skills = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string part in raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            string? cleaned = TextNormalizer.CleanLine(part)?.TrimEnd('.');
            if (string.IsNullOrEmpty(cleaned) || cleaned.Length > MaxSkillLength)
            {
                continue;
            }

            string skill = Aliases.TryGetValue(cleaned, out string? canonical) ? canonical : cleaned;
            if (seen.Add(skill))
            {
                skills.Add(skill);
            }

            if (skills.Count == MaxSkills)
            {
                break;
            }
        }

        return skills;
    }
}
