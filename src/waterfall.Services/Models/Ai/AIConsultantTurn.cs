// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.Extensions.AI;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Models.Ai;

/// <summary>
/// Результат підготовки однієї репліки діалогу: усе, що можна віддати клієнту до початку
/// стрімінгу відповіді (події <c>meta</c> та <c>vacancies</c>), і те, що потрібне сервісу
/// для подальшого стрімінгу.
/// </summary>
public sealed class AIConsultantTurn
{
    /// <summary>
    /// Ідентифікатор діалогу (новий або наявний).
    /// </summary>
    public Guid SessionId { get; init; }

    /// <summary>
    /// Застосований фільтр після уточнення (те, що клієнт показує як активні фільтри).
    /// </summary>
    public AppliedFilter Filter { get; init; } = AppliedFilter.Empty;

    /// <summary>
    /// Вакансії з БД, які буде показано користувачу (FUNC-001).
    /// </summary>
    public IReadOnlyList<VacancyDto> Vacancies { get; init; } = Array.Empty<VacancyDto>();

    /// <summary>
    /// true — усі вакансії відповідають фільтру; false — застосовано послаблення (US-005, критерій 3).
    /// </summary>
    public bool Exact { get; init; } = true;

    /// <summary>
    /// Ключі фільтра, які довелося зняти, щоб отримати хоч якісь результати (не більше трьох).
    /// </summary>
    public IReadOnlyList<string> Relaxed { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Пояснення для користувача, що точних збігів не знайдено (лише коли <see cref="Exact"/> = false).
    /// </summary>
    public string? Disclaimer { get; init; }

    /// <summary>
    /// Власник діалогу (завжди з JWT) — потрібен, щоб зберегти відповідь із тим самим обмеженням
    /// власника, з яким її було підготовлено (THR-12).
    /// </summary>
    internal Guid UserId { get; init; }

    /// <summary>
    /// Готовий промпт для формування тексту відповіді: контекст діалогу + лише поля знайдених
    /// вакансій. Заповнюється сервісом, контролеру не потрібен.
    /// </summary>
    internal IReadOnlyList<ChatMessage> SummaryPrompt { get; init; } = Array.Empty<ChatMessage>();
}
