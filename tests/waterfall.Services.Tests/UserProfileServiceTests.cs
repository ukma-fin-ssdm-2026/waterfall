// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Data;
using Waterfall.Services;

namespace Waterfall.Services.Tests;

public class UserProfileServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<WaterfallDbContext> _options;

    public UserProfileServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlite(_connection)
            .Options;

        using WaterfallDbContext db = new(_options);
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task GetUserAddressAsync_ReturnsNull_WhenNoRecord()
    {
        using WaterfallDbContext db = new(_options);
        UserProfileService service = new(db);

        string? address = await service.GetUserAddressAsync(Guid.NewGuid());

        Assert.Null(address);
    }

    [Fact]
    public async Task SaveUserAddressAsync_CreatesNewRecord_WhenNoneExists()
    {
        Guid userId = Guid.NewGuid();
        using (WaterfallDbContext db = new(_options))
        {
            UserProfileService service = new(db);
            await service.SaveUserAddressAsync(userId, "  Київ, вул. Мечникова, 2  ");
        }

        using (WaterfallDbContext db = new(_options))
        {
            UserProfileService service = new(db);
            string? address = await service.GetUserAddressAsync(userId);
            Assert.Equal("Київ, вул. Мечникова, 2", address);
        }
    }

    [Fact]
    public async Task SaveUserAddressAsync_UpdatesExistingRecord()
    {
        Guid userId = Guid.NewGuid();
        using (WaterfallDbContext db = new(_options))
        {
            UserProfileService service = new(db);
            await service.SaveUserAddressAsync(userId, "Київ, вул. Мечникова, 2");
        }

        using (WaterfallDbContext db = new(_options))
        {
            UserProfileService service = new(db);
            await service.SaveUserAddressAsync(userId, "Київ, вул. Хрещатик, 22");
        }

        using (WaterfallDbContext db = new(_options))
        {
            UserProfileService service = new(db);
            string? address = await service.GetUserAddressAsync(userId);
            Assert.Equal("Київ, вул. Хрещатик, 22", address);
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
