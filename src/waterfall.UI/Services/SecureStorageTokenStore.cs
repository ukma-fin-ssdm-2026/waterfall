// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Client.Interfaces;

namespace waterfall.UI.Services;

/// <summary>
/// Зберігає access-JWT у SecureStorage платформи (Keychain / Keystore / DPAPI).
/// Токен кешується в пам'яті: AuthHeaderHandler читає його на кожен запит.
/// </summary>
public sealed class SecureStorageTokenStore : IAuthTokenStore
{
    private const string Key = "waterfall_access_token";

    private readonly ISecureStorage _storage;
    private string? _token;
    private bool _loaded;

    public SecureStorageTokenStore(ISecureStorage storage)
    {
        _storage = storage;
    }

    public async Task<string?> GetTokenAsync(CancellationToken ct = default)
    {
        if (!_loaded)
        {
            _token = await _storage.GetAsync(Key);
            _loaded = true;
        }

        return _token;
    }

    public async Task SetTokenAsync(string token, CancellationToken ct = default)
    {
        await _storage.SetAsync(Key, token);
        _token = token;
        _loaded = true;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        _storage.Remove(Key);
        _token = null;
        _loaded = true;
        return Task.CompletedTask;
    }
}
