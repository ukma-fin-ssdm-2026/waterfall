// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.AspNetCore.Identity;
using Waterfall.Services.Interfaces;

namespace Waterfall.Api.Services;

/// <summary>
/// Хешування паролів на базі ASP.NET Identity PasswordHasher (PBKDF2-HMACSHA256/512, формат із версією та сіллю).
/// Заміна на BCrypt/Argon2 — цей один клас + один рядок DI (SEC-001).
/// </summary>
/// <remarks>
/// Розміщення: api, а не waterfall.Services (див. remarks в AuthenticationService).
/// Криптографія паролів — серверна відповідальність (Security Service, HLD §3.4).
/// </remarks>
public sealed class AspNetPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<object> _inner = new();

    /// <summary>
    /// Заглушка власного формату для вирівнювання часу (див. IPasswordHasher.Verify).
    /// Статична — один раз на процес; належить реалізації, тож заміна хешера (BCrypt/Argon2)
    /// приносить власну заглушку автоматично.
    /// </summary>
    private static readonly string DummyHash = new PasswordHasher<object>().HashPassword(null!, "dummy-unused-value");

    public string Hash(string password)
    {
        // Стандартна реалізація об'єкт користувача не використовує (лише для можливих перевизначень).
        return _inner.HashPassword(null!, password);
    }

    public PasswordVerifyResult Verify(string? hashedPassword, string providedPassword)
    {
        PasswordVerificationResult raw = _inner.VerifyHashedPassword(null!, hashedPassword ?? DummyHash, providedPassword);
        if (hashedPassword is null)
        {
            return PasswordVerifyResult.Failed;
        }

        return raw switch
        {
            PasswordVerificationResult.Success => PasswordVerifyResult.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerifyResult.SuccessRehashNeeded,
            _ => PasswordVerifyResult.Failed
        };
    }
}
