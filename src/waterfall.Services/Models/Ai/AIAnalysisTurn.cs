// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.Extensions.AI;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Models.Ai;

/// <summary>
/// Підготовлений запит аналізу вакансій (FR-010): рівно ті вакансії, які попросив користувач,
/// їхня детермінована релевантність і готовий промпт для <c>LlmRole.Analyze</c>.
/// Релевантність обчислюється ДО будь-якого звернення до LLM, тож клієнт отримує її навіть
/// коли провайдер недоступний (REL-003).
/// </summary>
public sealed record AIAnalysisTurn
{
    /// <summary>Вакансії в порядку, в якому їх передав користувач (1..3, без дублікатів).</summary>
    public IReadOnlyList<VacancyDto> Vacancies { get; init; } = Array.Empty<VacancyDto>();

    /// <summary>Оцінки релевантності в тому самому порядку, що й <see cref="Vacancies"/>.</summary>
    public IReadOnlyList<VacancyRelevance> Relevance { get; init; } = Array.Empty<VacancyRelevance>();

    /// <summary>Промпт для LLM: лише поля цих вакансій, оцінки релевантності та узагальнений профіль (THR-06).</summary>
    public IReadOnlyList<ChatMessage> Prompt { get; init; } = Array.Empty<ChatMessage>();
}
