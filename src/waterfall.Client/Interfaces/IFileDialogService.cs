// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Client.Interfaces;

/// <summary>
/// Нативні діалоги вибору файлу для імпорту та шляху збереження для експорту JSON.
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Відкриває діалог вибору існуючого JSON-файлу. Повертає повний шлях або null, якщо користувач скасував вибір.
    /// </summary>
    Task<string?> PickJsonFileToOpenAsync(CancellationToken ct = default);

    /// <summary>
    /// Відкриває діалог «Зберегти як…» для JSON-файлу. Повертає обраний повний шлях або null, якщо користувач скасував дію.
    /// </summary>
    Task<string?> PickJsonFileToSaveAsync(string suggestedFileName, CancellationToken ct = default);
}
