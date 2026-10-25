// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Entities;

namespace Waterfall.Repository.Interfaces;

/// <summary>
/// Репозиторій для роботи з постійним кешем географічних координат у базі даних (GeoLocationCache).
/// </summary>
public interface IGeoLocationCacheRepository
{
    /// <summary>
    /// Пошук збережених координат за адресою (з урахуванням нормалізації та нечутливості до регістру).
    /// </summary>
    Task<GeoLocationCacheDB?> GetByAddressAsync(string address, CancellationToken ct = default);

    /// <summary>
    /// Збереження або оновлення координат адреси в базі даних.
    /// </summary>
    Task<GeoLocationCacheDB> SetCoordinatesAsync(string address, double latitude, double longitude, CancellationToken ct = default);

    /// <summary>
    /// Отримання всіх збережених записів кешу геокоординат.
    /// </summary>
    Task<IReadOnlyList<GeoLocationCacheDB>> GetAllCachedAsync(CancellationToken ct = default);

    /// <summary>
    /// Масове збереження або оновлення записів координат через Entity Framework Core.
    /// </summary>
    Task UpsertRangeAsync(IEnumerable<GeoLocationCacheDB> items, CancellationToken ct = default);
}
