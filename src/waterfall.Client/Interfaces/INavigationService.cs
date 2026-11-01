// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Client.Interfaces;

/// <summary>
/// Перемикання вкладок застосунку. ViewModels не знають про MAUI Shell:
/// реалізацію з маршрутами <c>//catalog</c> / <c>//aiconsultant</c> надає waterfall.UI.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Відкрити каталог вакансій (REL-003: запасний шлях, коли ШІ недоступний).
    /// </summary>
    Task GoToCatalogAsync();

    /// <summary>
    /// Відкрити вкладку «ШІ-консультант» (FR-010: показ результату аналізу вакансії).
    /// </summary>
    Task GoToAiConsultantAsync();
}
