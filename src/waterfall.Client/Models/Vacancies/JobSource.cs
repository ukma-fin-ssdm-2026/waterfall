// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Client.Models.Vacancies;

/// <summary>
/// Джерело вакансії. Числові значення мусять збігатися з серверним JobSource (передається числом).
/// </summary>
public enum JobSource
{
    Unknown = 0,
    WorkUa = 1,
    RobotaUa = 2,
    Djinni = 3,
    Dou = 4
}
