// AI: Граничні тести (Boundary Tests) для перевірки стійкості до крайніх значень (Lab 9, Домашнє завдання 2)
// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services;
using Waterfall.Services.Models.Geo;

namespace Waterfall.Reliability.Tests;

/// <summary>
/// Граничні тести: перевіряють обробку екстремальних і граничних значень вхідних даних
/// (фізичні межі координат, NaN, нескінченність, ліміти розміру пакетів, граничні рядки).
/// Захищають систему від Cache Poisoning та вичерпання ресурсів пам'яті (FIND-05).
/// </summary>
public class BoundaryTests
{
    [Theory]
    [InlineData(90.0, 180.0, "Північний полюс / лінія зміни дат")]
    [InlineData(-90.0, -180.0, "Південний полюс / лінія зміни дат")]
    [InlineData(0.0, 0.0, "Нульовий меридіан та екватор")]
    [InlineData(89.999999, 179.999999, "Максимальне валідне значення перед межею")]
    [InlineData(-89.999999, -179.999999, "Мінімальне валідне значення перед межею")]
    public async Task GeoLocationService_SyncCacheAsync_AcceptsExactPhysicalBoundaries(double lat, double lon, string address)
    {
        // Arrange
        FakeGeoCacheRepository cache = new();
        GeoLocationService service = new(cache);
        GeoSyncRequestDto request = new()
        {
            ClientItems = new List<GeoSyncItemDto>
            {
                new() { Address = address, Latitude = lat, Longitude = lon }
            }
        };

        // Act
        GeoSyncResponseDto response = await service.SyncCacheAsync(request);

        // Assert - точні граничні значення мають успішно прийматися
        Assert.Single(response.ServerItems);
        Assert.Equal(lat, response.ServerItems[0].Latitude);
        Assert.Equal(lon, response.ServerItems[0].Longitude);
    }

    [Theory]
    [InlineData(90.000001, 30.5)]
    [InlineData(-90.000001, 30.5)]
    [InlineData(50.4, 180.000001)]
    [InlineData(50.4, -180.000001)]
    [InlineData(1000.0, 50.0)]
    [InlineData(50.0, 500.0)]
    public async Task GeoLocationService_SyncCacheAsync_RejectsCoordinatesJustBeyondBoundaries(double lat, double lon)
    {
        // Arrange
        FakeGeoCacheRepository cache = new();
        GeoLocationService service = new(cache);
        GeoSyncRequestDto request = new()
        {
            ClientItems = new List<GeoSyncItemDto>
            {
                new() { Address = "За межами діапазону", Latitude = lat, Longitude = lon }
            }
        };

        // Act
        GeoSyncResponseDto response = await service.SyncCacheAsync(request);

        // Assert - некоректні координати відфільтровано, кеш не отруєно (Cache Poisoning prevention)
        Assert.Empty(response.ServerItems);
    }

    [Fact]
    public async Task GeoLocationService_SyncCacheAsync_RejectsSpecialFloatingPointValues()
    {
        // Arrange - спеціальні значення типу IEEE 754: NaN та Infinity
        FakeGeoCacheRepository cache = new();
        GeoLocationService service = new(cache);
        GeoSyncRequestDto request = new()
        {
            ClientItems = new List<GeoSyncItemDto>
            {
                new() { Address = "NaN-1", Latitude = double.NaN, Longitude = 30.0 },
                new() { Address = "NaN-2", Latitude = 50.0, Longitude = double.NaN },
                new() { Address = "PositiveInf", Latitude = double.PositiveInfinity, Longitude = 30.0 },
                new() { Address = "NegativeInf", Latitude = 50.0, Longitude = double.NegativeInfinity },
                new() { Address = "Valid", Latitude = 50.45, Longitude = 30.52 }
            }
        };

        // Act
        GeoSyncResponseDto response = await service.SyncCacheAsync(request);

        // Assert - лише одне валідне значення збереглося, решта 4 небезпечних значень відкинуто
        Assert.Single(response.ServerItems);
        Assert.Equal("Valid", response.ServerItems[0].Address);
    }

    [Fact]
    public async Task GeoLocationService_SyncCacheAsync_EnforcesMaximumBatchLimit_ToPreventDatabaseDoS()
    {
        // Arrange - надсилання пакета понад встановлений ліміт у 500 елементів
        FakeGeoCacheRepository cache = new();
        GeoLocationService service = new(cache);

        List<GeoSyncItemDto> hugeList = Enumerable.Range(1, 550)
            .Select(i => new GeoSyncItemDto
            {
                Address = $"Адреса #{i}",
                Latitude = 50.0 + (i * 0.0001),
                Longitude = 30.0 + (i * 0.0001)
            })
            .ToList();

        GeoSyncRequestDto request = new() { ClientItems = hugeList };

        // Act
        GeoSyncResponseDto response = await service.SyncCacheAsync(request);

        // Assert - сервіс захистив СУБД та зберіг рівно 500 елементів
        Assert.Equal(500, response.ServerItems.Count);
    }

    [Fact]
    public async Task GeoLocationService_SyncCacheAsync_SkipsNullAndWhitespaceAddressEntries()
    {
        // Arrange
        FakeGeoCacheRepository cache = new();
        GeoLocationService service = new(cache);
        GeoSyncRequestDto request = new()
        {
            ClientItems = new List<GeoSyncItemDto>
            {
                new() { Address = null!, Latitude = 50.0, Longitude = 30.0 },
                new() { Address = "    ", Latitude = 50.0, Longitude = 30.0 },
                new() { Address = "", Latitude = 50.0, Longitude = 30.0 },
                new() { Address = "Валідна адреса", Latitude = 50.0, Longitude = 30.0 }
            }
        };

        // Act
        GeoSyncResponseDto response = await service.SyncCacheAsync(request);

        // Assert
        Assert.Single(response.ServerItems);
        Assert.Equal("Валідна адреса", response.ServerItems[0].Address);
    }

    private sealed class FakeGeoCacheRepository : IGeoLocationCacheRepository
    {
        private readonly List<GeoLocationCacheDB> _items = new();

        public Task<IReadOnlyList<GeoLocationCacheDB>> GetAllCachedAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeoLocationCacheDB>>(_items.ToList());

        public Task<GeoLocationCacheDB?> GetByAddressAsync(string address, CancellationToken ct = default) =>
            Task.FromResult(_items.FirstOrDefault(x => x.NormalizedAddress == address));

        public Task<GeoLocationCacheDB> SetCoordinatesAsync(string address, double latitude, double longitude, CancellationToken ct = default)
        {
            GeoLocationCacheDB item = new()
            {
                Id = Guid.NewGuid(),
                NormalizedAddress = address,
                Latitude = latitude,
                Longitude = longitude,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _items.Add(item);
            return Task.FromResult(item);
        }

        public Task UpsertRangeAsync(IEnumerable<GeoLocationCacheDB> items, CancellationToken ct = default)
        {
            _items.AddRange(items);
            return Task.CompletedTask;
        }
    }
}

