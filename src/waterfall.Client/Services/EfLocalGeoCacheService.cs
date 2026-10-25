// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.EntityFrameworkCore;
using Waterfall.Client.Data;
using Waterfall.Client.Entities;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Geo;
using Waterfall.Repository.Repositories;

namespace Waterfall.Client.Services;

/// <summary>
/// Реалізація локальної бази даних клієнта на базі Entity Framework Core (ClientWaterfallDbContext) та SQLite.
/// Дзеркальна до серверної бази даних (вакансії, користувачі тощо), але в таблиці координат зберігає прапорець IsSynced.
/// </summary>
public class EfLocalGeoCacheService : ILocalGeoCacheService
{
    private readonly IDbContextFactory<ClientWaterfallDbContext>? _contextFactory;
    private readonly DbContextOptions<ClientWaterfallDbContext>? _options;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;

    public EfLocalGeoCacheService(IDbContextFactory<ClientWaterfallDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public EfLocalGeoCacheService(string? databasePath = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "Waterfall");
            Directory.CreateDirectory(folder);
            databasePath = Path.Combine(folder, "waterfall_local.db");
        }
        else
        {
            string? dir = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        _options = new DbContextOptionsBuilder<ClientWaterfallDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
    }

    private ClientWaterfallDbContext CreateContext()
    {
        if (_contextFactory != null)
        {
            return _contextFactory.CreateDbContext();
        }

        if (_options != null)
        {
            return new ClientWaterfallDbContext(_options);
        }

        throw new InvalidOperationException("Неможливо створити контекст ClientWaterfallDbContext.");
    }

    private async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initLock.WaitAsync(ct);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            await using ClientWaterfallDbContext context = CreateContext();
            await context.Database.EnsureCreatedAsync(ct);
            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<(double Latitude, double Longitude)?> GetCoordinatesAsync(string address, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        string norm = EfGeoLocationCacheRepository.NormalizeKey(address);
        if (string.IsNullOrWhiteSpace(norm))
        {
            norm = address.Trim().ToLowerInvariant();
        }

        await EnsureInitializedAsync(ct);
        await using ClientWaterfallDbContext context = CreateContext();

        // 1. Точний збіг за нормалізованим ключем
        ClientGeoLocationCacheDB? exact = await context.GeoLocationCache
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.NormalizedAddress == norm, ct);

        if (exact != null)
        {
            return (exact.Latitude, exact.Longitude);
        }

        // 2. Збіг за підрядком через LIKE (якщо ключ достатньо довгий)
        if (norm.Length >= 4)
        {
            ClientGeoLocationCacheDB? partial = await context.GeoLocationCache
                .AsNoTracking()
                .FirstOrDefaultAsync(c => EF.Functions.Like(c.NormalizedAddress, "%" + norm + "%"), ct);

            if (partial != null)
            {
                return (partial.Latitude, partial.Longitude);
            }
        }

        return null;
    }

    public async Task SaveCoordinatesAsync(string address, double latitude, double longitude, bool isSynced, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        string norm = EfGeoLocationCacheRepository.NormalizeKey(address);
        if (string.IsNullOrWhiteSpace(norm))
        {
            norm = address.Trim().ToLowerInvariant();
        }

        await EnsureInitializedAsync(ct);
        await using ClientWaterfallDbContext context = CreateContext();

        ClientGeoLocationCacheDB? existing = await context.GeoLocationCache
            .FirstOrDefaultAsync(c => c.NormalizedAddress == norm, ct);

        if (existing != null)
        {
            existing.Latitude = latitude;
            existing.Longitude = longitude;
            existing.CreatedAt = DateTimeOffset.UtcNow;
            existing.IsSynced = isSynced;
        }
        else
        {
            ClientGeoLocationCacheDB entry = new()
            {
                Id = Guid.NewGuid(),
                NormalizedAddress = norm,
                Latitude = latitude,
                Longitude = longitude,
                CreatedAt = DateTimeOffset.UtcNow,
                IsSynced = isSynced
            };
            context.GeoLocationCache.Add(entry);
        }

        await context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ClientGeoLocationCacheDB>> GetUnsyncedAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using ClientWaterfallDbContext context = CreateContext();

        return await context.GeoLocationCache
            .Where(c => !c.IsSynced)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task MarkAsSyncedAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        List<Guid> idList = ids.ToList();
        if (idList.Count == 0)
        {
            return;
        }

        await EnsureInitializedAsync(ct);
        await using ClientWaterfallDbContext context = CreateContext();

        List<ClientGeoLocationCacheDB> items = await context.GeoLocationCache
            .Where(c => idList.Contains(c.Id))
            .ToListAsync(ct);

        foreach (ClientGeoLocationCacheDB item in items)
        {
            item.IsSynced = true;
        }

        await context.SaveChangesAsync(ct);
    }

    public async Task UpsertFromServerAsync(IEnumerable<GeoSyncItemDto> items, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using ClientWaterfallDbContext context = CreateContext();

        foreach (GeoSyncItemDto item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Address))
            {
                continue;
            }

            string norm = EfGeoLocationCacheRepository.NormalizeKey(item.Address);
            if (string.IsNullOrWhiteSpace(norm))
            {
                norm = item.Address.Trim().ToLowerInvariant();
            }

            ClientGeoLocationCacheDB? existing = await context.GeoLocationCache
                .FirstOrDefaultAsync(c => c.NormalizedAddress == norm, ct);

            if (existing != null)
            {
                existing.Latitude = item.Latitude;
                existing.Longitude = item.Longitude;
                existing.CreatedAt = item.CreatedAt;
                existing.IsSynced = true;
            }
            else
            {
                ClientGeoLocationCacheDB entry = new()
                {
                    Id = item.Id != Guid.Empty ? item.Id : Guid.NewGuid(),
                    NormalizedAddress = norm,
                    Latitude = item.Latitude,
                    Longitude = item.Longitude,
                    CreatedAt = item.CreatedAt,
                    IsSynced = true
                };
                context.GeoLocationCache.Add(entry);
            }
        }

        await context.SaveChangesAsync(ct);
    }
}
