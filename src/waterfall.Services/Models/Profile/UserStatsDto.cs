// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Models.Profile;

/// <summary>
/// Відповідь <c>GET api/v1/profile/stats</c> — статистика активності користувача (FR-007, US-008).
/// Поведінкові ПД (data-model.md §5): повертається лише самому користувачеві (THR-12).
/// </summary>
public class UserStatsDto
{
    /// <summary>
    /// Кількість активних збережених вакансій (як у розділі «Обране»).
    /// </summary>
    public int SavedVacanciesCount { get; set; }

    /// <summary>
    /// Загальна кількість виконаних пошукових запитів.
    /// </summary>
    public int TotalSearches { get; set; }

    /// <summary>
    /// Топ технологій за частотою серед переглянутих і збережених вакансій.
    /// </summary>
    public IReadOnlyList<StatCountDto> TopTechnologies { get; set; } = [];

    /// <summary>
    /// Топ категорій за частотою серед переглянутих і збережених вакансій.
    /// </summary>
    public IReadOnlyList<StatCountDto> TopCategories { get; set; } = [];

    /// <summary>
    /// Останні пошукові запити (новіші спершу).
    /// </summary>
    public IReadOnlyList<string> RecentSearches { get; set; } = [];
}

/// <summary>
/// Елемент агрегованої частоти: назва технології/категорії та кількість переглядів і збережень.
/// </summary>
public class StatCountDto
{
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}
