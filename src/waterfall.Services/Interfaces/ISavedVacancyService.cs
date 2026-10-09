// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Services.Models.SavedVacancies;

namespace Waterfall.Services.Interfaces;

/// <summary>
/// Інтерфейс сервісу бізнес-логіки збережених вакансій («лайків» / закладок).
/// Працює з DTO моделями, координує оновлення даних та інвалідацію кешу.
/// </summary>
public interface ISavedVacancyService
{
    Task<IReadOnlyList<SavedVacancyDto>> GetAllAsync(bool onlyActive = true, CancellationToken ct = default);
    Task<SavedVacancyDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SavedVacancyDto> SaveAsync(SaveVacancyRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
