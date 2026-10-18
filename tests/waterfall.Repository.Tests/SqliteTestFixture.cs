// AI: фікстура ізольованої in-memory бази даних SQLite для тестів репозиторіїв (Lab 06); перевірено: запуск dotnet test, самоперевірка автора (Денис Сидоренко, §8)

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Data;

namespace Waterfall.Repository.Tests;

/// <summary>
/// Фікстура для тестування репозиторіїв на базі реляційної in-memory SQLite.
/// Створює ізольовану SQLite базу в пам'яті для кожного тестового прогону
/// та автоматично застосовує схему через Database.EnsureCreated().
/// </summary>
public sealed class SqliteTestFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<WaterfallDbContext> _options;

    public SqliteTestFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlite(_connection)
            .Options;

        using WaterfallDbContext db = CreateContext();
        db.Database.EnsureCreated();
    }

    public WaterfallDbContext CreateContext() => new(_options);

    public void Dispose()
    {
        _connection.Dispose();
    }
}
