// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Entities;

namespace Waterfall.Repository.Interfaces;

/// <summary>
/// Інтерфейс репозиторію діалогів з ШІ-консультантом на рівні БД (FR-002, FR-009).
/// Усі операції, крім створення, вимагають ідентифікатор власника: чужий діалог
/// для репозиторію не існує (THR-12, той самий підхід, що й у <see cref="ISavedVacancyRepository"/>).
/// </summary>
public interface IChatSessionRepository
{
    /// <summary>
    /// Знайти діалог за id у межах власника.
    /// </summary>
    /// <param name="sessionId">Ідентифікатор діалогу.</param>
    /// <param name="userId">Власник діалогу (береться з JWT).</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <returns>Діалог або null, якщо його немає або він належить іншому користувачу.</returns>
    Task<ChatSessionDB?> GetSessionAsync(Guid sessionId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Створити новий порожній діалог із початковим фільтром.
    /// </summary>
    /// <param name="userId">Власник діалогу (береться з JWT).</param>
    /// <param name="contextFilterJson">Початковий фільтр діалогу в JSON.</param>
    /// <param name="ct">Токен скасування операції.</param>
    Task<ChatSessionDB> CreateSessionAsync(Guid userId, string contextFilterJson, CancellationToken ct = default);

    /// <summary>
    /// Отримати останні повідомлення діалогу в хронологічному порядку (від старіших до новіших).
    /// </summary>
    /// <param name="sessionId">Ідентифікатор діалогу.</param>
    /// <param name="userId">Власник діалогу (береться з JWT).</param>
    /// <param name="limit">Максимальна кількість повідомлень (історія для LLM).</param>
    /// <param name="ct">Токен скасування операції.</param>
    Task<IReadOnlyList<ChatMessageDB>> GetRecentMessagesAsync(Guid sessionId, Guid userId, int limit, CancellationToken ct = default);

    /// <summary>
    /// Додати повідомлення шукача роботи та оновити час останньої активності діалогу.
    /// </summary>
    /// <param name="sessionId">Ідентифікатор діалогу.</param>
    /// <param name="userId">Власник діалогу (береться з JWT).</param>
    /// <param name="content">Текст повідомлення (як ввів користувач, без маскування).</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <returns>Додане повідомлення або null, якщо діалог недоступний цьому користувачу.</returns>
    Task<ChatMessageDB?> AddUserMessageAsync(Guid sessionId, Guid userId, string content, CancellationToken ct = default);

    /// <summary>
    /// Додати відповідь ШІ разом із посиланнями на показані вакансії (FUNC-001) та оновити
    /// час останньої активності діалогу. Повідомлення і зв'язки зберігаються в одній транзакції.
    /// </summary>
    /// <param name="sessionId">Ідентифікатор діалогу.</param>
    /// <param name="userId">Власник діалогу (береться з JWT).</param>
    /// <param name="content">Повний текст відповіді.</param>
    /// <param name="vacancyIds">Вакансії, надіслані користувачу в події vacancies (може бути порожнім).</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <returns>Ідентифікатор збереженого повідомлення або null, якщо діалог уже недоступний.</returns>
    Task<long?> AddAssistantMessageAsync(Guid sessionId, Guid userId, string content, IReadOnlyList<Guid> vacancyIds, CancellationToken ct = default);

    /// <summary>
    /// Зберегти поточний фільтр діалогу та оновити час останньої активності.
    /// </summary>
    /// <param name="sessionId">Ідентифікатор діалогу.</param>
    /// <param name="userId">Власник діалогу (береться з JWT).</param>
    /// <param name="contextFilterJson">Новий фільтр діалогу в JSON.</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <returns>false, якщо діалог недоступний цьому користувачу.</returns>
    Task<bool> UpdateContextFilterAsync(Guid sessionId, Guid userId, string contextFilterJson, CancellationToken ct = default);

    /// <summary>
    /// Видалити діалог разом з усіма повідомленнями та зв'язками з вакансіями.
    /// </summary>
    /// <param name="sessionId">Ідентифікатор діалогу.</param>
    /// <param name="userId">Власник діалогу (береться з JWT).</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <returns>true, якщо діалог знайдено й видалено.</returns>
    Task<bool> DeleteSessionAsync(Guid sessionId, Guid userId, CancellationToken ct = default);
}
