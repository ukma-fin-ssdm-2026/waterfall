// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json;
using System.Text.RegularExpressions;

namespace Waterfall.Repository.Helpers;

/// <summary>
/// Загальні допоміжні методи нормалізації та обробки географічних адрес,
/// спільні для клієнтського та серверного модулів геокодування.
/// </summary>
public static partial class GeoAddressHelper
{
    public static readonly string[] KnownCities =
    [
        "київ", "львів", "харків", "дніпро", "одеса", "запоріж", "вінниц", "полтав",
        "чернівц", "тернопіль", "івано-франківськ", "ужгород", "луцьк", "рівне",
        "житомир", "черкас", "суми", "чернігів", "миколаїв", "херсон"
    ];

    [GeneratedRegex(@"\([^)]*\)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ParenthesesRegex();

    [GeneratedRegex(@"\b(БЦ|ТРЦ|ТОЦ|офіс|оф\.|поверх|корпус|корп\.)\b[^,]*", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NoiseRegex();

    [GeneratedRegex(@",\s*,+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CommasRegex();

    /// <summary>
    /// Генерує список кандидатів для геопошуку через Photon або Wikipedia.
    /// </summary>
    public static IEnumerable<string> GenerateQueryCandidates(string address)
    {
        string noParens = ParenthesesRegex().Replace(address, "").Trim();
        string cleanNoise = NoiseRegex().Replace(noParens, "").Trim();
        cleanNoise = CommasRegex().Replace(cleanNoise, ",").Trim(' ', ',');

        if (!string.IsNullOrWhiteSpace(cleanNoise))
        {
            yield return cleanNoise;
        }

        if (!string.Equals(cleanNoise, address, StringComparison.OrdinalIgnoreCase))
        {
            yield return address;
        }

        string[] parts = cleanNoise.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 2)
        {
            yield return $"{parts[0]}, {parts[1]}";
        }

        if (parts.Length > 1)
        {
            yield return parts[0];
        }
    }

    /// <summary>
    /// Перевіряє наявність конфлікту міст між шуканою адресою та знайденим заголовком об'єкта.
    /// </summary>
    public static bool IsCityConflict(string address, string pageTitle)
    {
        string normAddr = address.ToLowerInvariant();
        string normTitle = pageTitle.ToLowerInvariant();

        string? addrCity = KnownCities.FirstOrDefault(c => normAddr.Contains(c));
        if (addrCity == null)
        {
            return false;
        }

        bool titleHasOtherCity = KnownCities.Any(c => c != addrCity && normTitle.Contains(c));
        bool titleHasSameCity = normTitle.Contains(addrCity);

        return titleHasOtherCity && !titleHasSameCity;
    }

    /// <summary>
    /// Допоміжний метод читання рядкової властивості з JsonElement.
    /// </summary>
    public static string? GetStringProperty(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out JsonElement val) && val.ValueKind == JsonValueKind.String)
        {
            return val.GetString();
        }

        return null;
    }
}
