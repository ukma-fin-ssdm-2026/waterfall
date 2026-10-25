// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Client.Entities;
using Waterfall.Client.Models.Geo;
using Waterfall.Client.Services;
using Xunit;

namespace Waterfall.Client.Tests;

public class EfLocalGeoCacheServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly EfLocalGeoCacheService _service;

    public EfLocalGeoCacheServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_ef_geo_{Guid.NewGuid():N}.db");
        _service = new EfLocalGeoCacheService(_dbPath);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public async Task SaveCoordinatesAsync_And_GetCoordinatesAsync_WorksCorrectly()
    {
        // Arrange
        string address = "Київ, вул. Хрещатик, 22";

        // Act
        await _service.SaveCoordinatesAsync(address, 50.4472, 30.5226, isSynced: false);
        var coords = await _service.GetCoordinatesAsync("Хрещатик, 22");

        // Assert
        Assert.NotNull(coords);
        Assert.Equal(50.4472, coords.Value.Latitude);
        Assert.Equal(30.5226, coords.Value.Longitude);
    }

    [Fact]
    public async Task GetUnsyncedAsync_ReturnsOnlyUnsyncedItems()
    {
        // Arrange
        await _service.SaveCoordinatesAsync("Київ, вул. Хрещатик", 10.0, 20.0, isSynced: false);
        await _service.SaveCoordinatesAsync("Київ, вул. Басейна", 30.0, 40.0, isSynced: true);

        // Act
        IReadOnlyList<ClientGeoLocationCacheDB> unsynced = await _service.GetUnsyncedAsync();

        // Assert
        Assert.Single(unsynced);
        Assert.False(unsynced[0].IsSynced);
        Assert.Equal(10.0, unsynced[0].Latitude);
    }

    [Fact]
    public async Task MarkAsSyncedAsync_ChangesIsSyncedToTrue()
    {
        // Arrange
        await _service.SaveCoordinatesAsync("Офлайн адреса", 50.0, 30.0, isSynced: false);
        IReadOnlyList<ClientGeoLocationCacheDB> unsynced = await _service.GetUnsyncedAsync();
        Assert.Single(unsynced);

        // Act
        await _service.MarkAsSyncedAsync(new[] { unsynced[0].Id });

        // Assert
        IReadOnlyList<ClientGeoLocationCacheDB> remaining = await _service.GetUnsyncedAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task UpsertFromServerAsync_SavesItemsAsSynced()
    {
        // Arrange
        List<GeoSyncItemDto> serverItems = new()
        {
            new GeoSyncItemDto
            {
                Id = Guid.NewGuid(),
                Address = "Львів, пл. Ринок, 1",
                Latitude = 49.8419,
                Longitude = 24.0315,
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        // Act
        await _service.UpsertFromServerAsync(serverItems);

        // Assert
        var coords = await _service.GetCoordinatesAsync("Львів, пл. Ринок, 1");
        Assert.NotNull(coords);
        Assert.Equal(49.8419, coords.Value.Latitude);

        var unsynced = await _service.GetUnsyncedAsync();
        Assert.Empty(unsynced);
    }

    [Fact]
    public async Task GeoDistanceService_SyncWithServerAsync_UploadsAndUpdates()
    {
        // Arrange
        await _service.SaveCoordinatesAsync("Київ, вул. Мечникова, 2", 50.4375, 30.5283, isSynced: false);

        FakeGateway gateway = new()
        {
            CoordinatesHandler = _ => null
        };

        // Act
        GeoDistanceService geoService = new(gateway, _service);
        int syncedCount = await geoService.SyncWithServerAsync();

        // Assert: 1 unsynced item was processed
        Assert.Equal(1, syncedCount);

        IReadOnlyList<ClientGeoLocationCacheDB> remainingUnsynced = await _service.GetUnsyncedAsync();
        Assert.Empty(remainingUnsynced);
    }

    [Fact]
    public async Task EdgeCases_EmptyInputs_HandledGracefully()
    {
        // Null or whitespace inputs
        await _service.SaveCoordinatesAsync("", 0, 0, false);
        await _service.SaveCoordinatesAsync("   ", 0, 0, false);
        var nullCoords = await _service.GetCoordinatesAsync("");
        Assert.Null(nullCoords);

        // Empty collections
        await _service.MarkAsSyncedAsync(Array.Empty<Guid>());
        await _service.UpsertFromServerAsync(Array.Empty<GeoSyncItemDto>());

        // Updating existing entry
        await _service.SaveCoordinatesAsync("Одеса, вул. Дерибасівська", 46.4846, 30.7326, false);
        await _service.SaveCoordinatesAsync("Одеса, вул. Дерибасівська", 46.4850, 30.7330, true);

        var updated = await _service.GetCoordinatesAsync("Одеса, вул. Дерибасівська");
        Assert.NotNull(updated);
        Assert.Equal(46.4850, updated.Value.Latitude);

        // Substring / LIKE match
        var partial = await _service.GetCoordinatesAsync("Дерибасівська");
        Assert.NotNull(partial);
        Assert.Equal(46.4850, partial.Value.Latitude);
    }

    [Fact]
    public async Task GeoDistanceService_SyncWithServerAsync_HandlesServerItems()
    {
        FakeGateway gateway = new()
        {
            SyncHandler = req => new GeoSyncResponseDto
            {
                ServerItems = new List<GeoSyncItemDto>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        Address = "Харків, вул. Сумська",
                        Latitude = 50.0050,
                        Longitude = 36.2300,
                        CreatedAt = DateTimeOffset.UtcNow
                    }
                }
            }
        };

        GeoDistanceService geoService = new(gateway, _service);
        int synced = await geoService.SyncWithServerAsync();

        Assert.Equal(1, synced); // 1 server item processed

        var coords = await _service.GetCoordinatesAsync("Харків, вул. Сумська");
        Assert.NotNull(coords);
        Assert.Equal(50.0050, coords.Value.Latitude);
    }
}
