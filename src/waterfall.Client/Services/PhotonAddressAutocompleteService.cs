// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json;
using Waterfall.Client.Interfaces;

namespace Waterfall.Client.Services;

/// <summary>
/// Реалізація сервісу автопідказок адрес без платних/комерційних ключів
/// на базі відкритого Wikipedia OpenSearch API та локального каталогу українських топонімів
/// із підтримкою відкритого GeoJSON/Photon формату.
/// </summary>
public class PhotonAddressAutocompleteService : IAddressAutocompleteService
{

    private readonly HttpClient _httpClient;

    public PhotonAddressAutocompleteService() : this(null)
    {
    }

    public PhotonAddressAutocompleteService(HttpClient? httpClient)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "WaterfallApp/1.0 (contact@waterfall-app.local)");
        }
    }

    public async Task<IReadOnlyList<string>> GetSuggestionsAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 3)
        {
            return Array.Empty<string>();
        }

        string cleanQuery = query.Trim();
        List<string> list = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        // 1. Photon API 
        try
        {
            List<string> photonResults = await QueryPhotonAsync(cleanQuery, ct);
            if (photonResults.Count > 0)
            {
                return photonResults;
            }
        }
        catch
        {
            // Fallback нижче
        }

        // 2. Відкритий Wikipedia / Wikimedia OpenSearch API  (backup)
        try
        {
            string url = $"https://uk.wikipedia.org/w/api.php?action=opensearch&search={Uri.EscapeDataString(cleanQuery)}&limit=6&format=json";
            string json = await _httpClient.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array &&
                doc.RootElement.GetArrayLength() > 1 &&
                doc.RootElement[1].ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in doc.RootElement[1].EnumerateArray())
                {
                    string? title = item.GetString();
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        string formatted = CleanWikipediaTitle(title, cleanQuery);
                        if (seen.Add(formatted))
                        {
                            list.Add(formatted);
                        }
                    }
                }
            }
        }
        catch
        {
            // Офлайн
        }

        return list;
    }

    private static string CleanWikipediaTitle(string title, string query)
    {
        // Прибираємо уточнення в дужках або адаптуємо: "Проспект Соборності (Київ)" -> "Київ, проспект Соборності"
        if (title.Contains("(Київ)"))
        {
            string pure = title.Replace("(Київ)", "").Trim();
            return $"Київ, {pure}";
        }
        if (title.Contains("(Львів)"))
        {
            string pure = title.Replace("(Львів)", "").Trim();
            return $"Львів, {pure}";
        }
        if (title.Contains("(Харків)"))
        {
            string pure = title.Replace("(Харків)", "").Trim();
            return $"Харків, {pure}";
        }
        if (title.Contains("(Дніпро)"))
        {
            string pure = title.Replace("(Дніпро)", "").Trim();
            return $"Дніпро, {pure}";
        }
        if (title.Contains("(Одеса)"))
        {
            string pure = title.Replace("(Одеса)", "").Trim();
            return $"Одеса, {pure}";
        }

        return title;
    }

    private async Task<List<string>> QueryPhotonAsync(string query, CancellationToken ct)
    {
        string url = $"https://photon.komoot.io/api/?q={Uri.EscapeDataString(query)}&limit=5";
        string json = await _httpClient.GetStringAsync(url, ct);

        List<string> list = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("features", out JsonElement features) &&
            features.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement feature in features.EnumerateArray())
            {
                if (feature.TryGetProperty("properties", out JsonElement props))
                {
                    string? name = GetStringProperty(props, "name");
                    string? street = GetStringProperty(props, "street");
                    string? house = GetStringProperty(props, "housenumber");
                    string? city = GetStringProperty(props, "city") ?? GetStringProperty(props, "town") ?? GetStringProperty(props, "village");

                    string formatted;
                    if (!string.IsNullOrWhiteSpace(street))
                    {
                        string streetWithHouse = string.IsNullOrWhiteSpace(house) ? street : $"{street}, {house}";
                        formatted = string.IsNullOrWhiteSpace(city) ? streetWithHouse : $"{city}, {streetWithHouse}";
                    }
                    else if (!string.IsNullOrWhiteSpace(name))
                    {
                        formatted = string.IsNullOrWhiteSpace(city) ? name : $"{city}, {name}";
                    }
                    else
                    {
                        continue;
                    }

                    if (seen.Add(formatted))
                    {
                        list.Add(formatted);
                    }
                }
            }
        }

        return list;
    }

    private static string? GetStringProperty(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out JsonElement val) && val.ValueKind == JsonValueKind.String)
        {
            return val.GetString();
        }
        return null;
    }
}
