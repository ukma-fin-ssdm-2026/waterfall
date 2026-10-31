// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Services.Models.Profile;

namespace Waterfall.Services.Interfaces;

/// <summary>
/// Сервіс особистої статистики активності користувача (FR-007, US-008).
/// Реалізує частину «статистика активності користувачів» компонента
/// «Analytics &amp; Statistics Service» (high-level-design.md §3.4); ринкова аналітика — окремо.
/// Поведінкові ПД: читання і запис — лише для userId власника (THR-12),
/// текст пошукових запитів у логи не потрапляє (THR-08).
/// </summary>
public interface IUserStatsService
{
    /// <summary>
    /// Агрегована статистика користувача: кількість збережених вакансій, пошуків,
    /// топ технологій/категорій за переглядами та збереженнями, останні запити.
    /// </summary>
    Task<UserStatsDto> GetStatsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Записати виконаний пошук в історію. Повертає Skipped, якщо запит порожній або
    /// повторює попередній записаний запит; Forbidden — якщо роль не JobSeeker.
    /// </summary>
    Task<UserStatsRecordResult> RecordSearchAsync(Guid userId, string query, CancellationToken ct = default);

    /// <summary>
    /// Записати перегляд вакансії. Існування вакансії перевіряє контролер.
    /// </summary>
    Task<UserStatsRecordResult> RecordVacancyViewAsync(Guid userId, Guid vacancyId, CancellationToken ct = default);

    /// <summary>
    /// Очистити історію пошукових запитів користувача. Повертає true, якщо видалено хоча б один запис.
    /// </summary>
    Task<bool> ClearSearchHistoryAsync(Guid userId, CancellationToken ct = default);
}
