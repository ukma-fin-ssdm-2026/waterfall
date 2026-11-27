// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.Interfaces;

/// <summary>
/// Сервіс двосторонньої синхронізації бази вакансій між локальною SQLite БД клієнта та сервером Waterfall API.
/// Коли клієнт зібрав або відредагував вакансії офлайн, а потім підключився до сервера,
/// сервіс обмінюється всіма зібраними окремо даними в обидва боки.
/// </summary>
public interface IVacancySyncService
{
    /// <summary>Чи триває синхронізація просто зараз.</summary>
    bool IsSyncing { get; }

    /// <summary>
    /// Чи сервер приймає локальні вакансії (VacancySync:AcceptClientVacancies), за останньою відповіддю сервера.
    /// null — ще невідомо: жодна синхронізація ще не дійшла до сервера.
    /// </summary>
    bool? ClientVacanciesAccepted { get; }

    /// <summary>
    /// Виконує двосторонню синхронізацію:
    /// 1) вивантажує всі несинхронізовані локальні вакансії клієнта на сервер (пакетами до 500 записів),
    ///    якщо сервер їх приймає (VacancySync:AcceptClientVacancies);
    /// 2) завантажує всі нові/оновлені вакансії із сервера та об'єднує їх із локальною БД.
    /// </summary>
    Task<VacancySyncResult> SyncWithServerAsync(bool fullSync = false, CancellationToken ct = default);
}
