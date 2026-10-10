// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;

namespace Waterfall.Services.Models.Auth;

/// <summary>
/// Модель запиту на реєстрацію нового користувача (роль JobSeeker за замовчуванням або Analyst, FR-008).
/// </summary>
public record RegisterRequest
{
    /// <summary>
    /// Електронна пошта користувача (унікальна, використовується як логін).
    /// </summary>
    /// <example>jobseeker@example.com</example>
    [Required(ErrorMessage = "Поле email є обов'язковим")]
    [EmailAddress(ErrorMessage = "Поле email повинно бути валідною електронною адресою")]
    [StringLength(80, MinimumLength = 3, ErrorMessage = "Довжина email повинна бути від 3 до 80 символів")]
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Пароль користувача (мінімум 12 символів та хоча б одна цифра; зберігається лише як хеш, SEC-001).
    /// </summary>
    /// <example>Secret123456</example>
    [Required(ErrorMessage = "Поле password є обов'язковим")]
    [StringLength(32, MinimumLength = 12, ErrorMessage = "Довжина password повинна бути від 12 до 32 символів")]
    [RegularExpression(@".*\d.*", ErrorMessage = "Поле password повинно містити хоча б одну цифру")]
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Роль користувача (за замовчуванням JobSeeker; Analyst відкриває агреговану аналітику).
    /// Серверні перевірки доступу завжди виконуються на сервері за role claim JWT (THR-13).
    /// </summary>
    /// <example>JobSeeker</example>
    [EnumDataType(typeof(UserRole), ErrorMessage = "Поле role повинно бути одним із значень: JobSeeker, Analyst")]
    public UserRole Role { get; init; } = UserRole.JobSeeker;
}
