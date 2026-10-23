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
                    foreach (JsonProperty pageProp in pagesElem.EnumerateObject())
                    {
                        if (pageProp.Value.TryGetProperty("coordinates", out JsonElement coordsArr) &&
                            coordsArr.ValueKind == JsonValueKind.Array &&
                            coordsArr.GetArrayLength() > 0)
                        {
                            JsonElement firstCoord = coordsArr[0];
                            if (firstCoord.TryGetProperty("lat", out JsonElement latElem) &&
                                firstCoord.TryGetProperty("lon", out JsonElement lonElem))
                            {
                                double lat = latElem.GetDouble();
                                double lon = lonElem.GetDouble();


                                await _cacheRepository.SetCoordinatesAsync(address, lat, lon, ct);

                                return (lat, lon);
                            }
                        }
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
