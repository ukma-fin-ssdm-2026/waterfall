// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Waterfall.Repository.Entities;

/// <summary>
/// Сутність користувача для збереження в реляційній базі даних (SQLite / MSSQL) через EF Core.
/// Пароль зберігається лише як хеш (SEC-001); email унікальний (data-model.md §6).
/// </summary>
[Table("Users")]
[Index(nameof(Email), IsUnique = true)]
public class UserDB
{
    /// <summary>
    /// Первинний ключ (uuid; UUID для сутностей, що потрапляють в API, data-model.md §1).
    /// </summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Електронна пошта користувача (унікальна, використовується як логін; ПД, data-model.md §5).
    /// </summary>
    [Required]
    [MaxLength(80)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Хеш пароля (лише хеш, ніколи plaintext; ніколи не повертається з API, SEC-001).
    /// </summary>
    [Required]
    [MaxLength(1000)]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Роль користувача (JobSeeker або Analyst, FR-008).
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Role { get; set; } = "JobSeeker";

    /// <summary>
    /// Момент створення облікового запису (UTC).
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
