using Waterfall.Client.Entities;
using Waterfall.Client.Models.Geo;

namespace Waterfall.Client.Interfaces;

/// <summary>
/// Сервіс локальної SQLite бази даних Entity Framework Core для офлайн-режиму клієнта
/// та двосторонньої синхронізації координат із сервером.
/// Працює на базі загального контексту ClientWaterfallDbContext (дзеркало серверної БД).
/// </summary>
public interface ILocalGeoCacheService
{
    /// <summary>
    /// Отримати збережені координати з локальної бази EF Core.
    /// </summary>
    Task<(double Latitude, double Longitude)?> GetCoordinatesAsync(string address, CancellationToken ct = default);

    /// <summary>
    /// Зберегти або оновити координати адреси в локальній базі EF Core.
    /// </summary>
    Task SaveCoordinatesAsync(string address, double latitude, double longitude, bool isSynced, CancellationToken ct = default);

    /// <summary>
    /// Отримати всі несинхронізовані записи (де IsSynced == false) для вивантаження на сервер.
    /// </summary>
    Task<IReadOnlyList<ClientGeoLocationCacheDB>> GetUnsyncedAsync(CancellationToken ct = default);

    /// <summary>
    /// Позначити передані ідентифікатори як синхронізовані (IsSynced = true).
    /// </summary>
    Task MarkAsSyncedAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

    /// <summary>
    /// Зберегти нові записи, отримані із сервера під час синхронізації (з IsSynced = true).
    /// </summary>
    Task UpsertFromServerAsync(IEnumerable<GeoSyncItemDto> items, CancellationToken ct = default);
}
