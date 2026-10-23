namespace Waterfall.Client.Interfaces;

/// <summary>
/// Сервіс для розрахунку відстані між поточною адресою користувача та адресою офісу вакансії.
/// </summary>
public interface IDistanceCalculatorService
{
    /// <summary>
    /// Вказана користувачем адреса його поточного місцезнаходження.
    /// </summary>
    string? UserAddress { get; set; }

    /// <summary>
    /// Розраховує відстань у кілометрах від UserAddress до вказаної адреси вакансії.
    /// </summary>
    /// <param name="destinationAddress">Адреса або локація роботи.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <returns>Відстань у км або null, якщо адресу не задано чи формат віддалений.</returns>
    Task<double?> CalculateDistanceKmAsync(string destinationAddress, CancellationToken ct = default);
}
