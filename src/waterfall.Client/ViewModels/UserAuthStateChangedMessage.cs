namespace Waterfall.Client.ViewModels;

/// <summary>
/// Повідомлення про зміну стану авторизації користувача (вхід, вихід, відновлення сесії).
/// </summary>
/// <param name="IsLoggedIn">Чи авторизований користувач у системі.</param>
/// <param name="Email">Електронна пошта користувача (якщо авторизований).</param>
public sealed record UserAuthStateChangedMessage(bool IsLoggedIn, string? Email = null);
