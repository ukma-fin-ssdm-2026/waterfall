// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Client.Interfaces;

/// <summary>
/// Сховище access-JWT. У застосунку — SecureStorage, у тестах — пам'ять.
/// </summary>
public interface IAuthTokenStore
{
    Task<string?> GetTokenAsync(CancellationToken ct = default);
    Task SetTokenAsync(string token, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}
