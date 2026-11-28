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

    /// <summary>
    /// Двостороння синхронізація бази вакансій між клієнтом та сервером:
    /// приймає офлайн-зібрані або змінені клієнтом вакансії, об'єднує їх із БД сервера
    /// та повертає актуальні серверні вакансії.
    /// </summary>
    Task<VacancySyncResponseDto> SyncVacanciesAsync(VacancySyncRequestDto request, CancellationToken ct = default);
}
