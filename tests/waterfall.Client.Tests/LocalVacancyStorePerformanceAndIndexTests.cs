using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Waterfall.Client.Data;
using Waterfall.Client.Entities;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.Services;

namespace Waterfall.Client.Tests;

public sealed class LocalVacancyStorePerformanceAndIndexTests : IDisposable
{
    private static readonly DateTimeOffset BaseTime = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"waterfall-client-perf-{Guid.NewGuid():N}.db");
    private readonly VacancySelectAuditInterceptor _selectAudit = new();
    private readonly TrackingContextFactory _contextFactory;

    public LocalVacancyStorePerformanceAndIndexTests()
    {
        DbContextOptions<ClientWaterfallDbContext> options = new DbContextOptionsBuilder<ClientWaterfallDbContext>()
            .UseSqlite($"Data Source={_dbPath};Default Timeout=5")
            .AddInterceptors(_selectAudit)
            .Options;

        _contextFactory = new TrackingContextFactory(options);
    }

    public void Dispose()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task ClientContext_RejectsDuplicateSourceAndExternalId_AndAllowsMultipleNullExternalIds()
    {
        await using ClientWaterfallDbContext db = _contextFactory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();

        db.Vacancies.AddRange(
            CreateEntity(source: "Djinni", externalId: null, sourceUrl: "https://djinni.co/jobs/null-1/"),
            CreateEntity(source: "Djinni", externalId: null, sourceUrl: "https://djinni.co/jobs/null-2/"),
            CreateEntity(source: "Djinni", externalId: "client-100", sourceUrl: "https://djinni.co/jobs/100/"));
        await db.SaveChangesAsync();

        db.Vacancies.Add(CreateEntity(source: "Djinni", externalId: "client-100", sourceUrl: "https://djinni.co/jobs/100-dup/"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveVacanciesAsync_On10kTable_IssuesNoFullTableScan_AndTruncatesLongSourceUrl()
    {
        await SeedLocalVacanciesAsync(10_000);
        EfLocalVacancyStore store = new(_contextFactory);

        string overlongUrl = "https://djinni.co/jobs/" + new string('a', 500);
        List<VacancyDto> batch = BuildDtoBatch(updateCount: 110, createCount: 140, overlongUrl);
        _selectAudit.Clear();

        int affected = await store.SaveVacanciesAsync(batch, isSynced: true);

        Assert.Equal(250, affected);
        Assert.NotEmpty(_selectAudit.VacancySelects);
        Assert.All(_selectAudit.VacancySelects, sql => Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase));

        await using ClientWaterfallDbContext verifyDb = _contextFactory.CreateDbContext();
        Assert.Equal(10_140, await verifyDb.Vacancies.CountAsync());
        ClientVacancyDB createdWithLongUrl = await verifyDb.Vacancies.AsNoTracking()
            .SingleAsync(v => v.ExternalId == "dto-new-00001");
        Assert.Equal(450, createdWithLongUrl.SourceUrl.Length);
    }

    [Fact]
    public async Task UpsertFromServerAsync_On10kTable_IssuesNoFullTableScan_AndReplacesCanonicalIdSafely()
    {
        await SeedLocalVacanciesAsync(10_000);
        EfLocalVacancyStore store = new(_contextFactory);

        Guid canonicalServerId = Guid.NewGuid();
        VacancySyncItemDto canonicalReplace = CreateSyncItem(
            id: canonicalServerId,
            title: "Server Canonical Title",
            source: JobSource.Djinni,
            externalId: "local-00050",
            sourceUrl: "https://djinni.co/jobs/local-00050/",
            lastSeenAt: BaseTime.AddHours(3));

        VacancySyncItemDto urlMatch = CreateSyncItem(
            id: Guid.NewGuid(),
            title: "Matched By Url",
            source: JobSource.Djinni,
            externalId: null,
            sourceUrl: "https://djinni.co/jobs/local-00250/",
            lastSeenAt: BaseTime.AddHours(3));

        VacancySyncItemDto brandNew = CreateSyncItem(
            id: Guid.NewGuid(),
            title: "Brand New From Server",
            source: JobSource.Dou,
            externalId: "server-new-1",
            sourceUrl: "https://jobs.dou.ua/vacancies/server-new-1/",
            lastSeenAt: BaseTime.AddHours(3));

        _selectAudit.Clear();

        (int added, int updated) = await store.UpsertFromServerAsync([canonicalReplace, urlMatch, brandNew]);

        Assert.Equal(1, added);
        Assert.Equal(2, updated);
        Assert.NotEmpty(_selectAudit.VacancySelects);
        Assert.All(_selectAudit.VacancySelects, sql => Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase));

        await using ClientWaterfallDbContext verifyDb = _contextFactory.CreateDbContext();
        ClientVacancyDB replaced = await verifyDb.Vacancies.AsNoTracking()
            .SingleAsync(v => v.Source == "Djinni" && v.ExternalId == "local-00050");
        Assert.Equal(canonicalServerId, replaced.Id);
        Assert.Equal("Server Canonical Title", replaced.Title);

        ClientVacancyDB urlUpdated = await verifyDb.Vacancies.AsNoTracking()
            .SingleAsync(v => v.Source == "Djinni" && v.ExternalId == "local-00250");
        Assert.Equal("Matched By Url", urlUpdated.Title);
    }

    private async Task SeedLocalVacanciesAsync(int count)
    {
        await using ClientWaterfallDbContext db = _contextFactory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();

        List<ClientVacancyDB> rows = new(count);
        for (int i = 1; i <= count; i++)
        {
            rows.Add(CreateEntity(
                title: $"Local Seed #{i}",
                source: "Djinni",
                externalId: $"local-{i:D5}",
                sourceUrl: $"https://djinni.co/jobs/local-{i:D5}/"));
        }

        await db.Vacancies.AddRangeAsync(rows);
        await db.SaveChangesAsync();
    }

    private static List<VacancyDto> BuildDtoBatch(int updateCount, int createCount, string firstNewSourceUrl)
    {
        List<VacancyDto> batch = new(updateCount + createCount);
        for (int i = 1; i <= updateCount; i++)
        {
            batch.Add(new VacancyDto
            {
                Id = Guid.NewGuid(),
                ExternalId = $"local-{i:D5}",
                Title = $"Updated Local #{i}",
                Company = "LocalCo",
                Description = "Updated description",
                Category = ".NET",
                Location = "Kyiv",
                Currency = "USD",
                Source = JobSource.Djinni,
                SourceUrl = $"https://djinni.co/jobs/local-{i:D5}/",
                LastSeenAt = BaseTime.AddHours(2)
            });
        }

        for (int i = 1; i <= createCount; i++)
        {
            batch.Add(new VacancyDto
            {
                Id = Guid.NewGuid(),
                ExternalId = $"dto-new-{i:D5}",
                Title = $"Created Local #{i}",
                Company = "LocalCo",
                Description = "Created description",
                Category = ".NET",
                Location = "Kyiv",
                Currency = "USD",
                Source = JobSource.Djinni,
                SourceUrl = i == 1 ? firstNewSourceUrl : $"https://djinni.co/jobs/dto-new-{i:D5}/",
                LastSeenAt = BaseTime.AddHours(2)
            });
        }

        return batch;
    }

    private static ClientVacancyDB CreateEntity(
        string title = "Client Vacancy",
        string source = "Djinni",
        string? externalId = "ext-1",
        string sourceUrl = "https://djinni.co/jobs/1/") => new()
        {
            Id = Guid.NewGuid(),
            ExternalId = externalId,
            Title = title,
            Company = "ClientCo",
            Description = "Client description",
            Category = ".NET",
            Location = "Kyiv",
            Currency = "USD",
            Source = source,
            SourceUrl = sourceUrl,
            SkillsJson = "[\"C#\"]",
            PublishedAt = BaseTime,
            FirstSeenAt = BaseTime,
            LastSeenAt = BaseTime,
            IsActive = true,
            IsSynced = true,
            LocalUpdatedAt = BaseTime
        };

    private static VacancySyncItemDto CreateSyncItem(
        Guid id,
        string title,
        JobSource source,
        string? externalId,
        string sourceUrl,
        DateTimeOffset lastSeenAt) => new()
        {
            Id = id,
            ExternalId = externalId,
            Title = title,
            Company = "ServerCo",
            Description = "Server description",
            Category = ".NET",
            Location = "Kyiv",
            Currency = "USD",
            Source = source,
            SourceUrl = sourceUrl,
            PublishedAt = BaseTime,
            FirstSeenAt = BaseTime,
            LastSeenAt = lastSeenAt,
            IsActive = true
        };

    private sealed class TrackingContextFactory(DbContextOptions<ClientWaterfallDbContext> options)
        : IDbContextFactory<ClientWaterfallDbContext>
    {
        public ClientWaterfallDbContext CreateDbContext() => new(options);
    }

    private sealed class VacancySelectAuditInterceptor : DbCommandInterceptor
    {
        public List<string> VacancySelects { get; } = [];

        public void Clear() => VacancySelects.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            RecordIfVacancySelect(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            RecordIfVacancySelect(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        private void RecordIfVacancySelect(string sql)
        {
            if (sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("\"Vacancies\"", StringComparison.OrdinalIgnoreCase)
                && !sql.Contains("COUNT(*)", StringComparison.OrdinalIgnoreCase))
            {
                VacancySelects.Add(sql);
            }
        }
    }
}
