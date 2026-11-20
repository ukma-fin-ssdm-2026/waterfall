// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Client.Entities;
using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.Interfaces;

/// <summary>
/// Локальне SQLite сховище вакансій клієнта (ClientWaterfallDbContext).
/// Забезпечує офлайн-роботу, зберігання офлайн-зібраних вакансій та локальних змін категорій
/// із подальшою двосторонньою синхронізацією із сервером.
/// </summary>
public interface ILocalVacancyStore
{
    /// <summary>Повертає всі вакансії з локальної бази даних клієнта.</summary>
    Task<IReadOnlyList<VacancyDto>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Зберігає або оновлює вакансії у локальній БД.
    /// <paramref name="isSynced"/> = false позначає записи як локальні/офлайн, які потрібно вивантажити на сервер.
    /// </summary>
    Task<int> SaveVacanciesAsync(IEnumerable<VacancyDto> vacancies, bool isSynced, CancellationToken ct = default);

    /// <summary>Повертає всі несинхронізовані (офлайн зібрані або змінені) вакансії.</summary>
    Task<IReadOnlyList<ClientVacancyDB>> GetUnsyncedAsync(CancellationToken ct = default);

    /// <summary>Позначає вказані вакансії як успішно вивантажені на сервер (IsSynced = true).</summary>
    Task MarkAsSyncedAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

    /// <summary>Об'єднує отримані з сервера вакансії з локальною SQLite БД (не затираючи новіші офлайн-правки).</summary>
    Task<(int Added, int Updated)> UpsertFromServerAsync(IEnumerable<VacancySyncItemDto> serverItems, CancellationToken ct = default);

    /// <summary>Отримує мітку часу останньої успішної синхронізації вакансій (UTC).</summary>
    Task<DateTimeOffset?> GetLastSyncedAtAsync(CancellationToken ct = default);

    /// <summary>Зберігає мітку часу та короткий опис останньої синхронізації.</summary>
    Task SetLastSyncedAtAsync(DateTimeOffset syncedAt, string? summary = null, CancellationToken ct = default);

    /// <summary>Повертає зведену статистику локальної бази вакансій (усього, несинхронізовано, час синхронізації).</summary>
    Task<LocalVacancyStats> GetStatsAsync(CancellationToken ct = default);
}
