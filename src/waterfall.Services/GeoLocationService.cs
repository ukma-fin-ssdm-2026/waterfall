// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json;
using System.Text.RegularExpressions;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Geo;

namespace Waterfall.Services;

/// <summary>
/// Реалізація сервісу геокодування:
/// 1. Основний пошук координат виконується через Photon API із затримкою ~1 секунда між запитами
///    для запобігання таймаутам та rate-limit; усі результати Photon кешуються в базу даних (GeoLocationCache).
/// 2. Запасний варіант (fallback) — Wikipedia API; результати з Вікіпедії в базу НЕ кешуються.
/// </summary>
public class GeoLocationService : IGeoLocationService
{
    private static readonly SemaphoreSlim PhotonSemaphore = new(1, 1);

    private readonly IGeoLocationCacheRepository _cacheRepository;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _photonDelay;

    public GeoLocationService(
        IGeoLocationCacheRepository cacheRepository,
        HttpClient? httpClient = null,
        TimeSpan? photonDelay = null)
    {
        _cacheRepository = cacheRepository;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15.0) };
        _photonDelay = photonDelay ?? TimeSpan.FromSeconds(1.0);

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

        // 1. Перевірка постійного кешу в базі даних
        GeoLocationCacheDB? cached = await _cacheRepository.GetByAddressAsync(address, ct);
        if (cached != null)
        {
            return (cached.Latitude, cached.Longitude);
        }

        List<string> candidates = GenerateQueryCandidates(address).ToList();

        // 2. Основний пошук через Photon API (із затримкою ~1 сек та збереженням усіх результатів у БД)
        (double Lat, double Lon)? photonCoords = await TryPhotonGeocodeAsync(address, candidates, ct);
        if (photonCoords.HasValue)
        {
            return photonCoords.Value;
        }

        // 3. Запасний варіант (fallback) через Wikipedia API — результати НЕ кешуються в БД
        (double Lat, double Lon)? wikiCoords = await TryWikipediaGeocodeAsync(address, candidates, ct);
        if (wikiCoords.HasValue)
        {
            return wikiCoords.Value;
        }

        return null;
    }

    public async Task<GeoSyncResponseDto> SyncCacheAsync(GeoSyncRequestDto request, CancellationToken ct = default)
    {
        if (request?.ClientItems != null && request.ClientItems.Count > 0)
        {
            foreach (var item in request.ClientItems)
            {
                if (!string.IsNullOrWhiteSpace(item.Address))
                {
                    await _cacheRepository.SetCoordinatesAsync(item.Address, item.Latitude, item.Longitude, ct);
                }
            }
        }

        IReadOnlyList<GeoLocationCacheDB> allServerCached = await _cacheRepository.GetAllCachedAsync(ct);
        List<GeoSyncItemDto> responseItems = allServerCached.Select(c => new GeoSyncItemDto
        {
            Id = c.Id,
            Address = c.NormalizedAddress,
            Latitude = c.Latitude,
            Longitude = c.Longitude,
            CreatedAt = c.CreatedAt
        }).ToList();

        return new GeoSyncResponseDto
        {
            ServerItems = responseItems
        };
    }

    private async Task<(double Lat, double Lon)?> TryPhotonGeocodeAsync(
        string originalAddress,
        IReadOnlyList<string> candidates,
        CancellationToken ct)
    {
        foreach (string candidate in candidates)
        {
            try
            {
                string json;
                await PhotonSemaphore.WaitAsync(ct);
                try
                {
                    if (_photonDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(_photonDelay, ct);
                    }

                    string url = $"https://photon.komoot.io/api/?q={Uri.EscapeDataString(candidate)}&limit=5";
                    json = await _httpClient.GetStringAsync(url, ct);
                }
                finally
                {
                    PhotonSemaphore.Release();
                }

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("features", out JsonElement featuresElem) &&
                    featuresElem.ValueKind == JsonValueKind.Array)
                {
                    var matches = new List<(int Index, string Title, bool IsUa, double Lat, double Lon)>();
                    int idx = 0;

                    foreach (JsonElement feature in featuresElem.EnumerateArray())
                    {
                        idx++;
                        if (!feature.TryGetProperty("geometry", out JsonElement geomElem) ||
                            !geomElem.TryGetProperty("coordinates", out JsonElement coordsElem) ||
                            coordsElem.ValueKind != JsonValueKind.Array ||
                            coordsElem.GetArrayLength() < 2)
                        {
                            continue;
                        }

                        if (!coordsElem[0].TryGetDouble(out double lon) ||
                            !coordsElem[1].TryGetDouble(out double lat))
                        {
                            continue;
                        }

                        string title = string.Empty;
                        bool isUa = true;

                        if (feature.TryGetProperty("properties", out JsonElement propsElem) &&
                            propsElem.ValueKind == JsonValueKind.Object)
                        {
                            string? name = GetStringProperty(propsElem, "name");
                            string? street = GetStringProperty(propsElem, "street");
                            string? house = GetStringProperty(propsElem, "housenumber");
                            string? city = GetStringProperty(propsElem, "city")
                                ?? GetStringProperty(propsElem, "town")
                                ?? GetStringProperty(propsElem, "village")
                                ?? GetStringProperty(propsElem, "state");
                            string? countryCode = GetStringProperty(propsElem, "countrycode");

                            if (!string.IsNullOrWhiteSpace(countryCode))
                            {
                                isUa = string.Equals(countryCode, "UA", StringComparison.OrdinalIgnoreCase);
                            }

                            title = FormatPhotonFeatureAddress(name, street, house, city);
                        }

                        matches.Add((idx, title, isUa, lat, lon));
                    }

                    if (matches.Count > 0)
                    {
                        // Кешуємо всі валідні результати Photon у базу даних (без конфлікту міст)
                        foreach (var match in matches)
                        {
                            if (!string.IsNullOrWhiteSpace(match.Title) &&
                                !IsCityConflict(originalAddress, match.Title))
                            {
                                await _cacheRepository.SetCoordinatesAsync(match.Title, match.Lat, match.Lon, ct);
                            }
                        }

                        var best = matches
                            .OrderBy(m => IsCityConflict(originalAddress, m.Title) ? 1 : 0)
                            .ThenBy(m => m.IsUa ? 0 : 1)
                            .ThenBy(m => m.Index)
                            .First();

                        await _cacheRepository.SetCoordinatesAsync(originalAddress, best.Lat, best.Lon, ct);
                        return (best.Lat, best.Lon);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Спробувати наступного кандидата або перейти до запасного варіанта (Wikipedia)
            }
        }

        return null;
    }

    private async Task<(double Lat, double Lon)?> TryWikipediaGeocodeAsync(
        string originalAddress,
        IReadOnlyList<string> candidates,
        CancellationToken ct)
    {
        foreach (string candidate in candidates)
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
                            .OrderBy(m => IsCityConflict(originalAddress, m.Title) ? 1 : 0)
                            .ThenBy(m => m.Index)
                            .First();

                        // Результати Вікіпедії НЕ кешуємо в базу даних (запасний варіант)
                        return (best.Lat, best.Lon);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Спробувати наступного кандидата
            }
        }

        return null;
    }

    private static string FormatPhotonFeatureAddress(string? name, string? street, string? house, string? city)
    {
        if (!string.IsNullOrWhiteSpace(street))
        {
            string streetWithHouse = string.IsNullOrWhiteSpace(house) ? street : $"{street}, {house}";
            return string.IsNullOrWhiteSpace(city) ? streetWithHouse : $"{city}, {streetWithHouse}";
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            return string.IsNullOrWhiteSpace(city) ? name : $"{city}, {name}";
        }

        return city ?? string.Empty;
    }

    private static string? GetStringProperty(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out JsonElement val) && val.ValueKind == JsonValueKind.String)
        {
            return val.GetString();
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
