// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Models.Common;
using Waterfall.Repository.Models.Vacancies;

namespace Waterfall.Repository.Interfaces;

/// <summary>
/// Інтерфейс репозиторію вакансій платформи Waterfall.
/// </summary>
public interface IVacancyRepository
{
    Task<PagedResult<VacancyDto>> GetAllAsync(VacancyFilterParameters filter, CancellationToken ct = default);
    Task<VacancyDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<VacancyDto> CreateAsync(CreateVacancyRequest request, CancellationToken ct = default);
    Task<VacancyDto?> UpdateAsync(Guid id, UpdateVacancyRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
