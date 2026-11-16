// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Client.Models.Vacancies;

/// <summary>
/// Результат підбору персоналізованих рекомендацій вакансій (GET /api/v1/vacancies/recommendations).
/// </summary>
public class RecommendationsResultDto
{
    public IReadOnlyList<VacancyDto> Items { get; set; } = [];

    public int SavedCount { get; set; }

    public IReadOnlyList<string> BasisKeywords { get; set; } = [];

    public IReadOnlyList<string> BasisSearches { get; set; } = [];
}
