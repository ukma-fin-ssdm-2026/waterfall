using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;

namespace Waterfall.Repository.Tests;

public sealed class WaterfallMigrationsTests
{
    private const string InitialMigrationId = "20261127000001_InitialSchema";
    private const string SavedIntegrityMigrationId = "20261127215638_RestoreSavedVacanciesIntegrity";
    private const string IndexVacanciesMigrationId = "20261127220606_IndexVacanciesForSyncUpsert";
    private const string ClearAddressSentinelMigrationId = "20261127221129_ClearFullAddressSentinel";
    private const string DropSearchTextMigrationId = "20261127221526_DropVacancySearchText";

    [Fact]
    public async Task MigrateAsync_OnEmptySqliteDatabase_CreatesSchemaAndRecordsInitialMigrationHistory()
    {
        // Arrange
        await using SqliteConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync();
        DbContextOptions<WaterfallDbContext> options = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlite(connection)
            .Options;

        await using WaterfallDbContext db = new(options);

        // Act
        await db.Database.MigrateAsync();

        // Assert
        List<string> applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        List<string> pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        Assert.Equal([InitialMigrationId, SavedIntegrityMigrationId, IndexVacanciesMigrationId, ClearAddressSentinelMigrationId, DropSearchTextMigrationId], applied);
        Assert.Empty(pending);
        Assert.Equal(0, await db.Vacancies.CountAsync());
        Assert.Equal(0, await db.SiteSearchCache.CountAsync());
        Assert.Equal(0, await db.ChatSessions.CountAsync());
        Assert.Equal(0, await db.ChatMessageVacancies.CountAsync());
    }

    [Fact]
    public void HasPendingModelChanges_OnSqliteProvider_ReturnsFalse()
    {
        // Arrange
        DbContextOptions<WaterfallDbContext> options = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using WaterfallDbContext db = new(options);

        // Act
        bool hasPending = db.Database.HasPendingModelChanges();

        // Assert
        Assert.False(hasPending);
    }

    [Fact]
    public void HasPendingModelChanges_OnSqlServerProvider_ReturnsFalse()
    {
        // Arrange
        DbContextOptions<WaterfallDbContext> options = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=WaterfallValidationTest;Trusted_Connection=True;")
            .Options;

        using WaterfallDbContext db = new(options);

        // Act
        bool hasPending = db.Database.HasPendingModelChanges();

        // Assert
        Assert.False(hasPending);
    }

    [Fact]
    public void GenerateScript_OnSqliteProvider_IncludesCoreTablesAndMigrationHistory()
    {
        // Arrange
        DbContextOptions<WaterfallDbContext> options = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using WaterfallDbContext db = new(options);
        IMigrator migrator = db.GetService<IMigrator>();

        // Act
        string script = migrator.GenerateScript();

        // Assert
        Assert.Contains("CREATE TABLE IF NOT EXISTS \"Vacancies\"", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS \"ChatMessageVacancy\"", script, StringComparison.Ordinal);
        Assert.Contains(InitialMigrationId, script, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateScript_OnSqlServerProvider_UsesDatetimeoffsetAndIdentityAndIdempotentColumns()
    {
        // Arrange
        DbContextOptions<WaterfallDbContext> options = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=WaterfallScriptTest;Trusted_Connection=True;")
            .Options;

        using WaterfallDbContext db = new(options);
        IMigrator migrator = db.GetService<IMigrator>();

        // Act
        string script = migrator.GenerateScript();

        // Assert
        Assert.Contains("datetimeoffset NOT NULL", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IDENTITY", script, StringComparison.Ordinal);
        Assert.Contains("COL_LENGTH(N'Vacancies', N'CategoryGroup')", script, StringComparison.Ordinal);
        Assert.Contains(InitialMigrationId, script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MigrateAsync_OnLegacyPreEfSqliteDatabase_AddsMissingColumnsAndTablesAndRecordsHistory()
    {
        // Arrange
        await using SqliteConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync();
        await using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE "Vacancies" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "Title" TEXT NOT NULL,
                    "Company" TEXT NOT NULL,
                    "Description" TEXT NOT NULL,
                    "Category" TEXT NOT NULL,
                    "Location" TEXT NOT NULL,
                    "SalaryMin" decimal(18,2) NULL,
                    "SalaryMax" decimal(18,2) NULL,
                    "Currency" TEXT NOT NULL,
                    "Source" TEXT NOT NULL,
                    "SourceUrl" TEXT NOT NULL,
                    "SkillsJson" TEXT NOT NULL,
                    "PublishedAt" INTEGER NOT NULL,
                    "FirstSeenAt" INTEGER NOT NULL,
                    "LastSeenAt" INTEGER NOT NULL,
                    "IsActive" INTEGER NOT NULL
                );
                CREATE TABLE "SavedVacancies" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "VacancyId" TEXT NOT NULL,
                    "UserId" TEXT NULL,
                    "Notes" TEXT NULL,
                    "SavedAt" INTEGER NOT NULL
                );
                CREATE TABLE "Users" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "Email" TEXT NOT NULL,
                    "PasswordHash" TEXT NOT NULL,
                    "Role" TEXT NOT NULL,
                    "CreatedAt" INTEGER NOT NULL
                );
                INSERT INTO "Vacancies"
                    ("Id","Title","Company","Description","Category","Location","Currency","Source","SourceUrl","SkillsJson","PublishedAt","FirstSeenAt","LastSeenAt","IsActive")
                VALUES
                    ('AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA','Legacy Backend','OldCo','ASP.NET Core','.NET','Київ','USD','Djinni','https://djinni.co/jobs/old-1/','["C#"]',0,0,0,1);
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        DbContextOptions<WaterfallDbContext> options = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlite(connection)
            .Options;

        await using WaterfallDbContext db = new(options);

        // Act
        await db.Database.MigrateAsync();

        // Assert
        VacancyDB? preserved = await db.Vacancies
            .AsNoTracking()
            .SingleOrDefaultAsync(v => v.Id == Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"));
        Assert.NotNull(preserved);
        Assert.Equal("Legacy Backend", preserved.Title);
        Assert.Equal(WorkFormat.None, preserved.WorkFormat);
        Assert.Equal(0, await db.SiteSearchCache.CountAsync());
        Assert.Equal(0, await db.ChatSessions.CountAsync());
        Assert.Equal([InitialMigrationId, SavedIntegrityMigrationId, IndexVacanciesMigrationId, ClearAddressSentinelMigrationId, DropSearchTextMigrationId], await db.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task MigrateAsync_FromInitialSchema_DeduplicatesSavedVacanciesAndEnforcesCompositeKey()
    {
        // Arrange: мігруємо до InitialSchema та вставляємо дубль, null UserId і "висячий" рядок
        await using SqliteConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync();
        DbContextOptions<WaterfallDbContext> options = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlite(connection)
            .Options;

        Guid userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid vacancyId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await using (WaterfallDbContext initialDb = new(options))
        {
            await initialDb.GetService<IMigrator>().MigrateAsync(InitialMigrationId);
            Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter converter = new();
            long t100 = (long)converter.ConvertToProvider(new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero))!;
            long t200 = (long)converter.ConvertToProvider(new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero))!;
            long t300 = (long)converter.ConvertToProvider(new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero))!;
            await initialDb.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "Users" ("Id", "Email", "PasswordHash", "Role", "CreatedAt")
                VALUES ('11111111-1111-1111-1111-111111111111', 'u@test.local', 'hash', 'JobSeeker', {t100});
                INSERT INTO "Vacancies" ("Id", "Title", "Company", "Description", "Category", "Location", "WorkFormat", "Currency", "Source", "SourceUrl", "SkillsJson", "SearchText", "PublishedAt", "FirstSeenAt", "LastSeenAt", "IsActive")
                VALUES ('22222222-2222-2222-2222-222222222222', 'Dev', 'Co', 'Desc', '.NET', 'Київ', 0, 'USD', 'Djinni', 'https://djinni.co/1', '[]', 'dev', {t100}, {t100}, {t100}, 1);
                INSERT INTO "SavedVacancies" ("Id", "VacancyId", "UserId", "Notes", "SavedAt")
                VALUES
                    ('aaaa0001-0000-0000-0000-000000000001', '22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', 'older note', {t100}),
                    ('aaaa0002-0000-0000-0000-000000000002', '22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', 'newest note', {t200}),
                    ('aaaa0003-0000-0000-0000-000000000003', '22222222-2222-2222-2222-222222222222', NULL, 'orphan null user', {t300});
                INSERT INTO "UserPersonalData" ("Id", "UserId", "Address", "UpdatedAt")
                VALUES ('bbbb0001-0000-0000-0000-000000000001', '99999999-9999-9999-9999-999999999999', 'Ghost street', {t100});
                """);
        }

        // Act
        await using (WaterfallDbContext migratedDb = new(options))
        {
            await migratedDb.Database.MigrateAsync();

            // Assert
            List<SavedVacancyDB> rows = await migratedDb.SavedVacancies.AsNoTracking().ToListAsync();
            Assert.Single(rows);
            Assert.Equal(userId, rows[0].UserId);
            Assert.Equal(vacancyId, rows[0].VacancyId);
            Assert.Equal("newest note", rows[0].Notes);
            Assert.Equal(0, await migratedDb.UserPersonalData.CountAsync());
        }
    }

    [Fact]
    public async Task MigrateAsync_FromSavedIntegrity_DeduplicatesVacanciesAndCreatesUniqueExternalIdIndex()
    {
        // Arrange
        await using SqliteConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync();
        DbContextOptions<WaterfallDbContext> options = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (WaterfallDbContext beforeDb = new(options))
        {
            await beforeDb.GetService<IMigrator>().MigrateAsync(SavedIntegrityMigrationId);
            Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter converter = new();
            long older = (long)converter.ConvertToProvider(new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero))!;
            long newer = (long)converter.ConvertToProvider(new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero))!;
            string longUrl = "https://djinni.co/jobs/" + new string('a', 500);

            await beforeDb.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "Vacancies" ("Id", "Title", "Company", "Description", "Category", "Location", "WorkFormat", "Currency", "Source", "SourceUrl", "SkillsJson", "SearchText", "PublishedAt", "FirstSeenAt", "LastSeenAt", "ExternalId", "IsActive")
                VALUES
                    ('33330001-0000-0000-0000-000000000001', 'Older Dup', 'Co', 'Desc', '.NET', 'Київ', 0, 'USD', 'Djinni', {longUrl}, '[]', 'older', {older}, {older}, {older}, 'dup-1', 1),
                    ('33330002-0000-0000-0000-000000000002', 'Newest Dup', 'Co', 'Desc', '.NET', 'Київ', 0, 'USD', 'Djinni', {longUrl}, '[]', 'newest', {older}, {older}, {newer}, 'dup-1', 1),
                    ('33330003-0000-0000-0000-000000000003', 'Null Ext A', 'Co', 'Desc', '.NET', 'Київ', 0, 'USD', 'Djinni', 'https://djinni.co/a', '[]', 'null a', {older}, {older}, {older}, NULL, 1),
                    ('33330004-0000-0000-0000-000000000004', 'Null Ext B', 'Co', 'Desc', '.NET', 'Київ', 0, 'USD', 'Djinni', 'https://djinni.co/b', '[]', 'null b', {older}, {older}, {older}, NULL, 1);
                """);
        }

        // Act
        await using (WaterfallDbContext migratedDb = new(options))
        {
            await migratedDb.Database.MigrateAsync();

            // Assert
            VacancyDB survivor = await migratedDb.Vacancies.AsNoTracking().SingleAsync(v => v.ExternalId == "dup-1");
            Assert.Equal("Newest Dup", survivor.Title);
            Assert.Equal(450, survivor.SourceUrl.Length);
            Assert.Equal(3, await migratedDb.Vacancies.CountAsync());

            migratedDb.Vacancies.Add(new VacancyDB
            {
                Id = Guid.NewGuid(),
                Title = "Conflicting Insert",
                Company = "Co",
                Description = "Desc",
                Category = ".NET",
                Location = "Київ",
                Currency = "USD",
                Source = "Djinni",
                SourceUrl = "https://djinni.co/conflict",
                ExternalId = "dup-1"
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => migratedDb.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task MigrateAsync_FromIndexVacancies_ClearsFullAddressSentinelsAndPreservesRealAddresses()
    {
        // Arrange
        await using SqliteConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync();
        DbContextOptions<WaterfallDbContext> options = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (WaterfallDbContext beforeDb = new(options))
        {
            await beforeDb.GetService<IMigrator>().MigrateAsync(IndexVacanciesMigrationId);
            Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter converter = new();
            long ts = (long)converter.ConvertToProvider(new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero))!;

            await beforeDb.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "Vacancies" ("Id", "Title", "Company", "Description", "Category", "Location", "FullAddress", "WorkFormat", "Currency", "Source", "SourceUrl", "SkillsJson", "SearchText", "PublishedAt", "FirstSeenAt", "LastSeenAt", "ExternalId", "IsActive")
                VALUES
                    ('55550001-0000-0000-0000-000000000001', 'Sentinel Job', 'Co', 'Desc', '.NET', 'Київ', 'Київ, не вказано точну адресу', 0, 'USD', 'Djinni', 'https://djinni.co/s1', '[]', 's1', {ts}, {ts}, {ts}, 's1', 1),
                    ('55550002-0000-0000-0000-000000000002', 'HQ Job', 'Co', 'Desc', '.NET', 'Київ', 'Київ (Головний офіс)', 0, 'USD', 'Djinni', 'https://djinni.co/s2', '[]', 's2', {ts}, {ts}, {ts}, 's2', 1),
                    ('55550003-0000-0000-0000-000000000003', 'Street Job', 'Co', 'Desc', '.NET', 'Київ', 'Київ, вул. Мечникова, 2', 0, 'USD', 'Djinni', 'https://djinni.co/s3', '[]', 's3', {ts}, {ts}, {ts}, 's3', 1);
                """);
        }

        // Act
        await using (WaterfallDbContext migratedDb = new(options))
        {
            await migratedDb.Database.MigrateAsync();

            // Assert
            VacancyDB sentinel = await migratedDb.Vacancies.AsNoTracking().SingleAsync(v => v.ExternalId == "s1");
            VacancyDB hq = await migratedDb.Vacancies.AsNoTracking().SingleAsync(v => v.ExternalId == "s2");
            VacancyDB street = await migratedDb.Vacancies.AsNoTracking().SingleAsync(v => v.ExternalId == "s3");
            Assert.Null(sentinel.FullAddress);
            Assert.Null(hq.FullAddress);
            Assert.Equal("Київ, вул. Мечникова, 2", street.FullAddress);
        }
    }

    [Fact]
    public async Task MigrateAsync_FromClearAddressSentinel_DropsSearchTextColumn()
    {
        // Arrange
        await using SqliteConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync();
        DbContextOptions<WaterfallDbContext> options = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (WaterfallDbContext beforeDb = new(options))
        {
            await beforeDb.GetService<IMigrator>().MigrateAsync(ClearAddressSentinelMigrationId);
        }

        // Act
        await using (WaterfallDbContext migratedDb = new(options))
        {
            await migratedDb.Database.MigrateAsync();
        }

        // Assert
        await using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA table_info('Vacancies');";
        await using System.Data.Common.DbDataReader reader = await pragma.ExecuteReaderAsync();
        List<string> columnNames = [];
        while (await reader.ReadAsync())
        {
            columnNames.Add(reader.GetString(1));
        }

        Assert.DoesNotContain("SearchText", columnNames, StringComparer.OrdinalIgnoreCase);
    }
}
