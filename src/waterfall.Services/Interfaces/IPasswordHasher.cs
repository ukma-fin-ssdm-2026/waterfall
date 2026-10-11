// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Interfaces;

/// <summary>
/// Результат перевірки пароля (дзеркалить PasswordVerificationResult без залежності від ASP.NET Identity,
/// щоб контракт лишався MAUI-сумісним; SuccessRehashNeeded живить шлях перехешування, SEC-001).
/// </summary>
public enum PasswordVerifyResult
{
    /// <summary>
    /// Пароль не збігається.
    /// </summary>
    Failed = 0,

    /// <summary>
    /// Пароль збігається.
    /// </summary>
    Success = 1,

    /// <summary>
    /// Пароль збігається, але хеш створено застарілим форматом — слід перехешувати та зберегти.
    /// </summary>
    SuccessRehashNeeded = 2
}

/// <summary>
/// Інтерфейс хешування паролів (Security Service, Core Services Layer).
/// Реалізація за замовчуванням — ASP.NET Identity PBKDF2; заміна на BCrypt/Argon2 — один клас + один рядок DI (SEC-001).
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Створити стійкий хеш пароля (сіль вбудовано у формат).
    /// </summary>
    string Hash(string password);

    /// <summary>
    /// Перевірити пароль проти збереженого хеша.
    /// </summary>
    /// <param name="hashedPassword">
    /// Збережений хеш або null, якщо користувача не знайдено: реалізація виконує еквівалентну
    /// фіктивну роботу проти заглушки власного формату і повертає Failed. Це вирівнює час
    /// відповіді між гілками "неправильний логін" та "неправильний пароль" (THR-01)
    /// </param>
    /// <param name="providedPassword">Пароль із запиту (ніколи не логується, THR-08).</param>
    PasswordVerifyResult Verify(string? hashedPassword, string providedPassword);
}
