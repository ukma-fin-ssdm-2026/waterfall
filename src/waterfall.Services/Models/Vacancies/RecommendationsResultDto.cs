// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Models.Vacancies;

/// <summary>
/// Результат підбору персоналізованих рекомендацій вакансій (FR-006, US-004).
/// </summary>
public class RecommendationsResultDto
{
    /// <summary>Рекомендовані (ще не збережені) вакансії, відсортовані за релевантністю.</summary>
    public IReadOnlyList<VacancyDto> Items { get; set; } = [];

    /// <summary>Кількість активних збережених вакансій користувача (0 означає початковий стан).</summary>
    public int SavedCount { get; set; }

    /// <summary>Ключові технології та слова зі збережених вакансій, на основі яких підібрано рекомендації.</summary>
    public IReadOnlyList<string> BasisKeywords { get; set; } = [];

    /// <summary>Недавні пошукові запити користувача, враховані при підборі.</summary>
    public IReadOnlyList<string> BasisSearches { get; set; } = [];
}
