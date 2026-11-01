// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Models.Ai;

/// <summary>
/// Елемент потоку відповіді ШІ-консультанта (те, що контролер перетворює на SSE-події
/// <c>delta</c> та <c>done</c>).
/// </summary>
public abstract record AIConsultantStreamItem;

/// <summary>
/// Чергова порція тексту відповіді.
/// </summary>
/// <param name="Text">Текст порції (ніколи не порожній).</param>
public sealed record AIConsultantDelta(string Text) : AIConsultantStreamItem;

/// <summary>
/// Відповідь повністю сформована й збережена; містить id повідомлення для події <c>done</c>.
/// </summary>
/// <param name="MessageId">Ідентифікатор збереженого повідомлення асистента.</param>
public sealed record AIConsultantCompleted(long MessageId) : AIConsultantStreamItem;
