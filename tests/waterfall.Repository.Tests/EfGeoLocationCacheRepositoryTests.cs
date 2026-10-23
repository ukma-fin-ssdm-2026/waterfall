// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Repositories;

namespace Waterfall.Repository.Tests;

public class EfGeoLocationCacheRepositoryTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _fixture;

    public EfGeoLocationCacheRepositoryTests(SqliteTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SetCoordinatesAsync_AddsNewEntry_WhenNotFound()
    {
        using WaterfallDbContext db = _fixture.CreateContext();
        EfGeoLocationCacheRepository repository = new(db);

        GeoLocationCacheDB saved = await repository.SetCoordinatesAsync("вул. Хрещатик, 22", 50.4472, 30.5226);

        Assert.NotNull(saved);
        Assert.Equal(50.4472, saved.Latitude);
        Assert.Equal(30.5226, saved.Longitude);

        GeoLocationCacheDB? retrieved = await repository.GetByAddressAsync("Хрещатик, 22");
        Assert.NotNull(retrieved);
        Assert.Equal(50.4472, retrieved.Latitude);
    }

    [Fact]
    public async Task GetByAddressAsync_ReturnsNull_WhenEmptyAddress()
    {
        using WaterfallDbContext db = _fixture.CreateContext();
        EfGeoLocationCacheRepository repository = new(db);

        GeoLocationCacheDB? result = await repository.GetByAddressAsync("");

        Assert.Null(result);
    }

    [Fact]
    public async Task SetCoordinatesAsync_UpdatesExistingEntry_WhenCalledAgain()
    {
        using WaterfallDbContext db = _fixture.CreateContext();
        EfGeoLocationCacheRepository repository = new(db);

        await repository.SetCoordinatesAsync("вул. Садова, 2", 49.8220, 23.9850);
        await repository.SetCoordinatesAsync("вул. Садова, 2", 49.8225, 23.9855);

        GeoLocationCacheDB? updated = await repository.GetByAddressAsync("вул. Садова, 2");
        Assert.NotNull(updated);
        Assert.Equal(49.8225, updated.Latitude);
        Assert.Equal(23.9855, updated.Longitude);
    }

    [Fact]
    public async Task GetByAddressAsync_MatchesWithoutHouseNumberAndWithoutCity()
    {
        using WaterfallDbContext db = _fixture.CreateContext();
        EfGeoLocationCacheRepository repository = new(db);

        // У базі збережено повну адресу з містом і номером: "Київ, вул. Велика Васильківська, 55"
        await repository.SetCoordinatesAsync("Київ, вул. Велика Васильківська, 55", 50.4320, 30.5165);

        // 1. Пошук без міста і без номера: "васильківська"
        GeoLocationCacheDB? matchWithoutCity = await repository.GetByAddressAsync("васильківська");
        Assert.NotNull(matchWithoutCity);
        Assert.Equal(50.4320, matchWithoutCity.Latitude);

        // 2. Пошук з іншим номером будинку: "Велика Васильківська, 10"
        GeoLocationCacheDB? matchDifferentNumber = await repository.GetByAddressAsync("Велика Васильківська, 10");
        Assert.NotNull(matchDifferentNumber);
        Assert.Equal(50.4320, matchDifferentNumber.Latitude);
    }

    [Fact]
    public async Task GetByAddressAsync_MatchesFlexibleStreetVariations()
    {
        using WaterfallDbContext db = _fixture.CreateContext();
        EfGeoLocationCacheRepository repository = new(db);

        // У базі збережено: "Київ, проспект Академіка Глушкова, 1"
        await repository.SetCoordinatesAsync("Київ, проспект Академіка Глушкова, 1", 50.3780, 30.4682);

        // 1. Пошук "київ, глушкова" (без слова "академіка" і без номера 1)
        GeoLocationCacheDB? matchWithoutAcademic = await repository.GetByAddressAsync("київ, глушкова");
        Assert.NotNull(matchWithoutAcademic);
        Assert.Equal(50.3780, matchWithoutAcademic.Latitude);

        // 2. Пошук лише "глушкова" (без міста)
        GeoLocationCacheDB? matchOnlyStreet = await repository.GetByAddressAsync("глушкова");
        Assert.NotNull(matchOnlyStreet);
        Assert.Equal(50.3780, matchOnlyStreet.Latitude);
    }
}
