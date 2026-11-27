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
        Assert.Equal([InitialMigrationId], applied);
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
        Assert.Equal([InitialMigrationId], await db.Database.GetAppliedMigrationsAsync());
    }
}
