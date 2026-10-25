// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Client.Services;

namespace Waterfall.Client.Tests;

public class GeoDistanceServiceTests
{
    [Fact]
    public async Task CalculateDistanceKmAsync_ReturnsNull_WhenUserAddressEmpty()
    {
        GeoDistanceService service = new() { UserAddress = "" };
        double? result = await service.CalculateDistanceKmAsync("Київ, вул. Велика Васильківська, 5");
        Assert.Null(result);
    }

    [Theory]
    [InlineData("Remote")]
    [InlineData("Віддалено (Україна)")]
    [InlineData("remote / hybrid")]
    public async Task CalculateDistanceKmAsync_ReturnsNull_ForRemoteVacancies(string remoteAddress)
    {
        GeoDistanceService service = new();
        double? result = await service.CalculateDistanceKmAsync(remoteAddress);
        Assert.Null(result);
    }

    [Fact]
    public async Task CalculateDistanceKmAsync_ReturnsDeterministicDistance_WhenFallbackUsed()
    {
        GeoDistanceService service = new() { UserAddress = "вул. Хрещатик, 22, Київ" };
        double? result1 = await service.CalculateDistanceKmAsync("вул. Мечникова, 2, Київ");
        double? result2 = await service.CalculateDistanceKmAsync("вул. Мечникова, 2, Київ");

        Assert.NotNull(result1);
        Assert.Equal(result1, result2);
        Assert.InRange(result1.Value, 0.5, 20.0);
    }

    [Fact]
    public async Task CalculateDistanceKmAsync_LeftBankToRightBank_CalculatesRealisticCrossRiverDistance()
    {
        GeoDistanceService service = new() { UserAddress = "Київ, Позняки" };
        double? result = await service.CalculateDistanceKmAsync("Київ, Спортивна площа, 1а");

        Assert.NotNull(result);
        // Маршрут з лівого берега через міст на правий берег
        Assert.InRange(result.Value, 8.0, 15.0);
    }

    [Fact]
    public async Task CalculateDistanceKmAsync_InterCity_CalculatesRealisticHighwayDistance()
    {
        GeoDistanceService service = new() { UserAddress = "Київ" };
        double? result = await service.CalculateDistanceKmAsync("Львів");

        Assert.NotNull(result);
        // Реальна автомобільна траса Київ - Львів (М-06) складає ~540 км
        Assert.InRange(result.Value, 450.0, 650.0);
    }

    [Fact]
    public async Task CalculateDistanceKmAsync_WithMessyAddressAndBusinessCenter_CleansAndCalculatesDistance()
    {
        GeoDistanceService service = new() { UserAddress = "Київ, вул. Мечникова, 2" };
        double? result = await service.CalculateDistanceKmAsync("Київ, Спортивна площа, 1а (ТРЦ Gulliver, 5 поверх)");

        Assert.NotNull(result);
        // Локації в одному місті (БЦ Парус та ТРЦ Гулівер або міський fallback)
        Assert.InRange(result.Value, 0.4, 7.0);
    }

    [Fact]
    public async Task CalculateDistanceKmAsync_ProspektSobornostiToSportyvnaPloshcha_CalculatesRealisticCrossRiverDistance()
    {
        GeoDistanceService service = new() { UserAddress = "Київ, проспект Соборності" };
        double? result = await service.CalculateDistanceKmAsync("Київ, Спортивна площа, 1а, БЦ Гулівер");

        Assert.NotNull(result);
        // Реальна відстань через міст Патона: ~9.2 - 9.5 км
        Assert.InRange(result.Value, 8.5, 10.5);
    }

    [Fact]
    public async Task CalculateDistanceKmAsync_ReturnsPointOne_ForIdenticalAddresses()
    {
        GeoDistanceService service = new() { UserAddress = "Київ, вул. Мечникова, 2" };
        double? result = await service.CalculateDistanceKmAsync("Київ, вул. Мечникова, 2");

        Assert.NotNull(result);
        Assert.Equal(0.1, result.Value);
    }

    [Fact]
    public async Task CalculateDistanceKmAsync_UsesServerCoordinates_WhenProvidedByGateway()
    {
        FakeGateway gateway = new();
        gateway.CoordinatesHandler = addr => addr.Contains("хрещатик", StringComparison.OrdinalIgnoreCase)
            ? new Waterfall.Client.Models.Geo.CoordinatesDto { Latitude = 50.4472, Longitude = 30.5226 }
            : new Waterfall.Client.Models.Geo.CoordinatesDto { Latitude = 50.4372, Longitude = 30.5276 };

        GeoDistanceService service = new(gateway);
        service.UserAddress = "Київ, вул. Хрещатик, 22";

        double? result = await service.CalculateDistanceKmAsync("Київ, вул. Мечникова, 2");

        Assert.NotNull(result);
        Assert.InRange(result.Value, 0.5, 5.0);
    }

    [Fact]
    public async Task CalculateDistanceKmAsync_UsesOsrmRoute_WhenHttpSucceeds()
    {
        string osrmJson = """
        {
            "code": "Ok",
            "routes": [
                {
                    "distance": 8500.0,
                    "duration": 650.0
                }
            ]
        }
        """;

        FakeHttpHandler handler = new(osrmJson);
        HttpClient httpClient = new(handler);
        GeoDistanceService service = new(null, httpClient) { UserAddress = "Київ, проспект Соборності" };

        double? result = await service.CalculateDistanceKmAsync("Київ, Спортивна площа, 1а");

        Assert.NotNull(result);
        Assert.Equal(8.5, result.Value);
    }

    [Fact]
    public async Task CalculateDistanceKmAsync_FallsBackToHaversine_WhenOsrmThrows()
    {
        FakeHttpHandler handler = new("", throwError: true);
        HttpClient httpClient = new(handler);
        GeoDistanceService service = new(null, httpClient) { UserAddress = "Київ, вул. Мечникова, 2" };

        double? result = await service.CalculateDistanceKmAsync("Київ, Спортивна площа, 1а");

        Assert.NotNull(result);
        Assert.InRange(result.Value, 0.5, 3.0);
    }

    [Fact]
    public async Task CalculateDistanceKmAsync_ResolvesCityCentroids_ForDifferentCities()
    {
        GeoDistanceService service = new() { UserAddress = "Одеса" };
        double? result = await service.CalculateDistanceKmAsync("Харків");

        Assert.NotNull(result);
        // Відстань Одеса - Харків > 500 км
        Assert.InRange(result.Value, 500.0, 900.0);
    }

    [Fact]
    public async Task CalculateDistanceKmAsync_ReturnsNull_WhenAddressUnknownAndNotNetworkError()
    {
        string emptyWikiJson = """
        {
            "query": {
                "pages": {}
            }
        }
        """;
        FakeHttpHandler handler = new(emptyWikiJson);
        HttpClient httpClient = new(handler);
        GeoDistanceService service = new(null, httpClient, TimeSpan.FromMilliseconds(10)) { UserAddress = "Київ" };

        double? result = await service.CalculateDistanceKmAsync("Фантастична вул., 9999");

        Assert.Null(result);
    }

    [Fact]
    public async Task CalculateDistanceKmAsync_ThrowsHttpRequestException_WhenNetworkFailsDuringResolution()
    {
        FakeHttpHandler handler = new("", throwError: true);
        HttpClient httpClient = new(handler);
        GeoDistanceService service = new(null, httpClient, TimeSpan.FromMilliseconds(10)) { UserAddress = "Київ" };

        await Assert.ThrowsAsync<HttpRequestException>(() => service.CalculateDistanceKmAsync("Невідома вул., 8888"));
    }

    [Fact]
    public async Task CalculateDistanceKmAsync_ResolvesViaPhotonAndRoutesViaOsrm()
    {
        string photonJson = """
        {
            "type": "FeatureCollection",
            "features": [
                {
                    "type": "Feature",
                    "geometry": {
                        "coordinates": [30.5100, 50.4500],
                        "type": "Point"
                    },
                    "properties": {
                        "name": "Нова локація",
                        "city": "Київ"
                    }
                }
            ]
        }
        """;

        string osrmJson = """
        {
            "code": "Ok",
            "routes": [
                {
                    "distance": 4200.0,
                    "duration": 300.0
                }
            ]
        }
        """;

        RoutingFakeHttpHandler handler = new(photonJson, osrmJson);
        HttpClient httpClient = new(handler);
        GeoDistanceService service = new(null, httpClient, TimeSpan.FromMilliseconds(10)) { UserAddress = "Київ, вул. Мечникова, 2" };

        double? result = await service.CalculateDistanceKmAsync("Нова тестова локація 777");

        Assert.NotNull(result);
        Assert.Equal(4.2, result.Value);
    }

    private sealed class RoutingFakeHttpHandler : HttpMessageHandler
    {
        private readonly string _photonResponse;
        private readonly string _osrmResponse;

        public RoutingFakeHttpHandler(string photonResponse, string osrmResponse)
        {
            _photonResponse = photonResponse;
            _osrmResponse = osrmResponse;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.RequestUri != null && request.RequestUri.Host.Contains("router.project-osrm.org")
                ? _osrmResponse
                : _photonResponse;

            HttpResponseMessage msg = new(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            };
            return Task.FromResult(msg);
        }
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly string _response;
        private readonly bool _throwError;

        public FakeHttpHandler(string response, bool throwError = false)
        {
            _response = response;
            _throwError = throwError;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_throwError)
            {
                throw new HttpRequestException("Network failure");
            }

            HttpResponseMessage msg = new(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_response)
            };
            return Task.FromResult(msg);
        }
    }
}

