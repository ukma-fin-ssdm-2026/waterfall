// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Models.Auth;

/// <summary>
/// Відповідь автентифікації: access-JWT (15 хв, THR-02) та ідентифікатор користувача.
/// </summary>
public record AuthResponse
{
    /// <summary>
    /// Access-JWT (HS256), несе claims sub=userId та userId=userId.
    /// </summary>
    /// <example>eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...</example>
    public string Token { get; init; } = string.Empty;

    /// <summary>
    /// Момент спливу токена (UTC).
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Ідентифікатор користувача (uuid, дублюється в sub/userId claims).
    /// </summary>
    /// <example>11111111-1111-1111-1111-111111111111</example>
    public Guid UserId { get; init; }

    /// <summary>
    /// Електронна пошта користувача.
    /// </summary>
    /// <example>jobseeker@example.com</example>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Роль користувача (дублюється в role claim JWT для серверних перевірок THR-13).
    /// </summary>
    /// <example>JobSeeker</example>
    public UserRole Role { get; init; } = UserRole.JobSeeker;
}
