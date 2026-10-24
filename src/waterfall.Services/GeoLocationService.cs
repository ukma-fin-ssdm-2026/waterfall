// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json;
using System.Text.RegularExpressions;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services.Interfaces;

namespace Waterfall.Services;

/// <summary>
/// Реалізація сервісу геокодування з перевіркою та збереженням у таблиці GeoLocationCache бази даних.
/// </summary>
public class GeoLocationService : IGeoLocationService
{
    private readonly IGeoLocationCacheRepository _cacheRepository;
    private readonly HttpClient _httpClient;

    public GeoLocationService(IGeoLocationCacheRepository cacheRepository, HttpClient? httpClient = null)
    {
        _cacheRepository = cacheRepository;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(3.0) };

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "WaterfallCareerApp/1.0 (contact@waterfall.local)");
        }
    }

    public async Task<(double Latitude, double Longitude)?> GetCoordinatesAsync(string address, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        GeoLocationCacheDB? cached = await _cacheRepository.GetByAddressAsync(address, ct);
        if (cached != null)
        {
            return (cached.Latitude, cached.Longitude);
        }

        foreach (string candidate in GenerateQueryCandidates(address))
        {
            try
            {
                string url = $"https://uk.wikipedia.org/w/api.php?action=query&generator=search&gsrsearch={Uri.EscapeDataString(candidate)}&gsrlimit=5&prop=coordinates&format=json";
                string json = await _httpClient.GetStringAsync(url, ct);

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("query", out JsonElement queryElem) &&
                    queryElem.TryGetProperty("pages", out JsonElement pagesElem) &&
                    pagesElem.ValueKind == JsonValueKind.Object)
                {
                    var matches = new List<(int Index, string Title, double Lat, double Lon)>();
                    foreach (JsonProperty pageProp in pagesElem.EnumerateObject())
                    {
                        int index = pageProp.Value.TryGetProperty("index", out JsonElement idxElem) && idxElem.TryGetInt32(out int idxVal)
                            ? idxVal
                            : int.MaxValue;
                        string title = pageProp.Value.TryGetProperty("title", out JsonElement titleElem)
                            ? (titleElem.GetString() ?? string.Empty)
                            : string.Empty;

                        if (pageProp.Value.TryGetProperty("coordinates", out JsonElement coordsArr) &&
                            coordsArr.ValueKind == JsonValueKind.Array &&
                            coordsArr.GetArrayLength() > 0)
                        {
                            JsonElement firstCoord = coordsArr[0];
                            if (firstCoord.TryGetProperty("lat", out JsonElement latElem) &&
                                firstCoord.TryGetProperty("lon", out JsonElement lonElem))
                            {
                                matches.Add((index, title, latElem.GetDouble(), lonElem.GetDouble()));
                            }
                        }
                    }

                    if (matches.Count > 0)
                    {
                        var best = matches
                            .OrderBy(m => IsCityConflict(address, m.Title) ? 1 : 0)
                            .ThenBy(m => m.Index)
                            .First();

                        await _cacheRepository.SetCoordinatesAsync(address, best.Lat, best.Lon, ct);
                        return (best.Lat, best.Lon);
                    }
                }
            }
            catch
            {
                // Спробувати наступного кандидата
            }
        }

        return null;
    }

    private static bool IsCityConflict(string address, string pageTitle)
    {
        string[] knownCities = ["київ", "львів", "харків", "дніпро", "одеса", "запоріж", "вінниц", "полтав", "чернівц", "тернопіль", "івано-франківськ", "ужгород", "луцьк", "рівне", "житомир", "черкас", "суми", "чернігів", "миколаїв", "херсон"];
        string normAddr = address.ToLowerInvariant();
        string normTitle = pageTitle.ToLowerInvariant();

        string? addrCity = knownCities.FirstOrDefault(c => normAddr.Contains(c));
        if (addrCity == null)
        {
            return false;
        }

        bool titleHasOtherCity = knownCities.Any(c => c != addrCity && normTitle.Contains(c));
        bool titleHasSameCity = normTitle.Contains(addrCity);

        return titleHasOtherCity && !titleHasSameCity;
    }

    private static IEnumerable<string> GenerateQueryCandidates(string address)
    {
        string noParens = Regex.Replace(address, @"\([^)]*\)", "").Trim();
        string cleanNoise = Regex.Replace(noParens, @"\b(БЦ|ТРЦ|ТОЦ|офіс|оф\.|поверх|корпус|корп\.)\b[^,]*", "", RegexOptions.IgnoreCase).Trim();
        cleanNoise = Regex.Replace(cleanNoise, @",\s*,+", ",").Trim(' ', ',');

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
}
