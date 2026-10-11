// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Entities;

namespace Waterfall.Repository.Interfaces;

/// <summary>
/// Інтерфейс доступу до даних користувачів на рівні реляційної БД (Repository Layer).
/// Працює виключно із сутностями UserDB. Email очікується вже нормалізованим (нижній регістр, без пробілів)
/// — нормалізація є контрактом сервісного шару.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Знайти користувача за нормалізованим email (відстежувана сутність — для шляху перехешування).
    /// </summary>
    Task<UserDB?> GetByEmailAsync(string normalizedEmail, CancellationToken ct = default);

    /// <summary>
    /// Створити користувача. Порушення унікальності email (гонка) лишає DbUpdateException викликувачу.
    /// </summary>
    Task<UserDB> CreateAsync(UserDB entity, CancellationToken ct = default);

    /// <summary>
    /// Зберегти зміни сутності (шлях перехешування пароля).
    /// </summary>
    Task UpdateAsync(UserDB entity, CancellationToken ct = default);
}
