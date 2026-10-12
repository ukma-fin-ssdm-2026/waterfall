// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Client.Models.Auth;

/// <summary>
/// Роль користувача; обирається при реєстрації та передається числом (0 / 1).
/// </summary>
public enum UserRole
{
    JobSeeker = 0,
    Analyst = 1
}

/// <summary>
/// Тіло POST /api/v1/auth/login.
/// </summary>
public record LoginRequest
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

/// <summary>
/// Тіло POST /api/v1/auth/register. Політику пароля (12–32 символи + цифра) перевіряє сервер.
/// </summary>
public record RegisterRequest
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public UserRole Role { get; init; } = UserRole.JobSeeker;
}

/// <summary>
/// Відповідь register / login: access-JWT та дані користувача.
/// </summary>
public record AuthResponse
{
    public string Token { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public UserRole Role { get; init; } = UserRole.JobSeeker;
}
