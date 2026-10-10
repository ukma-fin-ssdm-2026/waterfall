// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services.Exceptions;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Auth;

namespace Waterfall.Api.Services;

/// <summary>
/// Сервіс автентифікації: реєстрація та вхід (Core Services Layer).
/// Email нормалізується (trim + нижній регістр) до звернення до БД, щоб унікальний індекс
/// покривав регістрозалежні дублікати. Невідомий email і невірний пароль — однаковий виняток (THR-01).
/// Логує лише email + результат, ніколи пароль чи токен (THR-08).
/// </summary>
/// <remarks>
/// Розміщення: реалізація живе в api, а не в waterfall.Services.
/// waterfall.Services мультитаргетиться на MAUI, а отже поширюється на клієнта;
/// серверні залежності цього сервісу не повинні потрапляти в застосунок клієнта.
/// Контракт (IAuthenticationService) лишається в Services — пошарове розділення
/// (HLD) збережено логічно: тонкий контролер → інтерфейс → DI-реалізація.
/// </remarks>
public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwt;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwt,
        ILogger<AuthenticationService> logger)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _logger = logger;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        string email = NormalizeEmail(request.Email);

        if (await _users.GetByEmailAsync(email, ct) is not null)
        {
            _logger.LogWarning("Спроба реєстрації на зайнятий email: {Email}", email);
            throw new DuplicateEmailException();
        }

        UserDB entity = new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            Role = request.Role.ToString(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            await _users.CreateAsync(entity, ct);
        }
        catch (DbUpdateException ex)
        {
            // Гонка двох одночасних реєстрацій: унікальний індекс відхилив другу (data-model.md §6.1).
            if (await _users.GetByEmailAsync(email, ct) is not null)
            {
                _logger.LogWarning("Гонка реєстрацій на email: {Email}", email);
                throw new DuplicateEmailException(ex);
            }

            throw;
        }

        _logger.LogInformation("Користувача зареєстровано: {Email}", email);
        return Map(entity);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        string email = NormalizeEmail(request.Email);

        UserDB? user = await _users.GetByEmailAsync(email, ct);

        // Вирівнювання часу (THR-01).
        // Без цього гілка «немає такого email» була б помітно швидшою і дозволяла б перебирати
        // зареєстровані адреси за часом відповіді. Виклик із null-хешем змушує IPasswordHasher
        // виконати еквівалентну фіктивну роботу проти заглушки.
        PasswordVerifyResult result = _passwordHasher.Verify(user?.PasswordHash, request.Password);

        if (user is null || result == PasswordVerifyResult.Failed)
        {
            _logger.LogWarning("Невдала спроба входу: {Email}", email);
            throw new InvalidCredentialsException();
        }

        if (result == PasswordVerifyResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.Hash(request.Password);
            await _users.UpdateAsync(user, ct);
            _logger.LogInformation("Пароль перехешовано під час входу: {Email}", email);
        }

        _logger.LogInformation("Успішний вхід: {Email}", email);
        return Map(user);
    }

    private AuthResponse Map(UserDB user)
    {
        UserRole role = Enum.TryParse(user.Role, ignoreCase: true, out UserRole parsed)
            ? parsed
            : UserRole.JobSeeker;

        (string token, DateTimeOffset expiresAt) = _jwt.Generate(user.Id, user.Email, role.ToString());

        return new AuthResponse
        {
            Token = token,
            ExpiresAt = expiresAt,
            UserId = user.Id,
            Email = user.Email,
            Role = role
        };
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
