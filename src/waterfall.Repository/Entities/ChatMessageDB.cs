// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Waterfall.Repository.Entities;

/// <summary>
/// Автор репліки в діалозі з ШІ-консультантом (data-model.md, розділ «ШІ-консультант»).
/// </summary>
public enum ChatMessageAuthor
{
    /// <summary>
    /// Повідомлення шукача роботи.
    /// </summary>
    User,

    /// <summary>
    /// Відповідь ШІ-консультанта.
    /// </summary>
    Assistant
}

/// <summary>
/// Сутність репліки діалогу з ШІ-консультантом (FR-009).
/// Історія потрібна, щоб уточнення враховували попередній контекст (US-005, критерій 2).
/// </summary>
[Table("ChatMessages")]
[Index(nameof(SessionId))]
public class ChatMessageDB
{
    /// <summary>
    /// Первинний ключ (long — id не є публічним ресурсом, data-model.md §1).
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>
    /// Діалог, до якого належить репліка.
    /// </summary>
    [Required]
    public Guid SessionId { get; set; }

    /// <summary>
    /// Автор репліки (User або Assistant).
    /// </summary>
    [Required]
    public ChatMessageAuthor Author { get; set; }

    /// <summary>
    /// Текст репліки (без маскування — маскування виконується лише перед відправкою до LLM, THR-06).
    /// </summary>
    [Required]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Момент створення репліки (UTC).
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Навігаційна властивість на діалог.
    /// </summary>
    [ForeignKey(nameof(SessionId))]
    public ChatSessionDB Session { get; set; } = null!;

    /// <summary>
    /// Вакансії, згадані у відповіді ШІ (FUNC-001).
    /// </summary>
    public ICollection<ChatMessageVacancyDB> Vacancies { get; set; } = new List<ChatMessageVacancyDB>();
}
