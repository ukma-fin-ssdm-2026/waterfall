// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Services.Interfaces;

namespace Waterfall.Services;

/// <summary>
/// Реалізація сервісу роботи з персональними даними користувача у БД.
/// </summary>
public class UserProfileService : IUserProfileService
{
    private readonly WaterfallDbContext _context;

    public UserProfileService(WaterfallDbContext context)
    {
        _context = context;
    }

    public async Task<string?> GetUserAddressAsync(Guid userId, CancellationToken ct = default)
    {
        UserPersonalDataDB? data = await _context.UserPersonalData
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
        return data?.Address;
    }

    public async Task SaveUserAddressAsync(Guid userId, string address, CancellationToken ct = default)
    {
        UserPersonalDataDB? data = await _context.UserPersonalData
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (data is null)
        {
            data = new UserPersonalDataDB
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Address = address.Trim(),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _context.UserPersonalData.AddAsync(data, ct);
        }
        else
        {
            data.Address = address.Trim();
            data.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _context.SaveChangesAsync(ct);
    }
}
