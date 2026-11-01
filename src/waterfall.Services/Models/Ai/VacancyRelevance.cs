// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Models.Ai;

/// <summary>
/// Результат детермінованої оцінки релевантності вакансії профілю користувача (FR-010):
/// ціле 0..100, збіги, на яких вона побудована, і кількість збережених вакансій в основі.
/// </summary>
/// <param name="VacancyId">Ідентифікатор оціненої вакансії.</param>
/// <param name="Score">
/// Оцінка 0..100; null, якщо в користувача немає збережених вакансій (тоді й <paramref name="BasisCount"/> = 0).
/// </param>
/// <param name="Matched">Збіги, які підвищили оцінку: категорія, формати роботи, топ ключових слів.</param>
/// <param name="BasisCount">Скільки збережених вакансій дали профіль (basis_count).</param>
public sealed record VacancyRelevance(
    Guid VacancyId,
    int? Score,
    IReadOnlyList<string> Matched,
    int BasisCount);
