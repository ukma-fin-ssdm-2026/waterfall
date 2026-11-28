using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Waterfall.Client.Data;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.Services;

namespace Waterfall.Client.Tests;

public sealed class ClientDatabaseBootstrapperTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string CreateTempDbPath(string prefix)
    {
        string path = Path.Combine(Path.GetTempPath(), $"waterfall-{prefix}-{Guid.NewGuid():N}.db");
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (string path in _tempFiles)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Ігноруємо помилку прибирання тимчасового файлу в тесті
            }
        }
    }

    private static async Task<int> ReadUserVersionAsync(string dbPath)
    {
        await using SqliteConnection connection = new($"Data Source={dbPath}");
        await connection.OpenAsync();
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        object? scalar = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(scalar);
    }

    [Fact]
    public async Task FreshDatabase_InitializesSchemaAndSetsUserVersionToCurrent()
    {
        // Arrange
        string dbPath = CreateTempDbPath("fresh");
        EfLocalVacancyStore store = new(dbPath);

        // Act
        LocalVacancyStats stats = await store.GetStatsAsync();

        // Assert
        Assert.Equal(1, await ReadUserVersionAsync(dbPath));
        Assert.Equal(0, stats.TotalCount);
        Assert.Equal(0, stats.UnsyncedCount);
        Assert.Null(stats.LastSyncedAt);
    }

    [Fact]
    public async Task LegacyUnversionedDatabase_UpgradesSchemaSetsUserVersionToOneAndSupportsSyncState()
    {
        // Arrange: створюємо стару БД із user_version = 0, без IsSynced/LocalUpdatedAt/CategoryGroup і без таблиці SyncState
        string dbPath = CreateTempDbPath("legacy");
        Guid existingId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        await using (SqliteConnection connection = new($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            await using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = """
                PRAGMA user_version = 0;
                CREATE TABLE "Vacancies" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "Title" TEXT NOT NULL,
                    "Company" TEXT NOT NULL,
                    "Description" TEXT NOT NULL,
                    "Category" TEXT NOT NULL,
                    "Location" TEXT NOT NULL,
                    "SalaryMin" TEXT NULL,
                    "SalaryMax" TEXT NULL,
                    "Currency" TEXT NOT NULL,
                    "Source" TEXT NOT NULL,
                    "SourceUrl" TEXT NOT NULL,
                    "SkillsJson" TEXT NOT NULL,
                    "PublishedAt" INTEGER NOT NULL,
                    "FirstSeenAt" INTEGER NOT NULL,
                    "LastSeenAt" INTEGER NOT NULL,
                    "IsActive" INTEGER NOT NULL
                );
                INSERT INTO "Vacancies"
                    ("Id","Title","Company","Description","Category","Location","Currency","Source","SourceUrl","SkillsJson","PublishedAt","FirstSeenAt","LastSeenAt","IsActive")
                VALUES
                    ('11111111-1111-1111-1111-111111111111','Legacy .NET Dev','OldCo','Backend','Backend','Київ','USD','Djinni','https://djinni.co/jobs/legacy-client/','["C#"]',0,0,0,1);
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        EfLocalVacancyStore store = new(dbPath);
        DateTimeOffset syncedAt = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

        // Act
        await store.SaveVacanciesAsync(
        [
            new VacancyDto
            {
                Id = Guid.NewGuid(),
                Title = "New Offline Job",
                Company = "FreshCo",
                Description = "MAUI",
                Category = "Mobile",
                Location = "Львів",
                Source = JobSource.Dou,
                SourceUrl = "https://jobs.dou.ua/vacancies/new-1/"
            }
        ], isSynced: false);
        await store.SetLastSyncedAtAsync(syncedAt, "OK");

        // Assert
        Assert.Equal(1, await ReadUserVersionAsync(dbPath));
        IReadOnlyList<VacancyDto> all = await store.GetAllAsync();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, v => v.Id == existingId && v.Title == "Legacy .NET Dev");
        Assert.Contains(all, v => v.Title == "New Offline Job");
        Assert.Equal(syncedAt, await store.GetLastSyncedAtAsync());

        LocalVacancyStats stats = await store.GetStatsAsync();
        Assert.Equal(2, stats.TotalCount);
        Assert.Equal(1, stats.UnsyncedCount);
        Assert.Equal(syncedAt, stats.LastSyncedAt);
    }

    [Fact]
    public async Task DatabaseWithNewerUserVersion_RebuildsLocalCacheAndResetsUserVersionToCurrent()
    {
        // Arrange
        string dbPath = CreateTempDbPath("future");
        await using (SqliteConnection connection = new($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            await using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE "StaleFutureTable" ("Id" INTEGER PRIMARY KEY);
                PRAGMA user_version = 99;
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        DbContextOptions<ClientWaterfallDbContext> options = new DbContextOptionsBuilder<ClientWaterfallDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        // Act
        await using (ClientWaterfallDbContext context = new(options))
        {
            await ClientDatabaseBootstrapper.EnsureInitializedAsync(context);
        }

        // Assert
        Assert.Equal(1, await ReadUserVersionAsync(dbPath));
        EfLocalVacancyStore store = new(dbPath);
        await store.SaveVacanciesAsync(
        [
            new VacancyDto
            {
                Id = Guid.NewGuid(),
                Title = "Rebuilt Cache Job",
                Company = "TestCo",
                Category = ".NET",
                Location = "Київ",
                Source = JobSource.Dou,
                SourceUrl = "https://jobs.dou.ua/vacancies/rebuilt/"
            }
        ], isSynced: true);
        Assert.Single(await store.GetAllAsync());
    }

    [Fact]
    public async Task MultipleDistinctDatabasePaths_EachDatabaseIsInitializedIndependently()
    {
        // Arrange
        string firstDbPath = CreateTempDbPath("first");
        string secondDbPath = CreateTempDbPath("second");
        EfLocalVacancyStore firstStore = new(firstDbPath);
        EfLocalGeoCacheService secondGeo = new(secondDbPath);

        // Act
        await firstStore.GetStatsAsync();
        await secondGeo.SaveCoordinatesAsync("Київ, Хрещатик, 1", 50.45, 30.52, isSynced: false);

        // Assert
        Assert.Equal(1, await ReadUserVersionAsync(firstDbPath));
        Assert.Equal(1, await ReadUserVersionAsync(secondDbPath));
        (double Latitude, double Longitude)? coords = await secondGeo.GetCoordinatesAsync("Київ, Хрещатик, 1");
        Assert.NotNull(coords);
        Assert.Equal(50.45, coords.Value.Latitude);
    }
}
