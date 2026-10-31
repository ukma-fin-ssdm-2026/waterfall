// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Waterfall.Repository.Entities;

/// <summary>
/// Перегляд вакансії — історія для особистої статистики (FR-007) та рекомендацій (FR-006, data-model.md §3).
/// Власний ключ, а не складений (user_id, vacancy_id): одну вакансію переглядають кілька разів.
/// Поведінкові ПД: читає лише власник (THR-12).
/// </summary>
[Table("VacancyViews")]
[Index(nameof(UserId), nameof(VacancyId))]
[Index(nameof(UserId), nameof(ViewedAt))]
public class VacancyViewDB
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid UserId { get; set; }

    [Required]
    public Guid VacancyId { get; set; }

    public DateTimeOffset ViewedAt { get; set; } = DateTimeOffset.UtcNow;

    [ForeignKey(nameof(UserId))]
    public UserDB User { get; set; } = null!;

    [ForeignKey(nameof(VacancyId))]
    public VacancyDB Vacancy { get; set; } = null!;
}
