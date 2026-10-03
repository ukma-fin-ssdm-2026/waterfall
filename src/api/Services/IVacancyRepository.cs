// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Api.Models.Common;
using Waterfall.Api.Models.Vacancies;

namespace Waterfall.Api.Services;

public interface IVacancyRepository
{
    Task<PagedResult<VacancyDto>> GetAllAsync(VacancyFilterParameters filter, CancellationToken ct = default);
    Task<VacancyDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<VacancyDto> CreateAsync(CreateVacancyRequest request, CancellationToken ct = default);
    Task<VacancyDto?> UpdateAsync(Guid id, UpdateVacancyRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
