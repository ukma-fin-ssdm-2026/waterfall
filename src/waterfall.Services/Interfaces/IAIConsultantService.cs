// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Services.Models.Ai;

namespace Waterfall.Services.Interfaces;

/// <summary>
/// Сервіс ШІ-консультанта (FR-002, FR-009): перетворює репліку природною мовою на структурований
/// фільтр каталогу, шукає вакансії в БД і формує текстову відповідь через LLM.
/// </summary>
public interface IAIConsultantService
{
    /// <summary>
    /// Підготувати репліку: перевірити власника діалогу, витягти фільтр із запиту (з урахуванням
    /// історії), зберегти повідомлення користувача, знайти вакансії (за потреби — з послабленням
    /// критеріїв) і скласти промпт для відповіді.
    /// </summary>
    /// <param name="userId">Власник діалогу (завжди з JWT, THR-12).</param>
    /// <param name="sessionId">Ідентифікатор діалогу або null для нового діалогу.</param>
    /// <param name="text">Текст запиту користувача (1..1000 символів).</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <exception cref="Exceptions.ChatSessionNotFoundException">Діалогу немає або він чужий (404).</exception>
    /// <remarks>Якщо LLM недоступна під час витягу фільтра, сервіс кидає <c>LlmUnavailableException</c> (503, REL-003).</remarks>
    Task<AIConsultantTurn> PrepareAsync(Guid userId, Guid? sessionId, string text, CancellationToken ct = default);

    /// <summary>
    /// Стрімити текст відповіді LLM і зберегти повне повідомлення разом із посиланнями на
    /// показані вакансії. Останній елемент потоку — <see cref="AIConsultantCompleted"/>.
    /// </summary>
    /// <param name="turn">Результат <see cref="PrepareAsync"/>.</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <remarks>Якщо провайдер відмовив під час стрімінгу, потік кидає <c>LlmUnavailableException</c> (подія error).</remarks>
    IAsyncEnumerable<AIConsultantStreamItem> StreamAnswerAsync(AIConsultantTurn turn, CancellationToken ct = default);

    /// <summary>
    /// Видалити діалог користувача («очистити чат»).
    /// </summary>
    /// <param name="sessionId">Ідентифікатор діалогу.</param>
    /// <param name="userId">Власник діалогу (завжди з JWT, THR-12).</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <returns>false, якщо діалогу немає або він чужий (404).</returns>
    Task<bool> DeleteSessionAsync(Guid sessionId, Guid userId, CancellationToken ct = default);
}
