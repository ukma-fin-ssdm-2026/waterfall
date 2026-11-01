// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Normalization;

namespace Waterfall.Services.Models.Vacancies;

/// <summary>
/// Закритий довідник широких категорій вакансій — спільна мова каталогу й ШІ-консультанта:
/// витягування фільтра з вільного тексту обмежене цими значеннями, тож модель не вигадає категорії,
/// якої немає в даних (FUNC-001). Довідник містить і IT, і не-IT напрями.
/// </summary>
public static class CategoryGroups
{
    /// <summary>Група для категорій, яких немає в довіднику.</summary>
    public const string Other = VacancyCategoryMap.Other;

    /// <summary>Усі канонічні групи; остання — <see cref="Other"/>.</summary>
    public static IReadOnlyList<string> All => VacancyCategoryMap.All;
}
