// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Waterfall.Repository.Entities;

/// <summary>
/// Постійний 3-годинний кеш результатів пошуку на сайтах (L2 у БД).
/// Зберігає знайдені ExternalId для ключа запиту, щоб кеш переживав перезапуск API.
/// </summary>
[Table("SiteSearchCache")]
[Index(nameof(CacheKey), IsUnique = true)]
public class SiteSearchCacheDB
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(500)]
    public string CacheKey { get; set; } = string.Empty;

    [Required]
    public string ExternalIdsJson { get; set; } = "[]";

    public DateTimeOffset CachedAt { get; set; } = DateTimeOffset.UtcNow;
}
