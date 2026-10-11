// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Exceptions;

/// <summary>
/// Email вже зареєстровано (контролер мапить на 409 Conflict).
/// </summary>
public sealed class DuplicateEmailException : Exception
{
    public DuplicateEmailException()
        : base("Користувач із таким email вже існує.")
    {
    }

    public DuplicateEmailException(Exception inner)
        : base("Користувач із таким email вже існує.", inner)
    {
    }
}

/// <summary>
/// Невірні облікові дані. Навмисно одне повідомлення для «немає такого email» і неправильний пароль» (THR-01).
/// Контролер мапить на 401 з узагальненим текстом; текст винятку всередину відповіді не потрапляє.
/// </summary>
public sealed class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException()
        : base("Неправильний email або пароль.")
    {
    }
}
