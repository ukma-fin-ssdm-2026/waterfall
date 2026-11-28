// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
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

    private static async Task<Guid> SeedUserAsync(WaterfallDbContext db)
    {
        Guid userId = Guid.NewGuid();
        db.Users.Add(new UserDB
        {
            Id = userId,
            Email = $"user-{userId:N}@waterfall.test",
            PasswordHash = "hash",
            Role = "JobSeeker"
        });
        await db.SaveChangesAsync();
        return userId;
    }

    [Fact]
    public async Task SaveUserAddressAsync_CreatesNewRecord_WhenNoneExists()
    {
        Guid userId;
        using (WaterfallDbContext db = new(_options))
        {
            userId = await SeedUserAsync(db);
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
        Guid userId;
        using (WaterfallDbContext db = new(_options))
        {
            userId = await SeedUserAsync(db);
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
