// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Entities;

namespace Waterfall.Client.Entities;

/// <summary>
/// Сутність кешу географічних координат адреси в локальній базі клієнта.
/// Успадковує всі поля серверної сутності GeoLocationCacheDB і додає прапорець IsSynced.
/// </summary>
[Table("GeoLocationCache")]
[Index(nameof(NormalizedAddress), IsUnique = true)]
[Index(nameof(IsSynced))]
public class ClientGeoLocationCacheDB : GeoLocationCacheDB
{
    /// <summary>
    /// Прапорець синхронізації з сервером (true — синхронізовано з сервером, false — знайдено локально/офлайн).
    /// </summary>
    public bool IsSynced { get; set; } = true;
}
