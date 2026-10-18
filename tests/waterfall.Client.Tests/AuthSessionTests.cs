// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text;
using System.Text.Json;
using Waterfall.Client.Models.Auth;

namespace Waterfall.Client.Tests;

public class AuthSessionTests
{
    private static readonly DateTimeOffset Expires = new(2026, 10, 11, 12, 15, 0, TimeSpan.Zero);

    /// <summary>
    /// JWT у форматі сервера: base64url БЕЗ падінгу, підпис клієнт не перевіряє (копія LoginViewModelTests.Jwt).
    /// </summary>
    private static string Jwt(string payloadJson)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("""{"alg":"HS256","typ":"JWT"}""")}.{B64(payloadJson)}.sig";
    }

    private static string Payload(string email, string role, long exp) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["email"] = email,
            ["role"] = role,
            ["exp"] = exp,
        });

    /// <summary>
    /// Регресія: Base64UrlDecode без зовнішнього `% 4` додавав 4 зайві '=', коли довжина
    /// сегмента вже кратна 4, тож FromToken повертав null — і RestoreSessionAsync розлогінював
    /// користувача після перезапуску (лише для деяких довжин email). Остача 1 для валідного
    /// base64 неможлива, тому покрито всі досяжні остачі довжини сегмента: 0, 2, 3.
    /// </summary>
    [Theory]
    [InlineData("kosta@ukma.edu.ua")] // сегмент % 4 == 0: падінг не потрібен
    [InlineData("a@b.c")] // сегмент % 4 == 0
    [InlineData("a@b.cd")] // сегмент % 4 == 2: два '='
    [InlineData("anna@ukma.edu.ua")] // реалістичний email, сегмент % 4 == 3: один '='
    [InlineData("a@b.cde")] // сегмент % 4 == 3
    public void FromToken_PayloadOfAnyBase64Length_RoundTripsClaims(string email)
    {
        long exp = Expires.ToUnixTimeSeconds();
        string token = Jwt(Payload(email, "Analyst", exp));

        AuthSession? session = AuthSession.FromToken(token);

        Assert.Equal(new AuthSession(email, UserRole.Analyst, Expires), session);
    }
}
