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
        Assert.Equal(4, await ReadUserVersionAsync(dbPath));
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
        Assert.Equal(4, await ReadUserVersionAsync(dbPath));
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
        Assert.Equal(4, await ReadUserVersionAsync(dbPath));
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
        Assert.Equal(4, await ReadUserVersionAsync(firstDbPath));
        Assert.Equal(4, await ReadUserVersionAsync(secondDbPath));
        (double Latitude, double Longitude)? coords = await secondGeo.GetCoordinatesAsync("Київ, Хрещатик, 1");
        Assert.NotNull(coords);
        Assert.Equal(50.45, coords.Value.Latitude);
    }

    [Fact]
    public async Task VersionOneDatabase_UpgradesToVersionTwoAndDropsUnusedUserTables()
    {
        // Arrange
        string dbPath = CreateTempDbPath("v1-to-v2");
        await using (SqliteConnection connection = new($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            await using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE "Vacancies" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "Title" TEXT NOT NULL,
                    "Company" TEXT NOT NULL,
                    "Description" TEXT NOT NULL,
                    "Category" TEXT NOT NULL,
                    "CategoryGroup" TEXT NULL,
                    "Location" TEXT NOT NULL,
                    "FullAddress" TEXT NULL,
                    "Schedule" TEXT NULL,
                    "Experience" TEXT NULL,
                    "ExperienceMonths" INTEGER NULL,
                    "WorkFormat" INTEGER NOT NULL DEFAULT 0,
                    "SalaryMin" TEXT NULL,
                    "SalaryMax" TEXT NULL,
                    "Currency" TEXT NOT NULL,
                    "Source" TEXT NOT NULL,
                    "SourceUrl" TEXT NOT NULL,
                    "SkillsJson" TEXT NOT NULL,
                    "SearchText" TEXT NOT NULL DEFAULT '',
                    "PublishedAt" INTEGER NOT NULL,
                    "FirstSeenAt" INTEGER NOT NULL,
                    "LastSeenAt" INTEGER NOT NULL,
                    "ExternalId" TEXT NULL,
                    "IsActive" INTEGER NOT NULL,
                    "IsSynced" INTEGER NOT NULL DEFAULT 1,
                    "LocalUpdatedAt" INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE "SyncState" (
                    "Key" TEXT NOT NULL PRIMARY KEY,
                    "LastSyncedAt" INTEGER NULL,
                    "LastSummary" TEXT NULL
                );
                CREATE TABLE "GeoLocationCache" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "NormalizedAddress" TEXT NOT NULL,
                    "Latitude" REAL NOT NULL,
                    "Longitude" REAL NOT NULL,
                    "CreatedAt" INTEGER NOT NULL,
                    "IsSynced" INTEGER NOT NULL DEFAULT 1
                );
                CREATE TABLE "SavedVacancies" ("Id" TEXT NOT NULL PRIMARY KEY, "VacancyId" TEXT NOT NULL, "UserId" TEXT NULL, "Notes" TEXT NULL, "SavedAt" INTEGER NOT NULL);
                CREATE TABLE "Users" ("Id" TEXT NOT NULL PRIMARY KEY, "Email" TEXT NOT NULL, "PasswordHash" TEXT NOT NULL, "Role" TEXT NOT NULL, "CreatedAt" INTEGER NOT NULL);
                CREATE TABLE "UserPersonalData" ("Id" TEXT NOT NULL PRIMARY KEY, "UserId" TEXT NOT NULL, "Address" TEXT NULL, "UpdatedAt" INTEGER NOT NULL);
                INSERT INTO "Vacancies" ("Id","Title","Company","Description","Category","Location","Currency","Source","SourceUrl","SkillsJson","SearchText","PublishedAt","FirstSeenAt","LastSeenAt","IsActive")
                VALUES ('22222222-2222-2222-2222-222222222222','Preserved Job','KeepCo','Desc','.NET','Київ','USD','Djinni','https://djinni.co/jobs/keep-1/','[]','preserved job',0,0,0,1);
                PRAGMA user_version = 1;
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        EfLocalVacancyStore store = new(dbPath);

        // Act
        IReadOnlyList<VacancyDto> vacancies = await store.GetAllAsync();

        // Assert
        Assert.Equal(4, await ReadUserVersionAsync(dbPath));
        Assert.Single(vacancies);
        Assert.Equal("Preserved Job", vacancies[0].Title);

        await using SqliteConnection verifyConn = new($"Data Source={dbPath}");
        await verifyConn.OpenAsync();
        await using SqliteCommand tableCmd = verifyConn.CreateCommand();
        tableCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('SavedVacancies', 'Users', 'UserPersonalData');";
        long unusedTables = Convert.ToInt64(await tableCmd.ExecuteScalarAsync());
        Assert.Equal(0, unusedTables);
    }

    [Fact]
    public async Task VersionTwoDatabase_UpgradesToVersionThreeDeduplicatesAndCreatesSyncIndexes()
    {
        // Arrange
        string dbPath = CreateTempDbPath("v2-to-v3");
        Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter converter = new();
        long older = (long)converter.ConvertToProvider(new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero))!;
        long newer = (long)converter.ConvertToProvider(new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero))!;

        await using (SqliteConnection connection = new($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            await using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                CREATE TABLE "Vacancies" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "Title" TEXT NOT NULL,
                    "Company" TEXT NOT NULL,
                    "Description" TEXT NOT NULL,
                    "Category" TEXT NOT NULL,
                    "CategoryGroup" TEXT NULL,
                    "Location" TEXT NOT NULL,
                    "FullAddress" TEXT NULL,
                    "Schedule" TEXT NULL,
                    "Experience" TEXT NULL,
                    "ExperienceMonths" INTEGER NULL,
                    "WorkFormat" INTEGER NOT NULL DEFAULT 0,
                    "SalaryMin" TEXT NULL,
                    "SalaryMax" TEXT NULL,
                    "Currency" TEXT NOT NULL,
                    "Source" TEXT NOT NULL,
                    "SourceUrl" TEXT NOT NULL,
                    "SkillsJson" TEXT NOT NULL,
                    "SearchText" TEXT NOT NULL DEFAULT '',
                    "PublishedAt" INTEGER NOT NULL,
                    "FirstSeenAt" INTEGER NOT NULL,
                    "LastSeenAt" INTEGER NOT NULL,
                    "ExternalId" TEXT NULL,
                    "IsActive" INTEGER NOT NULL,
                    "IsSynced" INTEGER NOT NULL DEFAULT 1,
                    "LocalUpdatedAt" INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE "SyncState" ("Key" TEXT NOT NULL PRIMARY KEY, "LastSyncedAt" INTEGER NULL, "LastSummary" TEXT NULL);
                CREATE TABLE "GeoLocationCache" ("Id" TEXT NOT NULL PRIMARY KEY, "NormalizedAddress" TEXT NOT NULL, "Latitude" REAL NOT NULL, "Longitude" REAL NOT NULL, "CreatedAt" INTEGER NOT NULL, "IsSynced" INTEGER NOT NULL DEFAULT 1);
                INSERT INTO "Vacancies" ("Id","Title","Company","Description","Category","Location","Currency","Source","SourceUrl","SkillsJson","SearchText","PublishedAt","FirstSeenAt","LastSeenAt","ExternalId","IsActive")
                VALUES
                    ('44440001-0000-0000-0000-000000000001','Old Local','Co','Desc','.NET','Київ','USD','Djinni','https://djinni.co/old','[]','old',{older},{older},{older},'ext-99',1),
                    ('44440002-0000-0000-0000-000000000002','New Local','Co','Desc','.NET','Київ','USD','Djinni','https://djinni.co/new','[]','new',{older},{older},{newer},'ext-99',1);
                PRAGMA user_version = 2;
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        EfLocalVacancyStore store = new(dbPath);

        // Act
        IReadOnlyList<VacancyDto> vacancies = await store.GetAllAsync();

        // Assert
        Assert.Equal(4, await ReadUserVersionAsync(dbPath));
        VacancyDto survivor = Assert.Single(vacancies);
        Assert.Equal("New Local", survivor.Title);

        await using SqliteConnection verifyConn = new($"Data Source={dbPath}");
        await verifyConn.OpenAsync();
        await using SqliteCommand idxCmd = verifyConn.CreateCommand();
        idxCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name IN ('IX_Vacancies_Source_ExternalId', 'IX_Vacancies_SourceUrl');";
        Assert.Equal(2, Convert.ToInt64(await idxCmd.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task VersionThreeDatabase_UpgradesToVersionFourAndClearsFullAddressSentinels()
    {
        // Arrange
        string dbPath = CreateTempDbPath("v3-to-v4");
        Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter converter = new();
        long ts = (long)converter.ConvertToProvider(new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero))!;

        await using (SqliteConnection connection = new($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            await using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                CREATE TABLE "Vacancies" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "Title" TEXT NOT NULL,
                    "Company" TEXT NOT NULL,
                    "Description" TEXT NOT NULL,
                    "Category" TEXT NOT NULL,
                    "CategoryGroup" TEXT NULL,
                    "Location" TEXT NOT NULL,
                    "FullAddress" TEXT NULL,
                    "Schedule" TEXT NULL,
                    "Experience" TEXT NULL,
                    "ExperienceMonths" INTEGER NULL,
                    "WorkFormat" INTEGER NOT NULL DEFAULT 0,
                    "SalaryMin" TEXT NULL,
                    "SalaryMax" TEXT NULL,
                    "Currency" TEXT NOT NULL,
                    "Source" TEXT NOT NULL,
                    "SourceUrl" TEXT NOT NULL,
                    "SkillsJson" TEXT NOT NULL,
                    "SearchText" TEXT NOT NULL DEFAULT '',
                    "PublishedAt" INTEGER NOT NULL,
                    "FirstSeenAt" INTEGER NOT NULL,
                    "LastSeenAt" INTEGER NOT NULL,
                    "ExternalId" TEXT NULL,
                    "IsActive" INTEGER NOT NULL,
                    "IsSynced" INTEGER NOT NULL DEFAULT 1,
                    "LocalUpdatedAt" INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE "SyncState" ("Key" TEXT NOT NULL PRIMARY KEY, "LastSyncedAt" INTEGER NULL, "LastSummary" TEXT NULL);
                CREATE TABLE "GeoLocationCache" ("Id" TEXT NOT NULL PRIMARY KEY, "NormalizedAddress" TEXT NOT NULL, "Latitude" REAL NOT NULL, "Longitude" REAL NOT NULL, "CreatedAt" INTEGER NOT NULL, "IsSynced" INTEGER NOT NULL DEFAULT 1);
                INSERT INTO "Vacancies" ("Id","Title","Company","Description","Category","Location","FullAddress","Currency","Source","SourceUrl","SkillsJson","SearchText","PublishedAt","FirstSeenAt","LastSeenAt","ExternalId","IsActive")
                VALUES
                    ('66660001-0000-0000-0000-000000000001','Sentinel Job','Co','Desc','.NET','Київ','Київ, не вказано точну адресу','USD','Djinni','https://djinni.co/c1','[]','s',{ts},{ts},{ts},'c1',1),
                    ('66660002-0000-0000-0000-000000000002','HQ Job','Co','Desc','.NET','Київ','Київ (Головний офіс)','USD','Djinni','https://djinni.co/c2','[]','h',{ts},{ts},{ts},'c2',1),
                    ('66660003-0000-0000-0000-000000000003','Street Job','Co','Desc','.NET','Київ','Київ, вул. Садова, 2а','USD','Djinni','https://djinni.co/c3','[]','r',{ts},{ts},{ts},'c3',1);
                PRAGMA user_version = 3;
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        EfLocalVacancyStore store = new(dbPath);

        // Act
        IReadOnlyList<VacancyDto> vacancies = await store.GetAllAsync();

        // Assert
        Assert.Equal(4, await ReadUserVersionAsync(dbPath));
        Assert.Null(vacancies.Single(v => v.ExternalId == "c1").FullAddress);
        Assert.Null(vacancies.Single(v => v.ExternalId == "c2").FullAddress);
        Assert.Equal("Київ, вул. Садова, 2а", vacancies.Single(v => v.ExternalId == "c3").FullAddress);
    }
}
