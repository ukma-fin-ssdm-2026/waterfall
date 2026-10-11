// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Services.Models.Common;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Interfaces;

/// <summary>
/// Інтерфейс сервісу бізнес-логіки управління вакансіями (Core Services Layer).
/// Працює з DTO моделями, оркеструє Cache-Aside кешування та взаємодіє з репозиторієм БД.
/// </summary>
public interface IVacancyService
{
    Task<PagedResult<VacancyDto>> GetAllAsync(VacancyFilterParameters filter, CancellationToken ct = default, Guid? userId = null);
    Task<VacancyDto?> GetByIdAsync(Guid id, CancellationToken ct = default, Guid? userId = null);
    Task<VacancyDto> CreateAsync(CreateVacancyRequest request, CancellationToken ct = default);
    Task<VacancyDto?> UpdateAsync(Guid id, UpdateVacancyRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
