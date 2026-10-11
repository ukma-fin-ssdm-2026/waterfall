// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Services.Models.Auth;

namespace Waterfall.Services.Interfaces;

/// <summary>
/// Інтерфейс сервісу автентифікації (реєстрація та вхід, Core Services Layer).
/// Помилки сигналізуються винятками: DuplicateEmailException → 409, InvalidCredentialsException → 401.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Зареєструвати користувача (роль із запиту, email нормалізується до нижнього регістру).
    /// </summary>
    /// <exception cref="Exceptions.DuplicateEmailException">Email вже зайнято.</exception>
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    /// <summary>
    /// Увійти (невідомий email і неправильний пароль дають однаковий виняток, THR-01).
    /// </summary>
    /// <exception cref="Exceptions.InvalidCredentialsException">Неправильний email або пароль.</exception>
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
}
