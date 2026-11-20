// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Waterfall.Client.Entities;

/// <summary>
/// Метадані синхронізації локальної бази клієнта із сервером (зберігається в SQLite між перезапусками).
/// </summary>
[Table("SyncState")]
public class ClientSyncStateDB
{
    [Key]
    public string Key { get; set; } = string.Empty;

    public DateTimeOffset? LastSyncedAt { get; set; }

    public string? LastSummary { get; set; }
}
