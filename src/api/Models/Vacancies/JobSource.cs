// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json.Serialization;

namespace Waterfall.Api.Models.Vacancies;
/// <summary>
/// Джерело, з якого агреговано або розміщено вакансію.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JobSource
{
    /// <summary>
    /// Портал Work.ua
    /// </summary>
    WorkUa = 1,

    /// <summary>
    /// Портал Robota.ua
    /// </summary>
    RobotaUa = 2,

    /// <summary>
    /// Анонімний пошук роботи Djinni.co
    /// </summary>
    Djinni = 3,

    /// <summary>
    /// Спільнота розробників DOU.ua
    /// </summary>
    Dou = 4
}
