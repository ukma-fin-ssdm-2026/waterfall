// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Waterfall.Repository.Entities;

/// <summary>
/// Сутність кешу географічних координат адрес у базі даних (GeoLocationCache).
/// Дозволяє зберігати координати вулиць та міст назавжди, уникаючи повторних запитів до зовнішніх геосервісів.
/// </summary>
[Table("GeoLocationCache")]
[Index(nameof(NormalizedAddress), IsUnique = true)]
public class GeoLocationCacheDB
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(300)]
    public string NormalizedAddress { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
