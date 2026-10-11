// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Api.Options;

/// <summary>
/// Налаштування JWT (секція Jwt:*). Issuer/Audience/ExpiresMinutes — не секрети (appsettings.json);
/// Key — лише змінна середовища Jwt__Key / User Secrets, ніколи в репозиторії (THR-09).
/// </summary>
public sealed record JwtOptions
{
    /// <summary>
    /// Видавець токена.
    /// </summary>
    /// <example>Waterfall</example>
    public string Issuer { get; init; } = string.Empty;

    /// <summary>
    /// Аудиторія токена.
    /// </summary>
    /// <example>WaterfallClients</example>
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// Симетричний ключ підпису HS256 (мінімум 32 символи / 256 біт). Тільки з середовища.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Час життя access-JWT у хвилинах (15 за THR-02).
    /// </summary>
    public int ExpiresMinutes { get; init; } = 15;
}
