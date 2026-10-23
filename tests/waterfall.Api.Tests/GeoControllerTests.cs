// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Waterfall.Api.Controllers;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Geo;

namespace Waterfall.Api.Tests;

public class FakeGeoLocationService : IGeoLocationService
{
    public Dictionary<string, (double Latitude, double Longitude)> Coordinates { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<(double Latitude, double Longitude)?> GetCoordinatesAsync(string address, CancellationToken ct = default)
    {
        if (Coordinates.TryGetValue(address, out (double Latitude, double Longitude) val))
        {
            return Task.FromResult<(double Latitude, double Longitude)?>(val);
        }

        return Task.FromResult<(double Latitude, double Longitude)?>(null);
    }
}

public class GeoControllerTests
{
    private readonly FakeGeoLocationService _geoService = new();

    private GeoController CreateController() => new(_geoService)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    [Fact]
    public async Task GetCoordinates_ReturnsBadRequest_WhenAddressIsEmpty()
    {
        GeoController controller = CreateController();

        ActionResult<CoordinatesDto> result = await controller.GetCoordinates("", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetCoordinates_ReturnsNotFound_WhenCoordinatesCannotBeResolved()
    {
        GeoController controller = CreateController();

        ActionResult<CoordinatesDto> result = await controller.GetCoordinates("Невідома адреса 123", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetCoordinates_ReturnsOkWithCoordinates_WhenResolved()
    {
        _geoService.Coordinates["Київ, Хрещатик, 22"] = (50.4472, 30.5226);

        GeoController controller = CreateController();

        ActionResult<CoordinatesDto> result = await controller.GetCoordinates("Київ, Хрещатик, 22", CancellationToken.None);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        CoordinatesDto dto = Assert.IsType<CoordinatesDto>(ok.Value);
        Assert.Equal(50.4472, dto.Latitude);
        Assert.Equal(30.5226, dto.Longitude);
    }
}
