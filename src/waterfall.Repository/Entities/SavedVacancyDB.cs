// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Waterfall.Repository.Entities;

/// <summary>
/// Сутність збереженої вакансії (обране / закладка) шукача роботи в базі даних.
/// </summary>
[Table("SavedVacancies")]
public class SavedVacancyDB
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid VacancyId { get; set; }
    public Guid? UserId { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;

    [ForeignKey(nameof(VacancyId))]
    public VacancyDB Vacancy { get; set; } = null!;
}
