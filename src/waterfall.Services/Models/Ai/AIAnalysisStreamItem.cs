// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Models.Ai;

/// <summary>
/// Елемент потоку аналізу вакансій (FR-010): контролер перетворює його на SSE-події
/// <c>delta</c> та <c>done</c>. Подія <c>relevance</c> формується ще до виклику LLM і тому
/// елементом потоку не є.
/// </summary>
public abstract record AIAnalysisStreamItem;

/// <summary>
/// Чергова порція тексту аналізу.
/// </summary>
/// <param name="Text">Текст порції (ніколи не порожній).</param>
public sealed record AIAnalysisDelta(string Text) : AIAnalysisStreamItem;

/// <summary>
/// Аналіз повністю сформовано; клієнт отримує термінальну подію <c>done</c>.
/// </summary>
public sealed record AIAnalysisCompleted : AIAnalysisStreamItem;
