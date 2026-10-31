// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;

namespace Waterfall.Repository.Repositories;

/// <summary>
/// Репозиторій історії переглядів вакансій (FR-006, FR-007) на базі EF Core, сутність VacancyViewDB.
/// </summary>
public class EfVacancyViewRepository : IVacancyViewRepository
{
    private readonly WaterfallDbContext _context;

    public EfVacancyViewRepository(WaterfallDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(VacancyViewDB entity, CancellationToken ct = default)
    {
        if (entity.Id == Guid.Empty)
        {
            entity.Id = Guid.NewGuid();
        }

        _context.VacancyViews.Add(entity);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<bool> ExistsRecentAsync(Guid userId, Guid vacancyId, TimeSpan within, CancellationToken ct = default)
    {
        DateTimeOffset threshold = DateTimeOffset.UtcNow - within;

        return await _context.VacancyViews
            .AsNoTracking()
            .AnyAsync(v => v.UserId == userId && v.VacancyId == vacancyId && v.ViewedAt >= threshold, ct);
    }

    public async Task<IReadOnlyList<VacancyViewDB>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        return await _context.VacancyViews
            .AsNoTracking()
            .Include(v => v.Vacancy)
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.ViewedAt)
            .ToListAsync(ct);
    }
}
