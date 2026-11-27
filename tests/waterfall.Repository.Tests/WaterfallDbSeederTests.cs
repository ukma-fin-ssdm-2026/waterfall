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
            Assert.True(await db.GeoLocationCache.AnyAsync());
        }
    }

    [Fact]
    public async Task SeedAsync_WithoutDemoData_CreatesSchemaButNoFakeVacanciesOrTestAccounts()
    {
        // Поза Development API викликає сідер без демо-даних: тестових акаунтів із відомими паролями бути не може
        using (WaterfallDbContext db = new(_options))
        {
            await WaterfallDbSeeder.SeedAsync(db, includeDemoData: false);
        }

        using (WaterfallDbContext db = new(_options))
        {
            Assert.False(await db.Vacancies.AnyAsync());
            Assert.False(await db.SavedVacancies.AnyAsync());
            Assert.False(await db.Users.AnyAsync());
            Assert.False(await db.UserPersonalData.AnyAsync());
            Assert.False(await db.GeoLocationCache.AnyAsync());
            // Таблиці, які сідер дописує сам (не з моделі EnsureCreated), теж на місці
            Assert.Equal(0, await db.SiteSearchCache.CountAsync());
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

    [Fact]
    public async Task SeedAsync_UpgradesLegacySqliteSchemaWithoutCategoryGroupAndBackfillsNormalizedFields()
    {
        // Імітуємо старий файл waterfall.db, створений до додавання CategoryGroup/ExperienceMonths/WorkFormat
        using (SqliteCommand cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE "Vacancies" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "Title" TEXT NOT NULL,
                    "Company" TEXT NOT NULL,
                    "Description" TEXT NOT NULL,
                    "Category" TEXT NOT NULL,
                    "Location" TEXT NOT NULL,
                    "FullAddress" TEXT NULL,
                    "Schedule" TEXT NULL,
                    "Experience" TEXT NULL,
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
                    "UserId" TEXT NOT NULL,
                    "VacancyId" TEXT NOT NULL,
                    "Notes" TEXT NULL,
                    "SavedAt" INTEGER NOT NULL,
                    PRIMARY KEY ("UserId", "VacancyId")
                );
                CREATE TABLE "Users" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "Email" TEXT NOT NULL,
                    "PasswordHash" TEXT NOT NULL,
                    "Role" TEXT NOT NULL,
                    "CreatedAt" INTEGER NOT NULL
                );
                INSERT INTO "Vacancies"
                    ("Id","Title","Company","Description","Category","Location","FullAddress","Schedule","Experience","Currency","Source","SourceUrl","SkillsJson","PublishedAt","FirstSeenAt","LastSeenAt","IsActive")
                VALUES
                    ('99999999-9999-9999-9999-999999999999','Senior C# Developer','LegacyCo','ASP.NET Core backend','C# / .NET','Київ (Віддалено)','Київ, Хрещатик 1','Повна зайнятість','3+ роки','USD','Djinni','https://djinni.co/jobs/legacy-1/','["C#"]',0,0,0,1);
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        using (WaterfallDbContext db = new(_options))
        {
            await WaterfallDbSeeder.SeedAsync(db);
        }

        using (WaterfallDbContext db = new(_options))
        {
            Waterfall.Repository.Entities.VacancyDB? upgraded = await db.Vacancies
                .SingleOrDefaultAsync(v => v.Id == Guid.Parse("99999999-9999-9999-9999-999999999999"));
            Assert.NotNull(upgraded);
            Assert.Equal("Software Development", upgraded.CategoryGroup);
            Assert.Equal(Waterfall.Repository.Entities.WorkFormat.Remote, upgraded.WorkFormat);
            Assert.Equal(36, upgraded.ExperienceMonths);
        }
    }

    [Fact]
    public async Task SeedAsync_LeavesNullFullAddressAsNull_OnExistingVacancies()
    {
        using (WaterfallDbContext db = new(_options))
        {
            await WaterfallDbSeeder.SeedAsync(db, includeDemoData: false);
            db.Vacancies.Add(new Waterfall.Repository.Entities.VacancyDB
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Title = "Backend Engineer",
                Company = "Acme",
                Description = "ASP.NET Core",
                Category = ".NET",
                Location = "Київ",
                FullAddress = null,
                Currency = "USD",
                Source = "Djinni",
                SourceUrl = "https://djinni.co/jobs/acme-1/",
                SkillsJson = "[]",
                PublishedAt = DateTimeOffset.UtcNow,
                FirstSeenAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        using (WaterfallDbContext db = new(_options))
        {
            await WaterfallDbSeeder.SeedAsync(db, includeDemoData: false);
        }

        using (WaterfallDbContext db = new(_options))
        {
            Waterfall.Repository.Entities.VacancyDB? existing = await db.Vacancies
                .SingleOrDefaultAsync(v => v.Id == Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
            Assert.NotNull(existing);
            Assert.Null(existing.FullAddress);
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
