// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;

namespace Waterfall.Repository.Repositories;

/// <summary>
/// Репозиторій доступу до збережених вакансій («лайків» / закладок) на базі EF Core.
/// Працює виключно із сутностями SavedVacancyDB згідно з Clean Architecture.
/// </summary>
public class EfSavedVacancyRepository : ISavedVacancyRepository
{
    private readonly WaterfallDbContext _context;

    public EfSavedVacancyRepository(WaterfallDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<SavedVacancyDB>> GetAllAsync(bool onlyActive = true, CancellationToken ct = default)
    {
        IQueryable<SavedVacancyDB> baseQuery = _context.SavedVacancies
            .AsNoTracking()
            .Include(s => s.Vacancy);

        IQueryable<SavedVacancyDB> query;
        if (onlyActive)
        {
            query = baseQuery.Where(s => s.Vacancy != null && s.Vacancy.IsActive);
        }
        else
        {
            IQueryable<SavedVacancyDB> activeQuery = baseQuery.Where(s => s.Vacancy != null && s.Vacancy.IsActive);
            IQueryable<SavedVacancyDB> archiveQuery = baseQuery
                .Where(s => s.Vacancy == null || !s.Vacancy.IsActive)
                .OrderByDescending(s => s.SavedAt)
                .Take(1000);

            query = activeQuery.Concat(archiveQuery);
        }

        return await query
            .OrderByDescending(s => s.SavedAt)
            .ToListAsync(ct);
    }

    public async Task<SavedVacancyDB?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.SavedVacancies
            .AsNoTracking()
            .Include(s => s.Vacancy)
            .FirstOrDefaultAsync(s => s.Id == id || s.VacancyId == id, ct);
    }

    public async Task<SavedVacancyDB> SaveAsync(SavedVacancyDB entity, CancellationToken ct = default)
    {
        if (entity.Id == Guid.Empty)
        {
            entity.Id = Guid.NewGuid();
        }

        if (entity.SavedAt == default)
        {
            entity.SavedAt = DateTimeOffset.UtcNow;
        }

        _context.SavedVacancies.Add(entity);

        VacancyDB? vacancy = await _context.Vacancies
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == entity.VacancyId, ct);

        if (vacancy != null)
        {
            entity.Vacancy = vacancy;
        }

        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        SavedVacancyDB? entity = await _context.SavedVacancies
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

    public async Task<HashSet<Guid>> GetSavedVacancyIdsAsync(IEnumerable<Guid> vacancyIds, CancellationToken ct = default)
    {
        List<Guid> ids = vacancyIds.ToList();
        if (ids.Count == 0)
        {
            return new HashSet<Guid>();
        }

        List<Guid> found = await _context.SavedVacancies
            .AsNoTracking()
            .Where(s => ids.Contains(s.VacancyId))
            .Select(s => s.VacancyId)
            .ToListAsync(ct);

        return found.ToHashSet();
    }
}
