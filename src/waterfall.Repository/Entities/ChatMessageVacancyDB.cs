// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations.Schema;

namespace Waterfall.Repository.Entities;

/// <summary>
/// Зв'язок «відповідь ШІ — вакансія» (data-model.md, розділ «Зв'язки багато до багатьох»).
/// Складений первинний ключ не дає зберегти ту саму вакансію в одній відповіді двічі,
/// а зовнішній ключ на Vacancies робить неможливим посилання на вигадану вакансію (FUNC-001).
/// </summary>
[Table("ChatMessageVacancy")]
public class ChatMessageVacancyDB
{
    /// <summary>
    /// Репліка ШІ, у відповіді якої згадано вакансію.
    /// </summary>
    public long MessageId { get; set; }

    /// <summary>
    /// Вакансія, яку було показано користувачу в цій відповіді.
    /// </summary>
    public Guid VacancyId { get; set; }

    /// <summary>
    /// Навігаційна властивість на репліку.
    /// </summary>
    [ForeignKey(nameof(MessageId))]
    public ChatMessageDB Message { get; set; } = null!;

    /// <summary>
    /// Навігаційна властивість на вакансію.
    /// </summary>
    [ForeignKey(nameof(VacancyId))]
    public VacancyDB Vacancy { get; set; } = null!;
}
