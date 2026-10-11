// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;

namespace Waterfall.Services.Models.Auth;

/// <summary>
/// Модель запиту на вхід користувача (навмисно без політики складності, щоб не розкривати її на логіні, THR-01).
/// </summary>
public record LoginRequest
{
    /// <summary>
    /// Електронна пошта користувача.
    /// </summary>
    /// <example>jobseeker@example.com</example>
    [Required(ErrorMessage = "Поле email є обов'язковим")]
    [EmailAddress(ErrorMessage = "Поле email повинно бути валідною електронною адресою")]
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Пароль користувача (ніколи не логується, THR-08).
    /// </summary>
    /// <example>Secret123456</example>
    [Required(ErrorMessage = "Поле password є обов'язковим")]
    public string Password { get; init; } = string.Empty;
}
