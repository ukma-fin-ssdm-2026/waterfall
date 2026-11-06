// AI: Тести ін'єкції збоїв (Fault Injection) зовнішніх залежностей (Lab 9, Домашнє завдання 2)
// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Net;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services;

namespace Waterfall.Reliability.Tests;

/// <summary>
/// Тести зовнішньої відмови: використання тестового дублера HttpMessageHandler
/// для імітації мережевих збоїв (обрив з'єднання, таймаут, 503 Service Unavailable, перевантаження семафора)
/// та перевірка реакції безпечної відмови (Safe Degradation / Fallback).
/// </summary>
public class FaultInjectionTests
{
    private const string KyivWikipediaJson = """
    {
        "query": {
            "pages": {
                "123": {
                    "index": 1,
                    "title": "Київ",
                    "coordinates": [
                        { "lat": 50.4501, "lon": 30.5234 }
                    ]
                }
            }
        }
    }
    """;

    [Fact]
    public async Task GeoLocationService_WhenPhotonThrowsConnectionRefused_FallsBackToWikipediaGracefully()
    {
        // Arrange - ін'єкція збою: Photon падає з Socket/Connection Refused
        FaultInjectingHttpHandler faultHandler = new(
            photonFault: new HttpRequestException("З'єднання розірвано віддаленим вузлом (Connection refused)."),
            wikiResponse: KyivWikipediaJson);

        using HttpClient httpClient = new(faultHandler);
        FakeGeoCacheRepository cache = new();
        GeoLocationService service = new(cache, httpClient, photonDelay: TimeSpan.Zero);

        // Act
        (double Latitude, double Longitude)? result = await service.GetCoordinatesAsync("Київ, вул. Хрещатик, 1");

        // Assert - система не впала, а безпечно отримала координати із запасного джерела (Wikipedia)
        Assert.NotNull(result);
        Assert.Equal(50.4501, result.Value.Latitude, precision: 4);
        Assert.Equal(30.5234, result.Value.Longitude, precision: 4);

        // Переконуємося, що ін'єкція збою дійсно спрацювала
        Assert.True(faultHandler.PhotonCallsCount > 0, "Запит до Photon мав відбутися перед переходом до fallback.");
        Assert.True(faultHandler.WikiCallsCount > 0, "Запит до Wikipedia повинен був виконатися як fallback.");
    }

    [Fact]
    public async Task GeoLocationService_WhenPhotonTimesOut_FallsBackToWikipediaGracefully()
    {
        // Arrange - ін'єкція збою: Photon перевищує таймаут (TaskCanceledException)
        FaultInjectingHttpHandler faultHandler = new(
            photonFault: new TaskCanceledException("Час очікування відповіді від Photon вичерпано (Timeout)."),
            wikiResponse: KyivWikipediaJson);

        using HttpClient httpClient = new(faultHandler);
        FakeGeoCacheRepository cache = new();
        GeoLocationService service = new(cache, httpClient, photonDelay: TimeSpan.Zero);

        // Act
        (double Latitude, double Longitude)? result = await service.GetCoordinatesAsync("Київ, вул. Хрещатик, 1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(50.4501, result.Value.Latitude, precision: 4);
        Assert.Equal(30.5234, result.Value.Longitude, precision: 4);
    }

    [Fact]
    public async Task GeoLocationService_WhenAllExternalServicesFail_ReturnsNullSafelyWithoutCrashing()
    {
        // Arrange - ін'єкція тотального збою мережі (і Photon, і Wikipedia недоступні)
        FaultInjectingHttpHandler faultHandler = new(
            photonFault: new HttpRequestException("503 Service Unavailable"),
            wikiFault: new HttpRequestException("DNS resolution failed for uk.wikipedia.org"));

        using HttpClient httpClient = new(faultHandler);
        FakeGeoCacheRepository cache = new();
        GeoLocationService service = new(cache, httpClient, photonDelay: TimeSpan.Zero);

        // Act - не повинно викидати неперехоплений виняток назовні
        (double Latitude, double Longitude)? result = await service.GetCoordinatesAsync("Львів");

        // Assert - безпечна деградація: повертається null
        Assert.Null(result);
    }

    /// <summary>
    /// Тестовий дублер HttpMessageHandler, що імітує вибіркове падіння окремих зовнішніх API.
    /// </summary>
    private sealed class FaultInjectingHttpHandler : HttpMessageHandler
    {
        private readonly Exception? _photonFault;
        private readonly Exception? _wikiFault;
        private readonly string? _photonResponse;
        private readonly string? _wikiResponse;

        public int PhotonCallsCount { get; private set; }
        public int WikiCallsCount { get; private set; }

        public FaultInjectingHttpHandler(
            Exception? photonFault = null,
            Exception? wikiFault = null,
            string? photonResponse = null,
            string? wikiResponse = null)
        {
            _photonFault = photonFault;
            _wikiFault = wikiFault;
            _photonResponse = photonResponse;
            _wikiResponse = wikiResponse;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string host = request.RequestUri?.Host ?? string.Empty;

            if (host.Contains("photon"))
            {
                PhotonCallsCount++;
                if (_photonFault != null)
                {
                    throw _photonFault;
                }

                HttpResponseMessage msg = new(HttpStatusCode.OK)
                {
                    Content = new StringContent(_photonResponse ?? "{\"type\":\"FeatureCollection\",\"features\":[]}")
                };
                return Task.FromResult(msg);
            }

            if (host.Contains("wikipedia"))
            {
                WikiCallsCount++;
                if (_wikiFault != null)
                {
                    throw _wikiFault;
                }

                HttpResponseMessage msg = new(HttpStatusCode.OK)
                {
                    Content = new StringContent(_wikiResponse ?? "{\"query\":{\"pages\":{}}}")
                };
                return Task.FromResult(msg);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FakeGeoCacheRepository : IGeoLocationCacheRepository
    {
        public Task<IReadOnlyList<GeoLocationCacheDB>> GetAllCachedAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeoLocationCacheDB>>(new List<GeoLocationCacheDB>());

        public Task<GeoLocationCacheDB?> GetByAddressAsync(string address, CancellationToken ct = default) =>
            Task.FromResult<GeoLocationCacheDB?>(null);

        public Task<GeoLocationCacheDB> SetCoordinatesAsync(string address, double latitude, double longitude, CancellationToken ct = default) =>
            Task.FromResult(new GeoLocationCacheDB { NormalizedAddress = address, Latitude = latitude, Longitude = longitude });

        public Task UpsertRangeAsync(IEnumerable<GeoLocationCacheDB> items, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}

