// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Models.Vacancies;

/// <summary>
/// Джерело походження вакансії (платформа агрегації).
/// </summary>
public enum JobSource
{
    /// <summary>
    /// Невідоме джерело або резервне значення за замовчуванням.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Портал пошуку роботи Work.ua.
    /// </summary>
    WorkUa = 1,

    /// <summary>
    /// Портал пошуку роботи Robota.ua.
    /// </summary>
    RobotaUa = 2,

    /// <summary>
    /// Сервіс анонімного пошуку роботи Djinni.co.
    /// </summary>
    Djinni = 3,

    /// <summary>
    /// Спільнота програмістів та вакансії DOU.ua.
    /// </summary>
    Dou = 4
}
