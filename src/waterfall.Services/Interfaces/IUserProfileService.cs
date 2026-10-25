// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Interfaces;

/// <summary>
/// Сервіс керування персональними даними профілю користувача (UserPersonalData).
/// </summary>
public interface IUserProfileService
{
    Task<string?> GetUserAddressAsync(Guid userId, CancellationToken ct = default);
    Task SaveUserAddressAsync(Guid userId, string address, CancellationToken ct = default);
}
