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
}
