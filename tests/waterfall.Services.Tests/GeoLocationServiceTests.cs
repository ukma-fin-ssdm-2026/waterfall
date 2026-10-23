// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Net;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services;

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
    public async Task GetCoordinatesAsync_CallsHttpAndCaches_WhenNotInCache()
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

        FakeHttpHandler handler = new(wikiJson);
        HttpClient httpClient = new(handler);
        GeoLocationService service = new(cache, httpClient);

        (double Latitude, double Longitude)? result = await service.GetCoordinatesAsync("вул. Мечникова, 2 (БЦ Парус)");

        Assert.NotNull(result);
        Assert.Equal(50.4372, result.Value.Latitude);
        Assert.Equal(30.5276, result.Value.Longitude);

        // Verify it was cached in repository
        GeoLocationCacheDB? inCache = await cache.GetByAddressAsync("вул. Мечникова, 2 (БЦ Парус)");
        Assert.NotNull(inCache);
        Assert.Equal(50.4372, inCache.Latitude);
    }

    [Fact]
    public async Task GetCoordinatesAsync_ReturnsNull_WhenWikipediaHasNoCoordinates()
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
        GeoLocationService service = new(cache, httpClient);

        (double Latitude, double Longitude)? result = await service.GetCoordinatesAsync("Невідома локація 123456");

        Assert.Null(result);
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
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly string _response;

        public FakeHttpHandler(string response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage msg = new(HttpStatusCode.OK)
            {
                Content = new StringContent(_response)
            };
            return Task.FromResult(msg);
        }
    }
}
