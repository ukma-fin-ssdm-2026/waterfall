// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Waterfall.Repository.Entities;

/// <summary>
/// Пошуковий запит користувача — історія для особистої статистики (FR-007, data-model.md §3).
/// Поведінкові ПД: читає лише власник (THR-12); текст запиту не пишеться в логи (THR-08).
/// </summary>
[Table("SearchQueries")]
[Index(nameof(UserId), nameof(SearchedAt))]
public class SearchQueryDB
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid UserId { get; set; }

    [Required]
    [MaxLength(300)]
    public string QueryText { get; set; } = string.Empty;

    public DateTimeOffset SearchedAt { get; set; } = DateTimeOffset.UtcNow;

    [ForeignKey(nameof(UserId))]
    public UserDB User { get; set; } = null!;
}
