// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Exceptions;

/// <summary>
/// Вакансію вже збережено цим користувачем (порушення складеного PK (UserId, VacancyId); контролер мапить на 409 Conflict).
/// </summary>
public sealed class DuplicateSavedVacancyException : Exception
{
    public DuplicateSavedVacancyException(Guid vacancyId)
        : base($"Вакансію з ID '{vacancyId}' уже додано до збережених.")
    {
        VacancyId = vacancyId;
    }

    public DuplicateSavedVacancyException(Guid vacancyId, Exception inner)
        : base($"Вакансію з ID '{vacancyId}' уже додано до збережених.", inner)
    {
        VacancyId = vacancyId;
    }

    /// <summary>
    /// Ідентифікатор вакансії, яку намагалися зберегти повторно.
    /// </summary>
    public Guid VacancyId { get; }
}
