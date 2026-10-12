// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Client.Interfaces;

namespace Waterfall.Client.Tests;

/// <summary>
/// Сховище токена в пам'яті (замість SecureStorage застосунку).
/// </summary>
public sealed class InMemoryTokenStore : IAuthTokenStore
{
    public string? Token { get; set; }

    public Task<string?> GetTokenAsync(CancellationToken ct = default) => Task.FromResult(Token);

    public Task SetTokenAsync(string token, CancellationToken ct = default)
    {
        Token = token;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        Token = null;
        return Task.CompletedTask;
    }
}
