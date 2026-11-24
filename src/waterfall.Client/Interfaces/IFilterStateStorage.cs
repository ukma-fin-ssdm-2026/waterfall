// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Client.Interfaces;

/// <summary>
/// Локальне сховище стану фільтрів каталогу (REL-002): один JSON-рядок на пристрої.
/// У застосунку це Preferences MAUI, у тестах — пам'ять.
/// </summary>
public interface IFilterStateStorage
{
    /// <summary>Повертає збережений рядок або <c>null</c>, якщо нічого не збережено чи сховище недоступне.</summary>
    string? Load();

    /// <summary>Записує рядок одразу (синхронно), щоб стан пережив аварійне завершення.</summary>
    void Save(string value);
}
