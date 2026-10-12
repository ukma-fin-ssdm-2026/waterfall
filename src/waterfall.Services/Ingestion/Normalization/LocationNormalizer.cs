// AI: нормалізація локацій вакансій (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests, самоперевірка автора (§8)

namespace Waterfall.Services.Ingestion.Normalization;

/// <summary>
/// Зводить локацію до єдиного вигляду: «Kyiv, Remote» → «Київ (віддалено)», «Remote» → «Віддалено».
/// Назви міст уніфікуються, щоб фільтр каталогу за локацією знаходив вакансії незалежно від написання.
/// </summary>
public static class LocationNormalizer
{
    public const string NotSpecified = "Не вказано";
    public const string RemoteOnly = "Віддалено";
    public const int MaxLength = 200;

    private const string Ukraine = "Україна";

    private static readonly Dictionary<string, string> Places = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kyiv"] = "Київ",
        ["kiev"] = "Київ",
        ["київ"] = "Київ",
        ["киев"] = "Київ",
        ["lviv"] = "Львів",
        ["львів"] = "Львів",
        ["львов"] = "Львів",
        ["kharkiv"] = "Харків",
        ["харків"] = "Харків",
        ["харьков"] = "Харків",
        ["odesa"] = "Одеса",
        ["odessa"] = "Одеса",
        ["одеса"] = "Одеса",
        ["одесса"] = "Одеса",
        ["dnipro"] = "Дніпро",
        ["дніпро"] = "Дніпро",
        ["днепр"] = "Дніпро",
        ["ukraine"] = Ukraine,
        ["україна"] = Ukraine
    };

    private static readonly HashSet<string> RemoteMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "remote", "full remote", "fully remote", "remote only", "віддалено", "віддалена робота", "дистанційно"
    };

    private static readonly HashSet<string> HybridMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "hybrid", "гібрид", "гібридний формат"
    };

    private static readonly HashSet<string> OfficeMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "office", "офіс", "в офісі", "office only"
    };

    private static readonly char[] Separators = [',', '/', ';', '|', '(', ')', '·', '•'];

    public static string Normalize(string? raw)
    {
        string? text = TextNormalizer.CleanLine(raw);
        if (text is null)
        {
            return NotSpecified;
        }

        List<string> places = [];
        bool remote = false;
        bool hybrid = false;

        foreach (string part in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (RemoteMarkers.Contains(part))
            {
                remote = true;
            }
            else if (HybridMarkers.Contains(part))
            {
                hybrid = true;
            }
            else if (!OfficeMarkers.Contains(part))
            {
                string place = Places.TryGetValue(part, out string? known) ? known : part;
                if (!places.Contains(place, StringComparer.OrdinalIgnoreCase))
                {
                    places.Add(place);
                }
            }
        }

        // «Україна» поруч із конкретним містом нічого не уточнює
        if (places.Count > 1)
        {
            places.Remove(Ukraine);
        }

        if (places.Count == 0)
        {
            return remote ? RemoteOnly : NotSpecified;
        }

        string suffix = remote ? " (віддалено)" : hybrid ? " (гібрид)" : string.Empty;
        return TextNormalizer.Truncate(string.Join(", ", places) + suffix, MaxLength);
    }
}
