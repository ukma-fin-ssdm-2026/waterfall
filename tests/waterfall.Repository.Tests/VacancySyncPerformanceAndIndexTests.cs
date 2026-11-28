using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Repository.Repositories;

namespace Waterfall.Repository.Tests;

public sealed class VacancySyncPerformanceAndIndexTests : IDisposable
{
    private static readonly DateTimeOffset BaseTime = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<WaterfallDbContext> _options;
    private readonly VacancySelectAuditInterceptor _selectAudit = new();

    public VacancySyncPerformanceAndIndexTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_selectAudit)
            .Options;

        using WaterfallDbContext db = new(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task InsertDuplicateSourceAndExternalId_ThrowsDbUpdateException()
    {
        await using WaterfallDbContext db = new(_options);
        db.Vacancies.Add(CreateVacancy(source: "Djinni", externalId: "dup-100", sourceUrl: "https://djinni.co/jobs/100/"));
        await db.SaveChangesAsync();

        db.Vacancies.Add(CreateVacancy(source: "Djinni", externalId: "dup-100", sourceUrl: "https://djinni.co/jobs/100-copy/"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task InsertMultipleVacanciesWithNullExternalId_SucceedsForSameSource()
    {
        await using WaterfallDbContext db = new(_options);
        db.Vacancies.AddRange(
            CreateVacancy(source: "Djinni", externalId: null, sourceUrl: "https://djinni.co/jobs/null-1/"),
            CreateVacancy(source: "Djinni", externalId: null, sourceUrl: "https://djinni.co/jobs/null-2/"));

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Vacancies.CountAsync());
    }

    [Fact]
    public async Task CandidateLookupQueries_UseExternalIdAndSourceUrlIndexes_WithoutTableScan()
    {
        await SeedVacanciesAsync(500);
        await ExecuteSqlNonQueryAsync("ANALYZE;");
        _selectAudit.Clear();

        await using WaterfallDbContext db = new(_options);
        EfVacancyRepository repo = new(db);

        VacancyDB incomingByExternal = CreateVacancy(
            source: "Djinni",
            externalId: "seed-00042",
            sourceUrl: "https://djinni.co/jobs/nonexistent-url/");
        VacancyDB incomingByUrl = CreateVacancy(
            source: "Djinni",
            externalId: null,
            sourceUrl: "https://djinni.co/jobs/seed-00123/");

        await repo.SyncMergeAsync([incomingByExternal, incomingByUrl]);

        List<CapturedSql> vacancySelects = _selectAudit.VacancySelects;
        Assert.NotEmpty(vacancySelects);
        Assert.All(vacancySelects, s => Assert.Contains("WHERE", s.Sql, StringComparison.OrdinalIgnoreCase));

        List<string> allPlans = [];
        foreach (CapturedSql query in vacancySelects)
        {
            List<string> plan = await ExplainQueryPlanAsync(query);
            allPlans.AddRange(plan);
            Assert.DoesNotContain(plan, line => line.Contains("SCAN Vacancies", StringComparison.OrdinalIgnoreCase));
        }

        Assert.Contains(allPlans, line => line.Contains("IX_Vacancies_Source_ExternalId", StringComparison.Ordinal));
        Assert.Contains(allPlans, line => line.Contains("IX_Vacancies_SourceUrl", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BulkUpsertAsync_On10kTable_IssuesNoFullTableScan_AndUpdatesAndCreatesAcrossChunks()
    {
        await SeedVacanciesAsync(10_000);
        List<VacancyDB> batch = BuildBulkUpsertBatch(updateCount: 120, createCount: 130);

        await using WaterfallDbContext db = new(_options);
        EfVacancyRepository repo = new(db);
        _selectAudit.Clear();

        (int created, int updated) = await repo.BulkUpsertAsync(batch);

        Assert.Equal(130, created);
        Assert.Equal(120, updated);
        Assert.NotEmpty(_selectAudit.VacancySelects);
        Assert.All(_selectAudit.VacancySelects, q => Assert.Contains("WHERE", q.Sql, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(10_130, await db.Vacancies.CountAsync());

        VacancyDB? updatedRow = await db.Vacancies.AsNoTracking()
            .SingleOrDefaultAsync(v => v.Source == "Djinni" && v.ExternalId == "seed-00005");
        Assert.NotNull(updatedRow);
        Assert.Equal("Bulk Updated #5", updatedRow.Title);
    }

    [Fact]
    public async Task SyncMergeAsync_On10kTable_IssuesNoFullTableScan_AndMergesByExternalIdIdAndUrl()
    {
        await SeedVacanciesAsync(10_000);

        VacancyDB targetByUrl;
        VacancyDB targetById;
        await using (WaterfallDbContext lookupDb = new(_options))
        {
            targetByUrl = await lookupDb.Vacancies.AsNoTracking()
                .SingleAsync(v => v.Source == "Djinni" && v.ExternalId == "seed-00500");
            targetById = await lookupDb.Vacancies.AsNoTracking()
                .SingleAsync(v => v.Source == "Djinni" && v.ExternalId == "seed-00600");
        }

        VacancyDB byExternal = CreateVacancy(
            title: "Merged By ExternalId",
            source: "Djinni",
            externalId: "seed-00010",
            sourceUrl: "https://djinni.co/jobs/other-10/",
            lastSeenAt: BaseTime.AddHours(4));

        VacancyDB byId = CreateVacancy(
            title: "Merged By Id",
            source: "Djinni",
            externalId: null,
            sourceUrl: "https://djinni.co/jobs/other-600/",
            lastSeenAt: BaseTime.AddHours(4));
        byId.Id = targetById.Id;

        VacancyDB byUrl = CreateVacancy(
            title: "Merged By Url",
            source: "Djinni",
            externalId: null,
            sourceUrl: targetByUrl.SourceUrl,
            lastSeenAt: BaseTime.AddHours(4));

        VacancyDB brandNew = CreateVacancy(
            title: "Brand New Synced",
            source: "Dou",
            externalId: "dou-new-1",
            sourceUrl: "https://jobs.dou.ua/vacancies/new-1/",
            lastSeenAt: BaseTime.AddHours(4));

        await using WaterfallDbContext db = new(_options);
        EfVacancyRepository repo = new(db);
        _selectAudit.Clear();

        VacancySyncMergeResult result = await repo.SyncMergeAsync([byExternal, byId, byUrl, brandNew]);

        Assert.Equal(1, result.Created);
        Assert.Equal(3, result.Updated);
        Assert.Equal(4, result.CanonicalIds.Count);
        Assert.Contains(targetById.Id, result.CanonicalIds);
        Assert.Contains(targetByUrl.Id, result.CanonicalIds);
        Assert.Contains(brandNew.Id, result.CanonicalIds);

        Assert.NotEmpty(_selectAudit.VacancySelects);
        Assert.All(_selectAudit.VacancySelects, q => Assert.Contains("WHERE", q.Sql, StringComparison.OrdinalIgnoreCase));

        Assert.Equal("Merged By ExternalId", (await db.Vacancies.AsNoTracking().SingleAsync(v => v.ExternalId == "seed-00010")).Title);
        Assert.Equal("Merged By Id", (await db.Vacancies.AsNoTracking().SingleAsync(v => v.Id == targetById.Id)).Title);
        Assert.Equal("Merged By Url", (await db.Vacancies.AsNoTracking().SingleAsync(v => v.Id == targetByUrl.Id)).Title);
    }

    private async Task SeedVacanciesAsync(int count)
    {
        await using WaterfallDbContext db = new(_options);
        List<VacancyDB> rows = new(count);
        for (int i = 1; i <= count; i++)
        {
            rows.Add(CreateVacancy(
                title: $"Seed Vacancy #{i}",
                source: "Djinni",
                externalId: $"seed-{i:D5}",
                sourceUrl: $"https://djinni.co/jobs/seed-{i:D5}/",
                lastSeenAt: BaseTime));
        }

        await db.Vacancies.AddRangeAsync(rows);
        await db.SaveChangesAsync();
    }

    private static List<VacancyDB> BuildBulkUpsertBatch(int updateCount, int createCount)
    {
        List<VacancyDB> batch = new(updateCount + createCount);
        for (int i = 1; i <= updateCount; i++)
        {
            batch.Add(CreateVacancy(
                title: $"Bulk Updated #{i}",
                source: "Djinni",
                externalId: $"seed-{i:D5}",
                sourceUrl: $"https://djinni.co/jobs/seed-{i:D5}/",
                lastSeenAt: BaseTime.AddHours(2)));
        }

        for (int i = 1; i <= createCount; i++)
        {
            batch.Add(CreateVacancy(
                title: $"Bulk Created #{i}",
                source: "Djinni",
                externalId: $"bulk-new-{i:D5}",
                sourceUrl: $"https://djinni.co/jobs/bulk-new-{i:D5}/",
                lastSeenAt: BaseTime.AddHours(2)));
        }

        return batch;
    }

    private async Task ExecuteSqlNonQueryAsync(string sql)
    {
        await using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> ExplainQueryPlanAsync(CapturedSql query)
    {
        await using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = $"EXPLAIN QUERY PLAN {query.Sql}";
        foreach ((string name, object? value) in query.Parameters)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        List<string> details = [];
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            details.Add(reader.GetString(3));
        }

        return details;
    }

    private static VacancyDB CreateVacancy(
        string title = "Backend Developer",
        string source = "Djinni",
        string? externalId = "ext-1",
        string sourceUrl = "https://djinni.co/jobs/1/",
        DateTimeOffset? lastSeenAt = null)
    {
        DateTimeOffset timestamp = lastSeenAt ?? BaseTime;
        return new VacancyDB
        {
            Id = Guid.NewGuid(),
            ExternalId = externalId,
            Title = title,
            Company = "TechCorp",
            Description = $"Description for {title}",
            Category = ".NET",
            Location = "Kyiv",
            Currency = "USD",
            Source = source,
            SourceUrl = sourceUrl,
            SkillsJson = "[\"C#\"]",
            PublishedAt = timestamp,
            FirstSeenAt = timestamp,
            LastSeenAt = timestamp,
            IsActive = true
        };
    }

    private sealed record CapturedSql(string Sql, IReadOnlyList<(string Name, object? Value)> Parameters);

    private sealed class VacancySelectAuditInterceptor : DbCommandInterceptor
    {
        public List<CapturedSql> VacancySelects { get; } = [];

        public void Clear() => VacancySelects.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            RecordIfVacancySelect(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            RecordIfVacancySelect(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        private void RecordIfVacancySelect(DbCommand command)
        {
            string sql = command.CommandText;
            if (!sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                || !sql.Contains("\"Vacancies\"", StringComparison.OrdinalIgnoreCase)
                || sql.Contains("EXPLAIN QUERY PLAN", StringComparison.OrdinalIgnoreCase)
                || sql.Contains("COUNT(*)", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            List<(string Name, object? Value)> parameters = [];
            foreach (DbParameter parameter in command.Parameters)
            {
                parameters.Add((parameter.ParameterName, parameter.Value));
            }

            VacancySelects.Add(new CapturedSql(sql, parameters));
        }
    }
}
