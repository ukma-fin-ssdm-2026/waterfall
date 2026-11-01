// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Models.Profile;

/// <summary>
/// Результат реєстрації події активності (пошук, перегляд) для особистої статистики (FR-007).
/// </summary>
public enum UserStatsRecordResult
{
    /// <summary>Подію записано.</summary>
    Recorded,

    /// <summary>Подію пропущено: порожній запит, повтор попереднього запиту або перегляд тієї ж вакансії протягом короткого інтервалу.</summary>
    Skipped,

    /// <summary>Записування подій доступне лише користувачам із роллю JobSeeker (data-model.md §7, інваріант 2).</summary>
    Forbidden
}
