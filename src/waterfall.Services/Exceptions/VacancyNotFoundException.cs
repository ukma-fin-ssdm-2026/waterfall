// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Exceptions;

/// <summary>
/// Вакансію з каталогу не знайдено. Контролер аналізу перетворює це на 404: клієнт не повинен
/// отримувати частковий аналіз за неіснуючим ідентифікатором.
/// </summary>
public sealed class VacancyNotFoundException : Exception
{
    public VacancyNotFoundException(Guid vacancyId)
        : base($"Вакансію з ID '{vacancyId}' не знайдено.")
    {
        VacancyId = vacancyId;
    }

    /// <summary>
    /// Ідентифікатор ненайденої вакансії (для журналу; у відповідь клієнту не потрапляє).
    /// </summary>
    public Guid VacancyId { get; }
}
