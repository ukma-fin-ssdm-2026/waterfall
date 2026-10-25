// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Net;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services;
using Waterfall.Services.Models.Geo;

namespace Waterfall.Services.Tests;

public class GeoLocationServiceTests
{
    [Fact]
    public async Task GetCoordinatesAsync_ReturnsNull_WhenAddressIsNullOrWhiteSpace()
    {
        FakeGeoCacheRepository cache = new();
        GeoLocationService service = new(cache);

        (double Latitude, double Longitude)? result1 = await service.GetCoordinatesAsync("");
        (double Latitude, double Longitude)? result2 = await service.GetCoordinatesAsync("   ");

        Assert.Null(result1);
        Assert.Null(result2);
    }

    [Fact]
    public async Task GetCoordinatesAsync_ReturnsCached_WithoutCallingHttp()
    {
        FakeGeoCacheRepository cache = new();
        await cache.SetCoordinatesAsync("Київ, вул. Мечникова, 2", 50.4372, 30.5276);

        // HttpClient without handler will throw if called, proving it relies on cache
        GeoLocationService service = new(cache);

        (double Latitude, double Longitude)? result = await service.GetCoordinatesAsync("Київ, вул. Мечникова, 2");

        Assert.NotNull(result);
        Assert.Equal(50.4372, result.Value.Latitude);
        Assert.Equal(30.5276, result.Value.Longitude);
    }

    [Fact]
    public async Task GetCoordinatesAsync_UsesPhotonPrimary_AndCachesAllPhotonResultsInDatabase()
    {
        FakeGeoCacheRepository cache = new();

        string photonJson = """
        {
            "type": "FeatureCollection",
            "features": [
                {
                    "type": "Feature",
                    "geometry": {
                        "coordinates": [30.5276, 50.4372],
                        "type": "Point"
                    },
                    "properties": {
                        "name": "БЦ Парус",
                        "street": "вулиця Мечникова",
                        "housenumber": "2",
                        "city": "Київ",
                        "countrycode": "UA"
                    }
                }
            ]
        }
        """;

        FakeHttpHandler handler = new(photonJson);
        HttpClient httpClient = new(handler);
        GeoLocationService service = new(cache, httpClient, TimeSpan.FromMilliseconds(10));

        (double Latitude, double Longitude)? result = await service.GetCoordinatesAsync("вул. Мечникова, 2 (БЦ Парус)");

        Assert.NotNull(result);
        Assert.Equal(50.4372, result.Value.Latitude);
        Assert.Equal(30.5276, result.Value.Longitude);

        // Перевіряємо, що результат Photon закешовано в базу даних
        GeoLocationCacheDB? inCache = await cache.GetByAddressAsync("вул. Мечникова, 2 (БЦ Парус)");
        Assert.NotNull(inCache);
        Assert.Equal(50.4372, inCache.Latitude);
        Assert.Equal(30.5276, inCache.Longitude);
    }

    [Fact]
    public async Task GetCoordinatesAsync_FallsBackToWikipedia_WhenPhotonFails_AndDoesNotCacheWikipediaResults()
    {
        FakeGeoCacheRepository cache = new();

        string wikiJson = """
        {
            "query": {
                "pages": {
                    "12345": {
                        "title": "Вулиця Мечникова (Київ)",
                        "coordinates": [
                            {
                                "lat": 50.4372,
                                "lon": 30.5276
                            }
                        ]
                    }
                }
            }
        }
        """;

        FakeHttpHandler handler = new(wikiJson, failPhoton: true);
        HttpClient httpClient = new(handler);
        GeoLocationService service = new(cache, httpClient, TimeSpan.FromMilliseconds(10));

        (double Latitude, double Longitude)? result = await service.GetCoordinatesAsync("вул. Мечникова, 2 (БЦ Парус)");

        Assert.NotNull(result);
        Assert.Equal(50.4372, result.Value.Latitude);
        Assert.Equal(30.5276, result.Value.Longitude);

        // Результати Вікіпедії НЕ повинні кешуватися в базу даних
        IReadOnlyList<GeoLocationCacheDB> cachedItems = await cache.GetAllCachedAsync();
        Assert.Empty(cachedItems);
    }

    [Fact]
    public async Task GetCoordinatesAsync_ReturnsNull_WhenNeitherPhotonNorWikipediaHasCoordinates()
    {
        FakeGeoCacheRepository cache = new();

        string emptyWikiJson = """
        {
            "query": {
                "pages": {
                    "9999": {
                        "title": "Деяка сторінка без координат"
                    }
                }
            }
        }
        """;

        FakeHttpHandler handler = new(emptyWikiJson);
        HttpClient httpClient = new(handler);
        GeoLocationService service = new(cache, httpClient, TimeSpan.FromMilliseconds(10));

        (double Latitude, double Longitude)? result = await service.GetCoordinatesAsync("Невідома локація 123456");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCoordinatesAsync_PicksBestIndexAndAvoidsCityConflict_WhenWikipediaReturnsMultiplePages()
    {
        FakeGeoCacheRepository cache = new();

        // Львів (вулиця Княгині Ольги, index 5) стоїть першим у словнику pages,
        // але для адреси "Київ, Спортивна площа, 1а" сервіс має обрати "Спортивна площа (Київ)" (index 1).
        string multiPageWikiJson = """
        {
            "query": {
                "pages": {
                    "1452521": {
                        "pageid": 1452521,
                        "title": "Вулиця Княгині Ольги (Львів)",
                        "index": 5,
                        "coordinates": [
                            {
                                "lat": 49.8266,
                                "lon": 24.0103
                            }
                        ]
                    },
                    "934621": {
                        "pageid": 934621,
                        "title": "Спортивна площа (Київ)",
                        "index": 1,
                        "coordinates": [
                            {
                                "lat": 50.4378,
                                "lon": 30.5220
                            }
                        ]
                    }
                }
            }
        }
        """;

        FakeHttpHandler handler = new(multiPageWikiJson, failPhoton: true);
        HttpClient httpClient = new(handler);
        GeoLocationService service = new(cache, httpClient, TimeSpan.FromMilliseconds(10));

        (double Latitude, double Longitude)? result = await service.GetCoordinatesAsync("Київ, Спортивна площа, 1а, БЦ Гулівер");

        Assert.NotNull(result);
        Assert.Equal(50.4378, result.Value.Latitude);
        Assert.Equal(30.5220, result.Value.Longitude);

        // Оскільки результат отримано з Вікіпедії (fallback), кеш бази залишається порожнім
        Assert.Empty(await cache.GetAllCachedAsync());
    }

    [Fact]
    public async Task SyncCacheAsync_UpsertsIncomingAndReturnsAll()
    {
        FakeGeoCacheRepository cache = new();
        await cache.SetCoordinatesAsync("Київ, Хрещатик", 50.4472, 30.5226);

        GeoLocationService service = new(cache);
        GeoSyncRequestDto request = new()
        {
            ClientItems = new List<GeoSyncItemDto>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Address = "Львів, Ринок",
                    Latitude = 49.8419,
                    Longitude = 24.0315,
                    CreatedAt = DateTimeOffset.UtcNow
                }
            }
        };

        GeoSyncResponseDto response = await service.SyncCacheAsync(request);

        Assert.NotNull(response);
        Assert.Equal(2, response.ServerItems.Count);
        Assert.Contains(response.ServerItems, x => x.Address == "Київ, Хрещатик");
        Assert.Contains(response.ServerItems, x => x.Address == "Львів, Ринок");
    }

    private sealed class FakeGeoCacheRepository : IGeoLocationCacheRepository
    {
        private readonly List<GeoLocationCacheDB> _items = new();

        public Task<IReadOnlyList<GeoLocationCacheDB>> GetAllCachedAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<GeoLocationCacheDB>>(_items.ToList());
        }

        public Task<GeoLocationCacheDB?> GetByAddressAsync(string address, CancellationToken ct = default)
        {
            GeoLocationCacheDB? item = _items.FirstOrDefault(x =>
                address.Contains(x.NormalizedAddress, StringComparison.OrdinalIgnoreCase) ||
                x.NormalizedAddress.Contains(address, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(item);
        }

        public Task<GeoLocationCacheDB> SetCoordinatesAsync(string address, double latitude, double longitude, CancellationToken ct = default)
        {
            GeoLocationCacheDB item = new()
            {
                Id = Guid.NewGuid(),
                NormalizedAddress = address.Trim(),
                Latitude = latitude,
                Longitude = longitude,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _items.Add(item);
            return Task.FromResult(item);
        }

        public Task UpsertRangeAsync(IEnumerable<GeoLocationCacheDB> items, CancellationToken ct = default)
        {
            foreach (GeoLocationCacheDB item in items)
            {
                GeoLocationCacheDB? existing = _items.FirstOrDefault(x => string.Equals(x.NormalizedAddress, item.NormalizedAddress, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Latitude = item.Latitude;
                    existing.Longitude = item.Longitude;
                    existing.CreatedAt = item.CreatedAt;
                }
                else
                {
                    _items.Add(item);
                }
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly string _response;
        private readonly bool _failPhoton;

        public FakeHttpHandler(string response, bool failPhoton = false)
        {
            _response = response;
            _failPhoton = failPhoton;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_failPhoton && request.RequestUri != null && request.RequestUri.Host.Contains("photon"))
            {
                throw new HttpRequestException("Photon error");
            }

            HttpResponseMessage msg = new(HttpStatusCode.OK)
            {
                Content = new StringContent(_response)
            };
            return Task.FromResult(msg);
        }
    }
}
