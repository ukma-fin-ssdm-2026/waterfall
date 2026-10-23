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
}
