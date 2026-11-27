// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services.Exceptions;
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
        ArgumentNullException.ThrowIfNull(request);
        if (request.VacancyId == Guid.Empty)
        {
            throw new ArgumentException("Ідентифікатор вакансії не може бути порожнім (Guid.Empty).", nameof(request));
        }
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Ідентифікатор користувача не може бути порожнім (Guid.Empty).", nameof(userId));
        }

        SavedVacancyDB entity = new SavedVacancyDB
        {
            VacancyId = request.VacancyId,
            UserId = userId,
            Notes = request.Notes?.Trim(),
            SavedAt = DateTimeOffset.UtcNow
        };

        SavedVacancyDB saved;
        try
        {
            saved = await _savedVacancyRepository.SaveAsync(entity, ct);
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyViolation(ex))
        {
            throw new DuplicateSavedVacancyException(request.VacancyId, ex);
        }

        await _cacheService.InvalidateVacancyAsync(request.VacancyId, ct);

        _logger.LogInformation("Вакансію {VacancyId} успішно збережено у закладки", request.VacancyId);
        return MapToDto(saved);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Ідентифікатор закладки не може бути порожнім.", nameof(id));
        }
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Ідентифікатор користувача не може бути порожнім.", nameof(userId));
        }

        bool deleted = await _savedVacancyRepository.DeleteAsync(id, userId, ct);

        if (deleted)
        {
            await _cacheService.InvalidateVacancyAsync(id, ct);

            _logger.LogInformation("Закладку для вакансії {VacancyId} видалено", id);
        }

        return deleted;
    }

    private static SavedVacancyDto MapToDto(SavedVacancyDB entity)
    {
        return new SavedVacancyDto
        {
            Id = entity.VacancyId,
            VacancyId = entity.VacancyId,
            Vacancy = entity.Vacancy != null ? VacancyService.MapToDto(entity.Vacancy, isSaved: true) : null,
            Notes = entity.Notes,
            SavedAt = entity.SavedAt
        };
    }

    private static bool IsDuplicateKeyViolation(DbUpdateException ex)
    {
        for (Exception? current = ex.InnerException; current is not null; current = current.InnerException)
        {
            if (IsProviderDuplicateKey(current) || HasDuplicateKeyMessage(current.Message))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsProviderDuplicateKey(Exception exception)
    {
        string typeName = exception.GetType().Name;
        if (string.Equals(typeName, "SqliteException", StringComparison.Ordinal))
        {
            int? extendedCode = GetIntProperty(exception, "SqliteExtendedErrorCode");
            if (extendedCode is 1555 or 2067)
            {
                return true;
            }

            int? errorCode = GetIntProperty(exception, "SqliteErrorCode");
            return errorCode == 19
                && exception.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(typeName, "SqlException", StringComparison.Ordinal))
        {
            int? number = GetIntProperty(exception, "Number");
            return number is 2601 or 2627;
        }

        return false;
    }

    private static bool HasDuplicateKeyMessage(string message) =>
        message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Violation of PRIMARY KEY constraint", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Violation of UNIQUE KEY constraint", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Cannot insert duplicate key", StringComparison.OrdinalIgnoreCase);

    private static int? GetIntProperty(Exception exception, string propertyName) =>
        exception.GetType().GetProperty(propertyName)?.GetValue(exception) as int?;
}
