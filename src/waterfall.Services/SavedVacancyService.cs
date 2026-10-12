// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.Extensions.Logging;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.SavedVacancies;

namespace Waterfall.Services;

/// <summary>
/// Сервіс бізнес-логіки збережених вакансій («лайків» / закладок).
/// Забезпечує мапінг сутностей SavedVacancyDB у SavedVacancyDto
/// та автоматичну інвалідацію кешу при додаванні чи видаленні закладок.
/// </summary>
public class SavedVacancyService : ISavedVacancyService
{
    private readonly ISavedVacancyRepository _savedVacancyRepository;
    private readonly IVacancyCacheService _cacheService;
    private readonly ILogger<SavedVacancyService> _logger;

    public SavedVacancyService(
        ISavedVacancyRepository savedVacancyRepository,
        IVacancyCacheService cacheService,
        ILogger<SavedVacancyService> logger)
    {
        _savedVacancyRepository = savedVacancyRepository;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SavedVacancyDto>> GetAllAsync(Guid userId, bool onlyActive = true, CancellationToken ct = default)
    {
        IReadOnlyList<SavedVacancyDB> entities = await _savedVacancyRepository.GetAllAsync(userId, onlyActive, ct);
        return entities.Select(MapToDto).ToList();
    }

    public async Task<SavedVacancyDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        SavedVacancyDB? entity = await _savedVacancyRepository.GetByIdAsync(id, userId, ct);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<SavedVacancyDto> SaveAsync(SaveVacancyRequest request, Guid userId, CancellationToken ct = default)
    {
        SavedVacancyDB entity = new SavedVacancyDB
        {
            Id = Guid.NewGuid(),
            VacancyId = request.VacancyId,
            UserId = userId,
            Notes = request.Notes?.Trim(),
            SavedAt = DateTimeOffset.UtcNow
        };

        SavedVacancyDB saved = await _savedVacancyRepository.SaveAsync(entity, ct);

        await _cacheService.InvalidateVacancyAsync(request.VacancyId, ct);

        _logger.LogInformation("Вакансію {VacancyId} успішно збережено у закладки", request.VacancyId);
        return MapToDto(saved);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        SavedVacancyDB? existing = await _savedVacancyRepository.GetByIdAsync(id, userId, ct);
        bool deleted = await _savedVacancyRepository.DeleteAsync(id, userId, ct);

        if (deleted && existing != null)
        {
            await _cacheService.InvalidateVacancyAsync(existing.VacancyId, ct);

            _logger.LogInformation("Закладку для вакансії {VacancyId} видалено", existing.VacancyId);
        }

        return deleted;
    }

    private static SavedVacancyDto MapToDto(SavedVacancyDB entity)
    {
        return new SavedVacancyDto
        {
            Id = entity.Id,
            VacancyId = entity.VacancyId,
            Vacancy = entity.Vacancy != null ? VacancyService.MapToDto(entity.Vacancy, isSaved: true) : null,
            Notes = entity.Notes,
            SavedAt = entity.SavedAt
        };
    }
}
