// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;

namespace Waterfall.Repository.Repositories;

/// <summary>
/// Репозиторій доступу до таблиці користувачів на базі Entity Framework Core.
/// Працює виключно із сутностями UserDB згідно з Clean Architecture.
/// </summary>
public class EfUserRepository : IUserRepository
{
    private readonly WaterfallDbContext _context;

    public EfUserRepository(WaterfallDbContext context)
    {
        _context = context;
    }

    public async Task<UserDB?> GetByEmailAsync(string normalizedEmail, CancellationToken ct = default)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);
    }

    public async Task<UserDB?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<UserDB> CreateAsync(UserDB entity, CancellationToken ct = default)
    {
        _context.Users.Add(entity);
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(UserDB entity, CancellationToken ct = default)
    {
        _context.Users.Update(entity);
        await _context.SaveChangesAsync(ct);
    }
}
