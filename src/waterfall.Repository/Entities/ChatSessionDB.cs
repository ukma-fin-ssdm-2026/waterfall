// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Waterfall.Repository.Entities;

/// <summary>
/// Сутність діалогу з ШІ-консультантом (FR-002, FR-009).
/// Власник діалогу зберігається явно, щоб усі запити можна було обмежити одним користувачем (THR-12).
/// </summary>
[Table("ChatSessions")]
[Index(nameof(UserId))]
public class ChatSessionDB
{
    /// <summary>
    /// Первинний ключ діалогу (uuid — id потрапляє в API, data-model.md §1).
    /// </summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Власник діалогу (FK на Users). Береться лише з JWT, ніколи з тіла запиту (THR-12).
    /// </summary>
    [Required]
    public Guid UserId { get; set; }

    /// <summary>
    /// Поточний структурований фільтр діалогу в JSON (snake_case, той самий формат, що й у SSE-події meta).
    /// Уточнення на кшталт «а тепер тільки від 1000$» змінюють його, а не запускають пошук заново (FR-009, US-005).
    /// </summary>
    [Required]
    public string ContextFilterJson { get; set; } = "{}";

    /// <summary>
    /// Момент створення діалогу (UTC).
    /// </summary>
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Момент останньої активності в діалозі (UTC).
    /// </summary>
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Навігаційна властивість на власника діалогу.
    /// </summary>
    [ForeignKey(nameof(UserId))]
    public UserDB User { get; set; } = null!;

    /// <summary>
    /// Повідомлення діалогу (видаляються разом із діалогом).
    /// </summary>
    public ICollection<ChatMessageDB> Messages { get; set; } = new List<ChatMessageDB>();
}
