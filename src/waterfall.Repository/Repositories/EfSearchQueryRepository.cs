// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;

namespace Waterfall.Repository.Repositories;

/// <summary>
/// Репозиторій історії пошукових запитів (FR-007) на базі EF Core, сутність SearchQueryDB.
/// </summary>
public class EfSearchQueryRepository : ISearchQueryRepository
{
    private readonly WaterfallDbContext _context;

    public EfSearchQueryRepository(WaterfallDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(SearchQueryDB entity, CancellationToken ct = default)
    {
        if (entity.Id == Guid.Empty)
        {
            entity.Id = Guid.NewGuid();
        }

        _context.SearchQueries.Add(entity);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SearchQueryDB>> GetRecentAsync(Guid userId, int limit, CancellationToken ct = default)
    {
        if (limit <= 0)
        {
            return [];
        }

        return await _context.SearchQueries
            .AsNoTracking()
            .Where(q => q.UserId == userId)
            .OrderByDescending(q => q.SearchedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<SearchQueryDB?> GetLatestAsync(Guid userId, CancellationToken ct = default)
    {
        return await _context.SearchQueries
            .AsNoTracking()
            .Where(q => q.UserId == userId)
            .OrderByDescending(q => q.SearchedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> GetCountAsync(Guid userId, CancellationToken ct = default)
    {
        return await _context.SearchQueries
            .AsNoTracking()
            .CountAsync(q => q.UserId == userId, ct);
    }

    public async Task<bool> DeleteAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        List<SearchQueryDB> entities = await _context.SearchQueries
            .Where(q => q.UserId == userId)
            .ToListAsync(ct);

        if (entities.Count == 0)
        {
            return false;
        }

        _context.SearchQueries.RemoveRange(entities);
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
