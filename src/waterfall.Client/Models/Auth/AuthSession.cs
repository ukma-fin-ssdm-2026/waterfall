// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text;
using System.Text.Json;

namespace Waterfall.Client.Models.Auth;

/// <summary>
/// Поточний користувач клієнта: email, роль і момент спливу access-JWT.
/// </summary>
public sealed record AuthSession(string Email, UserRole Role, DateTimeOffset ExpiresAt)
{
    // JwtSecurityTokenHandler пише ClaimTypes.Role як "role"; повну URI-форму теж приймаємо.
    private static readonly string[] RoleClaims = ["role", "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"];

    public static AuthSession FromResponse(AuthResponse response) => new(response.Email, response.Role, response.ExpiresAt);

    /// <summary>
    /// Читає claims (email, role, exp) із payload збереженого JWT, щоб після перезапуску показати, хто увійшов.
    /// Лише для відображення: підпис перевіряє сервер на кожному запиті.
    /// </summary>
    /// <returns>Сесію або null, якщо токен не розбирається чи бракує claims.</returns>
    public static AuthSession? FromToken(string token)
    {
        string[] parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            using JsonDocument payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            JsonElement root = payload.RootElement;

            if (!root.TryGetProperty("email", out JsonElement email) || email.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("exp", out JsonElement exp) || !exp.TryGetInt64(out long expSeconds))
            {
                return null;
            }

            UserRole role = UserRole.JobSeeker;
            foreach (string claim in RoleClaims)
            {
                if (root.TryGetProperty(claim, out JsonElement value) && value.ValueKind == JsonValueKind.String
                    && Enum.TryParse(value.GetString(), ignoreCase: true, out UserRole parsed))
                {
                    role = parsed;
                    break;
                }
            }

            return new AuthSession(email.GetString()!, role, DateTimeOffset.FromUnixTimeSeconds(expSeconds));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    private static byte[] Base64UrlDecode(string segment)
    {
        string base64 = segment.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        return Convert.FromBase64String(base64);
    }
}
