// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Maui.Devices.Sensors;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Geo;
using Waterfall.Repository.Helpers;

namespace Waterfall.Client.Services;

/// <summary>
/// Сервіс розрахунку реальної дорожньої відстані між адресами:
/// 1. Основний пошук координат — через Photon API із затримкою ~1 секунда (щоб не йти в таймаут)
///    та збереженням усіх результатів Photon у кеш бази даних.
/// 2. Запасний варіант (fallback) пошуку координат — через Wikipedia API (результати Вікіпедії не кешуються).
/// 3. Побудова маршруту та розрахунок відстані між точками — через OSRM (Open Source Routing Machine),
///    а в офлайн-режимі — через Location.CalculateDistance із дорожнім коефіцієнтом.
/// </summary>
public class GeoDistanceService : IDistanceCalculatorService
{
    private static readonly ConcurrentDictionary<string, (double Lat, double Lon)> GeoCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ознаки вуличної адреси (вулиця, проспект, площа, будинок тощо).
    /// Якщо вони присутні — адреса вважається вуличною, а не міською.
    /// </summary>
    private static readonly string[] StreetMarkers =
    {
        "вул", "просп", "пр-т", "пр.", "пл.", "площ", "б-р", "бульв", "пров", "набережн",
        "шосе", "узвіз", "алея", "тупик", "мікрорайон", "мкр", "бц", "трц", "тц", "офіс",
        "метро", "буд", "st.", "str.", "street", "ave", "blvd", "sq."
    };

    /// <summary>
    /// Обласні центри України: канонічний ключ міста, координати центроїда та підрядки,
    /// за якими місто розпізнається в адресі (українська та англійська назви).
    /// </summary>
    private static readonly (string Key, double Lat, double Lon, string[] Aliases)[] CityCentroids =
    [
        ("київ", 50.4501, 30.5234, ["київ", "kyiv", "kiev"]),
        ("львів", 49.8397, 24.0297, ["львів", "lviv"]),
        ("харків", 49.9935, 36.2304, ["харків", "kharkiv"]),
        ("дніпро", 48.4647, 35.0462, ["дніпро", "dnipro"]),
        ("одеса", 46.4825, 30.7233, ["одеса", "odesa"]),
        ("запоріжжя", 47.8388, 35.1396, ["запоріж", "zaporizhzhia"]),
        ("вінниця", 49.2331, 28.4682, ["вінниц", "vinnytsia"]),
        ("полтава", 49.5883, 34.5514, ["полтав", "poltava"]),
        ("чернівці", 48.2921, 25.9358, ["чернівц", "chernivtsi"]),
        ("івано-франківськ", 48.9226, 24.7111, ["івано-франківськ", "frankivsk"]),
        ("тернопіль", 49.5535, 25.5948, ["тернопіль", "ternopil"]),
        ("ужгород", 48.6208, 22.2879, ["ужгород", "uzhhorod"]),
        ("луцьк", 50.7472, 25.3254, ["луцьк", "lutsk"]),
        ("рівне", 50.6199, 26.2516, ["рівне", "rivne"]),
        ("житомир", 50.2547, 28.6587, ["житомир", "zhytomyr"]),
        ("черкаси", 49.4444, 32.0598, ["черкас", "cherkasy"]),
        ("суми", 50.9077, 34.7981, ["суми", "sumy"]),
        ("чернігів", 51.4982, 31.2893, ["чернігів", "chernihiv"]),
        ("миколаїв", 46.9750, 31.9946, ["миколаїв", "mykolaiv"]),
        ("херсон", 46.6354, 32.6169, ["херсон", "kherson"]),
        ("кропивницький", 48.5079, 32.2623, ["кропивницьк", "kropyvnytskyi"]),
        ("хмельницький", 49.4230, 26.9871, ["хмельницьк", "khmelnytskyi"])
    ];

    private static readonly SemaphoreSlim PhotonSemaphore = new(1, 1);
    private static readonly SemaphoreSlim OsrmSemaphore = new(2, 2);

    private readonly ConcurrentDictionary<string, double> _routeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IHttpGatewayService? _gateway;
    private readonly ILocalGeoCacheService? _localCache;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _photonDelay;

    public GeoDistanceService() : this(null, null, null, null)
    {
    }

    public GeoDistanceService(IHttpGatewayService? gateway) : this(gateway, null, null, null)
    {
    }

    public GeoDistanceService(IHttpGatewayService? gateway, ILocalGeoCacheService? localCache) : this(gateway, null, null, localCache)
    {
    }

    public GeoDistanceService(IHttpGatewayService? gateway, HttpClient? httpClient, TimeSpan? photonDelay = null, ILocalGeoCacheService? localCache = null)
    {
        _gateway = gateway;
        _localCache = localCache;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15.0) };
        _photonDelay = photonDelay ?? TimeSpan.FromSeconds(1.0);
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "WaterfallApp/1.0 (contact@waterfall-app.local)");
        }
    }

    /// <summary>
    /// Адреса користувача (підтягується з таблиці персональних даних після входу в акаунт або введення в профілі).
    /// Поки користувач не увійшов — null (відстань не розраховується).
    /// </summary>
    public string? UserAddress { get; set; }

    public async Task<double?> CalculateDistanceKmAsync(string destinationAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(UserAddress) || string.IsNullOrWhiteSpace(destinationAddress))
        {
            return null;
        }

        // Для віддаленої роботи або якщо точну адресу не вказано — фізична відстань не рахується
        if (destinationAddress.Contains("Remote", StringComparison.OrdinalIgnoreCase) ||
            destinationAddress.Contains("Віддалено", StringComparison.OrdinalIgnoreCase) ||
            destinationAddress.Contains("не вказано точну адресу", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string normalizedOrigin = NormalizeAddressKey(UserAddress);
        string normalizedDestination = NormalizeAddressKey(destinationAddress);

        // Однакові адреси (наприклад, користувач знаходиться в тому ж офісі чи за тією ж адресою)
        if (string.Equals(normalizedOrigin, normalizedDestination, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(UserAddress.Trim(), destinationAddress.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return 0.1;
        }

        // 1. Отримуємо координати обох адрес (Photon як основний із кешуванням у БД, Wikipedia як запасний без кешування)
        (double Lat, double Lon)? originCoord = await ResolveCoordinatesAsync(UserAddress, ct).ConfigureAwait(false);
        (double Lat, double Lon)? destCoord = await ResolveCoordinatesAsync(destinationAddress, ct).ConfigureAwait(false);

        if (originCoord.HasValue && destCoord.HasValue)
        {
            if (originCoord.Value == destCoord.Value)
            {
                return 0.1;
            }

            // 2. З'єднуємо точки та шукаємо маршрут і реальну дорожню відстань через роутер OSRM.
            // Якщо хоча б одна точка — центроїд міста (не точна адреса), маршрутизація OSRM не дасть
            // коректного результату для «центр міста -> центр міста», тому одразу беремо геовідстань.
            bool originApproximate = IsCityCentroidResolution(UserAddress);
            bool destinationApproximate = IsCityCentroidResolution(destinationAddress);

            if (!originApproximate && !destinationApproximate)
            {
                double? routeKm = await TryOsrmRouteAsync(originCoord.Value, destCoord.Value, ct).ConfigureAwait(false);
                if (routeKm.HasValue)
                {
                    return routeKm.Value;
                }
            }

            // 3. Якщо офлайн або роутер недоступний — розрахунок геовідстані з дорожнім коефіцієнтом
            double roadDistance = CalculateRoadDistance(originCoord.Value, destCoord.Value);
            if (roadDistance > 0.0)
            {
                return roadDistance;
            }
        }

        return null;
    }

    public async Task<(double Lat, double Lon)?> ResolveCoordinatesAsync(string address, CancellationToken ct = default)
    {
        string norm = NormalizeAddressKey(address);
        if (GeoCache.TryGetValue(norm, out (double Lat, double Lon) cached))
        {
            return cached;
        }

        // 0. Перевірка локальної бази даних SQLite клієнта
        if (_localCache != null)
        {
            (double Lat, double Lon)? localDbCoords = await _localCache.GetCoordinatesAsync(address, ct).ConfigureAwait(false);
            if (localDbCoords.HasValue)
            {
                GeoCache[norm] = localDbCoords.Value;
                return localDbCoords.Value;
            }
        }

        // 1. Офлайн-довідник: точні координати відомих адрес (авторитетні, завжди) та центроїди міст
        //    (лише для адрес без вуличної деталізації — без цифр та ознак вулиці).
        if (TryGetApproximateCoordinate(norm, LooseKey(address), out (double Lat, double Lon) approximate))
        {
            GeoCache[norm] = approximate;
            if (_localCache != null)
            {
                await _localCache.SaveCoordinatesAsync(address, approximate.Lat, approximate.Lon, isSynced: false, ct).ConfigureAwait(false);
            }
            return approximate;
        }

        bool hadNetworkError = false;
        Exception? networkException = null;

        // 2. Звернення до сервера: перевірка БД (GeoLocationCache) або геокодування через Photon (з кешуванням у БД) / Wikipedia (fallback без кешування)
        if (_gateway != null)
        {
            try
            {
                CoordinatesDto? serverCoords = await _gateway.GetCoordinatesAsync(address, ct).ConfigureAwait(false);
                if (serverCoords != null)
                {
                    (double Lat, double Lon) res = (serverCoords.Latitude, serverCoords.Longitude);
                    GeoCache[norm] = res;
                    if (_localCache != null)
                    {
                        await _localCache.SaveCoordinatesAsync(address, res.Lat, res.Lon, isSynced: true, ct).ConfigureAwait(false);
                    }
                    return res;
                }
            }
            catch (Exception ex) when (IsNetworkException(ex))
            {
                hadNetworkError = true;
                networkException = ex;
            }
            catch
            {
                // Сервер повернув іншу помилку — переходимо до локального пошуку
            }
        }

        List<string> candidates = GeoAddressHelper.GenerateQueryCandidates(address).ToList();

        // 3. Основний пошук координат через Photon API із затримкою ~1 секунда (результати кешуються)
        foreach (string candidate in candidates)
        {
            try
            {
                string json;
                await PhotonSemaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (_photonDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(_photonDelay, ct).ConfigureAwait(false);
                    }

                    string url = $"https://photon.komoot.io/api/?q={Uri.EscapeDataString(candidate)}&limit=5";
                    json = await _httpClient.GetStringAsync(url, ct).ConfigureAwait(false);
                }
                finally
                {
                    PhotonSemaphore.Release();
                }

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("features", out JsonElement featuresElem) &&
                    featuresElem.ValueKind == JsonValueKind.Array)
                {
                    var matches = new List<(int Index, string Title, double Lat, double Lon)>();
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
                        if (feature.TryGetProperty("properties", out JsonElement propsElem) &&
                            propsElem.ValueKind == JsonValueKind.Object)
                        {
                            string? name = GeoAddressHelper.GetStringProperty(propsElem, "name");
                            string? street = GeoAddressHelper.GetStringProperty(propsElem, "street");
                            string? city = GeoAddressHelper.GetStringProperty(propsElem, "city")
                                ?? GeoAddressHelper.GetStringProperty(propsElem, "town")
                                ?? GeoAddressHelper.GetStringProperty(propsElem, "village")
                                ?? GeoAddressHelper.GetStringProperty(propsElem, "state");

                            title = $"{city} {street} {name}".Trim();
                        }

                        matches.Add((idx, title, lat, lon));
                    }

                    if (matches.Count > 0)
                    {
                        var best = matches
                            .OrderBy(m => GeoAddressHelper.IsCityConflict(address, m.Title) ? 1 : 0)
                            .ThenBy(m => m.Index)
                            .First();

                        (double Lat, double Lon) res = (best.Lat, best.Lon);
                        GeoCache[norm] = res;
                        if (_localCache != null)
                        {
                            await _localCache.SaveCoordinatesAsync(address, res.Lat, res.Lon, isSynced: false, ct).ConfigureAwait(false);
                        }
                        return res;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsNetworkException(ex))
            {
                hadNetworkError = true;
                networkException = ex;
            }
            catch
            {
                // Переходимо до наступного кандидата або Wikipedia fallback
            }
        }

        // 4. Запасний варіант (fallback): пошук координат через Wikipedia API (результати Вікіпедії НЕ кешуються!)
        foreach (string candidate in candidates)
        {
            try
            {
                string url = $"https://uk.wikipedia.org/w/api.php?action=query&generator=search&gsrsearch={Uri.EscapeDataString(candidate)}&gsrlimit=5&prop=coordinates&format=json";
                string json = await _httpClient.GetStringAsync(url, ct).ConfigureAwait(false);
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
                            .OrderBy(m => GeoAddressHelper.IsCityConflict(address, m.Title) ? 1 : 0)
                            .ThenBy(m => m.Index)
                            .First();

                        // Результати Вікіпедії НЕ кешуємо
                        return (best.Lat, best.Lon);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsNetworkException(ex))
            {
                hadNetworkError = true;
                networkException = ex;
            }
            catch
            {
                // Продовжуємо
            }
        }

        // 5. Офлайн-довідник обласних центрів для міського рівня
        (double Lat, double Lon)? cityCentroid = LookupCityCentroid(norm);
        if (cityCentroid.HasValue)
        {
            GeoCache[norm] = cityCentroid.Value;
            if (_localCache != null)
            {
                await _localCache.SaveCoordinatesAsync(address, cityCentroid.Value.Lat, cityCentroid.Value.Lon, isSynced: false, ct).ConfigureAwait(false);
            }
            return cityCentroid.Value;
        }

        // 6. Якщо координати не знайдено і виникла мережева помилка або немає мережі — кидаємо помилку мережі
        if (hadNetworkError || !System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
        {
            throw new HttpRequestException("Немає підключення до інтернету.", networkException);
        }

        return null;
    }

    /// <summary>
    /// Двостороння синхронізація локальної бази SQLite із сервером:
    /// вивантажує офлайн знайдені координати (IsSynced = false) на сервер,
    /// позначає їх як IsSynced = true, та завантажує актуальні координати із сервера у локальну SQLite базу.
    /// </summary>
    public async Task<int> SyncWithServerAsync(CancellationToken ct = default)
    {
        if (_gateway == null || _localCache == null)
        {
            return 0;
        }

        try
        {
            var unsynced = await _localCache.GetUnsyncedAsync(ct).ConfigureAwait(false);
            GeoSyncRequestDto request = new()
            {
                ClientItems = unsynced.Select(u => new GeoSyncItemDto
                {
                    Id = u.Id,
                    Address = u.NormalizedAddress,
                    Latitude = u.Latitude,
                    Longitude = u.Longitude,
                    CreatedAt = u.CreatedAt
                }).ToList()
            };

            GeoSyncResponseDto? response = await _gateway.SyncGeoCacheAsync(request, ct).ConfigureAwait(false);
            if (response == null)
            {
                return 0;
            }

            if (unsynced.Count > 0)
            {
                await _localCache.MarkAsSyncedAsync(unsynced.Select(u => u.Id), ct).ConfigureAwait(false);
            }

            if (response.ServerItems != null && response.ServerItems.Count > 0)
            {
                await _localCache.UpsertFromServerAsync(response.ServerItems, ct).ConfigureAwait(false);

                foreach (GeoSyncItemDto item in response.ServerItems)
                {
                    if (!string.IsNullOrWhiteSpace(item.Address))
                    {
                        string norm = NormalizeAddressKey(item.Address);
                        GeoCache[norm] = (item.Latitude, item.Longitude);
                    }
                }
            }

            return (response.ServerItems?.Count ?? 0) + unsynced.Count;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<double?> TryOsrmRouteAsync((double Lat, double Lon) origin, (double Lat, double Lon) destination, CancellationToken ct)
    {
        string oLat = origin.Lat.ToString("0.00000", CultureInfo.InvariantCulture);
        string oLon = origin.Lon.ToString("0.00000", CultureInfo.InvariantCulture);
        string dLat = destination.Lat.ToString("0.00000", CultureInfo.InvariantCulture);
        string dLon = destination.Lon.ToString("0.00000", CultureInfo.InvariantCulture);

        string routeKey = $"{oLat},{oLon};{dLat},{dLon}";
        if (_routeCache.TryGetValue(routeKey, out double cachedKm))
        {
            return cachedKm;
        }

        await OsrmSemaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_routeCache.TryGetValue(routeKey, out cachedKm))
            {
                return cachedKm;
            }

            string url = $"https://router.project-osrm.org/route/v1/driving/{oLon},{oLat};{dLon},{dLat}?overview=false";
            string json = await _httpClient.GetStringAsync(url, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("routes", out JsonElement routes) &&
                routes.ValueKind == JsonValueKind.Array &&
                routes.GetArrayLength() > 0 &&
                routes[0].TryGetProperty("distance", out JsonElement distanceElem))
            {
                double meters = distanceElem.GetDouble();
                double km = Math.Round(meters / 1000.0, 1);
                _routeCache[routeKey] = km;
                return km;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Офлайн-розрахунок стандартним алгоритмом C# нижче
        }
        finally
        {
            OsrmSemaphore.Release();
        }

        return null;
    }



    private static double CalculateRoadDistance((double Lat, double Lon) c1, (double Lat, double Lon) c2)
    {
        double straightKm = Location.CalculateDistance(c1.Lat, c1.Lon, c2.Lat, c2.Lon, DistanceUnits.Kilometers);

        // Коефіцієнт реальної дорожньої мережі (перетин річок / мостів у містах дає коефіцієнт ~1.38x)
        bool crossesDnipro = (c1.Lon < 30.56 && c2.Lon > 30.56) || (c1.Lon > 30.56 && c2.Lon < 30.56);
        double roadFactor = crossesDnipro ? 1.38 : 1.30;

        return Math.Round(straightKm * roadFactor, 1);
    }

    internal static string NormalizeAddressKey(string address) =>
        address.Trim().ToLowerInvariant().Replace("вул.", "").Replace("проспект", "").Replace("пл.", "");

    /// <summary>
    /// «Сирий» ключ без видалення слів «вул.», «проспект», «пл.»: перевірка вуличної деталізації
    /// має бачити ці ознаки, бо NormalizeAddressKey їх вирізає.
    /// </summary>
    private static string LooseKey(string address) =>
        address.Trim().ToLowerInvariant();

    /// <summary>
    /// Канонічний ключ міста з адреси (наприклад, «київ» для «Київ, вул. Хрещатик 22»),
    /// або <c>null</c>, якщо жодного обласного центру з таблиці центроїдів в адресі немає
    /// (передмістя, іноземна чи порожня адреса). Вулична деталізація не заважає.
    /// </summary>
    public static string? ExtractCityKey(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        (string Key, double Lat, double Lon)? match = MatchCityCentroid(NormalizeAddressKey(address));
        return match is null ? null : match.Value.Key;
    }

    /// <summary>
    /// Пошук канонічного ключа та центроїда міста за підрядками тієї самої таблиці,
    /// яку використовує офлайн-розв'язання координат.
    /// </summary>
    private static (string Key, double Lat, double Lon)? MatchCityCentroid(string norm)
    {
        foreach ((string key, double lat, double lon, string[] aliases) in CityCentroids)
        {
            foreach (string alias in aliases)
            {
                if (norm.Contains(alias, StringComparison.Ordinal))
                {
                    return (key, lat, lon);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Чи містить нормалізована адреса вуличну деталізацію (номер будинку або ознаку вулиці).
    /// Такі адреси потребують точного геокодування, центроїд міста для них не застосовується.
    /// </summary>
    private static bool HasStreetDetail(string norm)
    {
        foreach (char c in norm)
        {
            if (char.IsDigit(c))
            {
                return true;
            }
        }

        foreach (string marker in StreetMarkers)
        {
            if (norm.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Чи буде адреса розв'язана саме як центроїд міста (міська адреса без вуличної деталізації).
    /// Точні координати з довідника відомих адрес сюди НЕ входять — вони вважаються точними.
    /// </summary>
    private static bool IsCityCentroidResolution(string address) =>
        !HasStreetDetail(LooseKey(address)) && LookupCityCentroid(NormalizeAddressKey(address)).HasValue;

    /// <summary>
    /// Офлайн-пошук координат без жодного мережевого запиту:
    /// спочатку точний довідник відомих адрес (авторитетний), потім — центроїд міста,
    /// але лише для адрес без вуличної деталізації.
    /// </summary>
    private static bool TryGetApproximateCoordinate(string norm, string looseNorm, out (double Lat, double Lon) coords)
    {
        (double Lat, double Lon)? known = LookupKnownLocation(norm);
        if (known.HasValue)
        {
            coords = known.Value;
            return true;
        }

        if (!HasStreetDetail(looseNorm))
        {
            (double Lat, double Lon)? city = LookupCityCentroid(norm);
            if (city.HasValue)
            {
                coords = city.Value;
                return true;
            }
        }

        coords = default;
        return false;
    }



    /// <summary>
    /// Локальний довідник точних координат для топових офісних адрес вакансій та популярних адрес користувачів.
    /// Забезпечує миттєвий результат без мережевих затримок та 100% надійність.
    /// </summary>
    internal static (double Lat, double Lon)? LookupKnownLocation(string norm)
    {
        // --- КИЇВ: Бізнес-центри, IT-кластери та ключові магістралі ---

        // Проспект Соборності (Дарницька пл. / Русанівка / Лівий берег Києва)
        if (norm.Contains("соборност") || norm.Contains("возз'єднан"))
        {
            return (50.4421, 30.6250);
        }

        // Спортивна площа / ТРЦ Гулівер (Печерськ, Палац Спорту)
        if (norm.Contains("спортивн") || norm.Contains("гулівер") || norm.Contains("gulliver"))
        {
            return (50.4378, 30.5220);
        }

        // Вул. Мечникова / БЦ Парус (Кловська / Печерськ)
        if (norm.Contains("мечников") || norm.Contains("парус") || norm.Contains("parus"))
        {
            return (50.4372, 30.5276);
        }

        // Вул. Семена Скляренка (Куренівка, Київ)
        if (norm.Contains("скляренк"))
        {
            return (50.4851, 30.4721);
        }

        // Проспект Академіка Глушкова (ВДНГ / Теремки, Київ)
        if (norm.Contains("глушков") || norm.Contains("теремк") || norm.Contains("вднг"))
        {
            return (50.3780, 30.4682);
        }

        // Вул. Велика Васильківська (Центр / Олімпійська, Київ)
        if (norm.Contains("васильківськ"))
        {
            return (50.4320, 30.5165);
        }

        // Вул. Хрещатик (Центр Києва / Майдан)
        if (norm.Contains("хрещатик"))
        {
            return (50.4472, 30.5226);
        }

        // Вул. Жилянська (БЦ Eurasia, БЦ Prime, Центр)
        if (norm.Contains("жилянськ") || norm.Contains("eurasia") || norm.Contains("євразія"))
        {
            return (50.4365, 30.5020);
        }

        // Вул. Саксаганського
        if (norm.Contains("саксаганськ"))
        {
            return (50.4360, 30.5050);
        }

        // Вул. Володимирська (Золоті Ворота / Оперний театр)
        if (norm.Contains("володимирськ"))
        {
            return (50.4460, 30.5160);
        }

        // Вул. Ярославів Вал
        if (norm.Contains("ярославів вал") || norm.Contains("ярославов вал"))
        {
            return (50.4510, 30.5110);
        }

        // Вул. Дорогожицька / IT-парк UNIT.City
        if (norm.Contains("дорогожицьк") || norm.Contains("unit.city") || norm.Contains("юніт"))
        {
            return (50.4680, 30.4620);
        }

        // Протасів Яр / вул. Миколи Грінченка / вул. Миколи Амосова (БЦ Horizon Park)
        if (norm.Contains("протасів") || norm.Contains("грінченк") || norm.Contains("амосов") || norm.Contains("horizon"))
        {
            return (50.4150, 30.5150);
        }

        // Вул. Басейна / Бессарабська площа (Arena City)
        if (norm.Contains("басейн") || norm.Contains("бессараб") || norm.Contains("arena"))
        {
            return (50.4415, 30.5225);
        }

        // Вул. Антоновича (Горького) / Либідська / Ocean Plaza
        if (norm.Contains("антонович") || norm.Contains("горьког") || norm.Contains("либідськ") || norm.Contains("ocean plaza"))
        {
            return (50.4130, 30.5240);
        }

        // Поділ: Контрактова площа / Верхній Вал / Нижній Вал
        if (norm.Contains("контрактов") || norm.Contains("нижній вал") || norm.Contains("верхній вал") || norm.Contains("поділ"))
        {
            return (50.4670, 30.5130);
        }

        // Поділ: вул. Петра Сагайдачного / Поштова площа
        if (norm.Contains("сагайдачн") || norm.Contains("поштов"))
        {
            return (50.4600, 30.5230);
        }

        // Проспект Перемоги / Берестейський проспект (Шулявка / КПІ / Житомирська)
        if (norm.Contains("перемоги") || norm.Contains("берестейськ") || norm.Contains("шулявк"))
        {
            return (50.4540, 30.4480);
        }

        // Вул. Борщагівська / Політехнічна (КПІ)
        if (norm.Contains("борщагівськ") || norm.Contains("політехнічн") || norm.Contains("кпі"))
        {
            return (50.4460, 30.4570);
        }

        // Вул. Польова
        if (norm.Contains("польов"))
        {
            return (50.4430, 30.4480);
        }

        // Вул. Дегтярівська (Лук'янівка)
        if (norm.Contains("дегтярівськ") || norm.Contains("лук'янівк"))
        {
            return (50.4620, 30.4810);
        }

        // Оболонь / Оболонський проспект / Мінська / Героїв Дніпра (Dream Town)
        if (norm.Contains("оболон") || norm.Contains("dream") || norm.Contains("мінськ"))
        {
            return (50.5010, 30.4980);
        }

        // Проспект Степана Бандери / Почайна (Петрівка)
        if (norm.Contains("бандер") || norm.Contains("почайн") || norm.Contains("петрівк"))
        {
            return (50.4890, 30.4980);
        }

        // Академмістечко / проспект Академіка Палладіна
        if (norm.Contains("академмістечк") || norm.Contains("палладін"))
        {
            return (50.4640, 30.3550);
        }

        // Лівий берег: Позняки / Осокорки / Харківський масив / Лівобережна
        if (norm.Contains("позняк") || norm.Contains("осокорк") || norm.Contains("харківськ") || norm.Contains("лівобережн"))
        {
            return (50.4020, 30.6350);
        }

        // --- ЛЬВІВ ---

        // Вул. Садова (SoftServe HQ)
        if (norm.Contains("садов"))
        {
            return (49.8225, 23.9855);
        }

        // Вул. Наукова (Львівський IT-кластер)
        if (norm.Contains("науков"))
        {
            return (49.8020, 23.9930);
        }

        // Вул. Героїв УПА
        if (norm.Contains("героїв упа"))
        {
            return (49.8290, 23.9980);
        }

        // Вул. Кульпарківська
        if (norm.Contains("кульпарківськ"))
        {
            return (49.8160, 23.9870);
        }

        // Вул. Стрийська
        if (norm.Contains("стрийськ"))
        {
            return (49.8050, 24.0180);
        }

        // Площа Ринок / Центр Львова
        if (norm.Contains("площа ринок") || norm.Contains("пл. ринок"))
        {
            return (49.8418, 24.0315);
        }

        // --- ДНІПРО ---

        // Проспект Дмитра Яворницького
        if (norm.Contains("яворницьк"))
        {
            return (48.4715, 35.0310);
        }

        // Вул. Січових Стрільців
        if (norm.Contains("січових стрільц"))
        {
            return (48.4550, 35.0430);
        }

        // Набережна Перемоги
        if (norm.Contains("набережна перемоги") || norm.Contains("набережн"))
        {
            return (48.4350, 35.0760);
        }

        // --- ОДЕСА ---

        // Вул. Дерибасівська (Центр Одеси)
        if (norm.Contains("дерибасівськ"))
        {
            return (46.4850, 30.7420);
        }

        // Вул. Середньофонтанська
        if (norm.Contains("середньофонтанськ"))
        {
            return (46.4600, 30.7390);
        }

        // Французький бульвар
        if (norm.Contains("французьк"))
        {
            return (46.4560, 30.7580);
        }

        // --- ХАРКІВ ---

        // Вул. Сумська
        if (norm.Contains("сумськ"))
        {
            return (50.0010, 36.2340);
        }

        // Вул. Клочківська
        if (norm.Contains("клочківськ"))
        {
            return (50.0120, 36.2160);
        }

        // Вул. Григорія Сковороди / Пушкінська
        if (norm.Contains("скоровород") || norm.Contains("пушкінськ"))
        {
            return (50.0030, 36.2480);
        }

        return null;
    }

    /// <summary>
    /// Офлайн-центроїди обласних центрів України для випадків відсутності зв'язку
    /// або коли вказано лише місто без вулиці.
    /// </summary>
    private static (double Lat, double Lon)? LookupCityCentroid(string norm)
    {
        (string Key, double Lat, double Lon)? match = MatchCityCentroid(norm);
        if (match is null)
        {
            return null;
        }

        return (match.Value.Lat, match.Value.Lon);
    }

    private static bool IsNetworkException(Exception ex) =>
        ex is HttpRequestException ||
        ex is System.Net.Sockets.SocketException ||
        (ex is TaskCanceledException tc && !tc.CancellationToken.IsCancellationRequested) ||
        ex.InnerException is HttpRequestException ||
        ex.InnerException is System.Net.Sockets.SocketException ||
        (ex is ApiException apiEx && apiEx.InnerException is HttpRequestException);
}
