// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Waterfall.Repository.Entities;

/// <summary>
/// Персональні дані користувача (домашня адреса, локаційні налаштування), пов'язані з обліковим записом Users.
/// </summary>
[Table("UserPersonalData")]
[Index(nameof(UserId), IsUnique = true)]
public class UserPersonalDataDB
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid UserId { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
