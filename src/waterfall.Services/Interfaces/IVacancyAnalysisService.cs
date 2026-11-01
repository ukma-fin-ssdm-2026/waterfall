// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Services.Models.Ai;

namespace Waterfall.Services.Interfaces;

/// <summary>
/// Сервіс аналізу вакансій (FR-010, US-005): будує профіль переваг зі збережених вакансій
/// користувача, детерміновано оцінює релевантність вибраних вакансій і готує промпт, за яким
/// LLM (<c>LlmRole.Analyze</c>) дає розбір вимог, червоних прапорців і відповідності профілю.
/// </summary>
public interface IVacancyAnalysisService
{
    /// <summary>
    /// Підготувати аналіз: перевірити, що всі вакансії існують, побудувати профіль користувача,
    /// оцінити релевантність кожної вакансії та скласти промпт для LLM.
    /// </summary>
    /// <param name="userId">Власник збережених вакансій (завжди з JWT, THR-12).</param>
    /// <param name="vacancyIds">Від 1 до 3 ідентифікаторів вакансій каталогу.</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <exception cref="ArgumentException">Порожній список або більше трьох вакансій (400).</exception>
    /// <exception cref="Exceptions.VacancyNotFoundException">Хоч однієї вакансії немає в каталозі (404).</exception>
    Task<AIAnalysisTurn> PrepareAsync(Guid userId, IReadOnlyList<Guid> vacancyIds, CancellationToken ct = default);

    /// <summary>
    /// Стрімити текст аналізу від LLM. Останній елемент потоку — <see cref="AIAnalysisCompleted"/>.
    /// </summary>
    /// <param name="turn">Результат <see cref="PrepareAsync"/>.</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <remarks>Якщо провайдер відмовив, потік кидає <c>LlmUnavailableException</c> (подія error).</remarks>
    IAsyncEnumerable<AIAnalysisStreamItem> StreamAnalysisAsync(AIAnalysisTurn turn, CancellationToken ct = default);
}
