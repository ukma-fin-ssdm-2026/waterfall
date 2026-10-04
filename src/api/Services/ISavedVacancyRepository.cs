// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Api.Models.SavedVacancies;

namespace Waterfall.Api.Services;

public interface ISavedVacancyRepository
{
    Task<IReadOnlyList<SavedVacancyDto>> GetAllAsync(CancellationToken ct = default);
    Task<SavedVacancyDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SavedVacancyDto> SaveAsync(SaveVacancyRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsForVacancyAsync(Guid vacancyId, CancellationToken ct = default);
}
