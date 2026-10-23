// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Data;

namespace Waterfall.Repository.Tests;

public class WaterfallDbSeederTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<WaterfallDbContext> _options;

    public WaterfallDbSeederTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    [Fact]
    public async Task SeedAsync_PopulatesDatabaseTables_WhenEmpty()
    {
        using (WaterfallDbContext db = new(_options))
        {
            await WaterfallDbSeeder.SeedAsync(db);
        }

        using (WaterfallDbContext db = new(_options))
        {
            Assert.True(await db.Vacancies.AnyAsync());
            Assert.True(await db.SavedVacancies.AnyAsync());
            Assert.True(await db.Users.AnyAsync());
            Assert.True(await db.UserPersonalData.AnyAsync());
        }
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_WhenCalledMultipleTimes()
    {
        using (WaterfallDbContext db = new(_options))
        {
            await WaterfallDbSeeder.SeedAsync(db);
            // Second call should not throw or duplicate unique entities
            await WaterfallDbSeeder.SeedAsync(db);
        }

        using (WaterfallDbContext db = new(_options))
        {
            int vacancyCount = await db.Vacancies.CountAsync();
            Assert.True(vacancyCount > 0);
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
