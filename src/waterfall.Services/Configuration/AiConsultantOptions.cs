// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Configuration;

/// <summary>
/// Налаштування ШІ-консультанта (секція <c>AiConsultant</c> у appsettings.json).
/// </summary>
public sealed class AiConsultantOptions
{
    /// <summary>
    /// Ім'я секції конфігурації.
    /// </summary>
    public const string SectionName = "AiConsultant";

    /// <summary>
    /// Скільки останніх повідомлень діалогу передавати LLM як контекст (FR-009).
    /// </summary>
    public int HistoryMessages { get; set; } = 10;

    /// <summary>
    /// Скільки вакансій показувати у відповіді на один запит.
    /// </summary>
    public int MaxVacancies { get; set; } = 5;

    /// <summary>
    /// Ліміт запитів до ШІ на користувача за годину (THR-10, REL-003).
    /// </summary>
    public int MessagesPerHour { get; set; } = 20;
}
