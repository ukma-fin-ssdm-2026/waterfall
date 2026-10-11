// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Security.Claims;

namespace Waterfall.Services.Interfaces;

/// <summary>
/// Інтерфейс випуску та перевірки access-JWT (Authentication Service, Core Services Layer).
/// Токен несе sub=userId ТА userId=userId, email, role, jti; час життя 15 хв (THR-02).
/// Навмисно працює з примітивами, а не з UserDB: контракт лишається вільним від сутностей БД,
/// як IVacancyService оперує DTO (сутності знає лише реалізація в api).
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Випустити токен; повертає токен і момент спливу (UTC).
    /// </summary>
    (string Token, DateTimeOffset ExpiresAt) Generate(Guid userId, string email, string role);

    /// <summary>
    /// Перевірити токен (підпис, issuer/audience, час життя, ClockSkew = Zero).
    /// Повертає principal або null, якщо токен недійсний/прострочений/підроблений.
    /// </summary>
    ClaimsPrincipal? Validate(string token);
}
