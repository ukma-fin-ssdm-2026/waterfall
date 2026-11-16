// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Waterfall.Api.Workers;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Repository.Repositories;
using Waterfall.Services;
using Waterfall.Services.Models.Common;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Ingestion.Tests;

public sealed class VacancyExpirationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _services;
    private readonly StubVacancyCacheService _cache = new();

    public VacancyExpirationServiceTests()
    {
        _connection.Open();
        _services = new ServiceCollection()
            .AddDbContext<WaterfallDbContext>(options => options.UseSqlite(_connection))
            .AddScoped<IVacancyRepository, EfVacancyRepository>()
            .BuildServiceProvider();

        using IServiceScope scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<WaterfallDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task DeactivateOnceAsync_DeactivatesExpiredVacancies_AndInvalidatesCache()
    {
        DateTimeOffset now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        await SeedVacanciesAsync(
            CreateVacancy("Old", now.AddDays(-31), isActive: true),
            CreateVacancy("Fresh", now.AddDays(-5), isActive: true));

        VacancyExpirationService worker = new(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _cache,
            NullLogger<VacancyExpirationService>.Instance,
            new FixedTimeProvider(now));

        int deactivated = await worker.DeactivateOnceAsync();

        Assert.Equal(1, deactivated);
        Assert.Equal(1, _cache.InvalidateCatalogCalls);
    }

    [Fact]
    public async Task DeactivateOnceAsync_WhenNoneExpired_DoesNotInvalidateCache()
    {
        DateTimeOffset now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        await SeedVacanciesAsync(CreateVacancy("Fresh", now.AddDays(-2), isActive: true));

        VacancyExpirationService worker = new(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _cache,
            NullLogger<VacancyExpirationService>.Instance,
            new FixedTimeProvider(now));

        int deactivated = await worker.DeactivateOnceAsync();

        Assert.Equal(0, deactivated);
        Assert.Equal(0, _cache.InvalidateCatalogCalls);
    }

    [Fact]
    public async Task StartAsync_RunsInitialSweepAndStopsCleanly()
    {
        DateTimeOffset now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        VacancyDB old = CreateVacancy("Expired", now.AddDays(-45), isActive: true);
        await SeedVacanciesAsync(old);

        VacancyExpirationService worker = new(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _cache,
            NullLogger<VacancyExpirationService>.Instance,
            new FixedTimeProvider(now));

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await worker.StopAsync(CancellationToken.None);

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        VacancyDB? reloaded = await scope.ServiceProvider.GetRequiredService<WaterfallDbContext>().Vacancies.FindAsync(old.Id);
        Assert.False(reloaded!.IsActive);
    }

    private async Task SeedVacanciesAsync(params VacancyDB[] vacancies)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        WaterfallDbContext db = scope.ServiceProvider.GetRequiredService<WaterfallDbContext>();
        db.Vacancies.AddRange(vacancies);
        await db.SaveChangesAsync();
    }

    private static VacancyDB CreateVacancy(string title, DateTimeOffset publishedAt, bool isActive) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Company = "Test Corp",
        Description = title,
        Category = ".NET",
        Location = "Kyiv",
        Source = "Djinni",
        SourceUrl = "https://djinni.co/jobs/" + Guid.NewGuid().ToString("N")[..8],
        PublishedAt = publishedAt,
        FirstSeenAt = publishedAt,
        IsActive = isActive
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubVacancyCacheService : IVacancyCacheService
    {
        public int InvalidateCatalogCalls { get; private set; }
        public DateTimeOffset? LastCatalogRefreshedAt => null;
        public Task<PagedResult<VacancyDto>?> GetPagedVacanciesAsync(VacancyFilterParameters filter, CancellationToken ct = default) => Task.FromResult<PagedResult<VacancyDto>?>(null);
        public Task SetPagedVacanciesAsync(VacancyFilterParameters filter, PagedResult<VacancyDto> result, TimeSpan? ttl = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<VacancyDto?> GetVacancyByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<VacancyDto?>(null);
        public Task SetVacancyByIdAsync(Guid id, VacancyDto vacancy, TimeSpan? ttl = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateVacancyAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public PagedResult<VacancyDto>? GetStaleCatalog() => null;
        public (long Hits, long Misses) GetMetrics() => (0, 0);

        public Task InvalidateCatalogAsync(CancellationToken ct = default)
        {
            InvalidateCatalogCalls++;
            return Task.CompletedTask;
        }
    }
}
