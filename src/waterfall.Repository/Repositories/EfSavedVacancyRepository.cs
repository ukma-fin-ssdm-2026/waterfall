// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Repository.Models.SavedVacancies;
using Waterfall.Repository.Models.Vacancies;

namespace Waterfall.Repository.Repositories;

/// <summary>
/// Повноцінний репозиторій збережених вакансій на базі Entity Framework Core.
/// </summary>
public class EfSavedVacancyRepository : ISavedVacancyRepository
{
    private readonly WaterfallDbContext _context;

    public EfSavedVacancyRepository(WaterfallDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<SavedVacancyDto>> GetAllAsync(bool onlyActive = true, CancellationToken ct = default)
    {
        IQueryable<SavedVacancyEntity> baseQuery = _context.SavedVacancies
            .AsNoTracking()
            .Include(s => s.Vacancy);

        IQueryable<SavedVacancyEntity> query;
        if (onlyActive)
        {
            query = baseQuery.Where(s => s.Vacancy != null && s.Vacancy.IsActive);
        }
        else
        {
            // Усі активні + не більше 1000 найновіших архівних збережених вакансій за один запит
            IQueryable<SavedVacancyEntity> activeQuery = baseQuery.Where(s => s.Vacancy != null && s.Vacancy.IsActive);
            IQueryable<SavedVacancyEntity> archiveQuery = baseQuery
                .Where(s => s.Vacancy == null || !s.Vacancy.IsActive)
                .OrderByDescending(s => s.SavedAt)
                .Take(1000);

            query = activeQuery.Concat(archiveQuery);
        }

        List<SavedVacancyEntity> entities = await query
            .OrderByDescending(s => s.SavedAt)
            .ToListAsync(ct);

        return entities.Select(MapToDto).ToList();
    }

    public async Task<SavedVacancyDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        SavedVacancyEntity? entity = await _context.SavedVacancies
            .AsNoTracking()
            .Include(s => s.Vacancy)
            .FirstOrDefaultAsync(s => s.Id == id || s.VacancyId == id, ct);

        return entity is null ? null : MapToDto(entity);
    }

    public async Task<SavedVacancyDto> SaveAsync(SaveVacancyRequest request, CancellationToken ct = default)
    {
        Guid id = Guid.NewGuid();
        SavedVacancyEntity entity = new SavedVacancyEntity
        {
            Id = id,
            VacancyId = request.VacancyId,
            Notes = request.Notes?.Trim(),
            SavedAt = DateTimeOffset.UtcNow
        };

        _context.SavedVacancies.Add(entity);

        VacancyEntity? vacancy = await _context.Vacancies
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == request.VacancyId, ct);

        if (vacancy != null)
        {
            entity.Vacancy = vacancy;
        }

        await _context.SaveChangesAsync(ct);

        return MapToDto(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        SavedVacancyEntity? entity = await _context.SavedVacancies
            .FirstOrDefaultAsync(s => s.Id == id || s.VacancyId == id, ct);

        if (entity is null)
        {
            return false;
        }

        _context.SavedVacancies.Remove(entity);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ExistsForVacancyAsync(Guid vacancyId, CancellationToken ct = default)
    {
        return await _context.SavedVacancies.AnyAsync(s => s.VacancyId == vacancyId, ct);
    }

    private static SavedVacancyDto MapToDto(SavedVacancyEntity entity)
    {
        VacancyDto? vacancyDto = entity.Vacancy != null ? EfVacancyRepository.MapToDto(entity.Vacancy, isSaved: true) : null;

        return new SavedVacancyDto
        {
            Id = entity.Id,
            VacancyId = entity.VacancyId,
            Vacancy = vacancyDto,
            Notes = entity.Notes,
            SavedAt = entity.SavedAt
        };
    }
}
