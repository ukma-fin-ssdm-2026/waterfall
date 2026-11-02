// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Client.Models.Profile;

/// <summary>
/// Відповідь GET api/v1/profile/stats — статистика активності користувача (FR-007, US-008).
/// Поведінкові ПД: повертається лише самому користувачеві (THR-12).
/// </summary>
public class UserStatsDto
{
    /// <summary>
    /// Кількість активних збережених вакансій.
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
/// Елемент агрегованої частоти: назва та кількість входжень.
/// </summary>
public class StatCountDto
{
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}
