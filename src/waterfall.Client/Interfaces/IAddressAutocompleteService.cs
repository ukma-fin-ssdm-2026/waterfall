// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Client.Interfaces;

/// <summary>
/// Сервіс безкоштовного гео-автодоповнення адрес (search-as-you-type) на базі відкритих даних OpenStreetMap.
/// </summary>
public interface IAddressAutocompleteService
{
    /// <summary>
    /// Отримує список підказок адрес для введеного тексту.
    /// </summary>
    /// <param name="query">Частина адреси або назва вулиці/міста.</param>
    /// <param name="ct">Токен скасування.</param>
    /// <returns>Список форматованих варіантів адрес.</returns>
    Task<IReadOnlyList<string>> GetSuggestionsAsync(string query, CancellationToken ct = default);
}
