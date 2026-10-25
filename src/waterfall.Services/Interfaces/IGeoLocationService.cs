// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Interfaces;

/// <summary>
/// Сервіс геокодування адрес із підтримкою постійного кешування у базі даних (GeoLocationCache).
/// </summary>
public interface IGeoLocationService
{
    /// <summary>
    /// Отримує координати (широту й довготу) для вказаної адреси.
    /// Спочатку перевіряє таблицю GeoLocationCache у БД; якщо запису немає —
    /// звертається до публічного сервісу Wikimedia, зберігає отримані координати в БД і повертає їх.
    /// </summary>
    Task<(double Latitude, double Longitude)?> GetCoordinatesAsync(string address, CancellationToken ct = default);

    /// <summary>
    /// Двостороння синхронізація геокешу: зберігає координати з клієнта в БД сервера та повертає всі серверні записи.
    /// </summary>
    Task<Waterfall.Services.Models.Geo.GeoSyncResponseDto> SyncCacheAsync(Waterfall.Services.Models.Geo.GeoSyncRequestDto request, CancellationToken ct = default);
}
