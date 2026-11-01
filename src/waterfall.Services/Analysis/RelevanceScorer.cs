// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Entities;
using Waterfall.Services.Models.Ai;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Analysis;

/// <summary>
/// Детермінована оцінка релевантності вакансії профілю користувача (FR-010): без LLM, лише
/// збіги збережених переваг із полями вакансії. Оцінка — зважена сума п'яти складових,
/// нормована на суму ваг тих складових, для яких є дані в профілі та у вакансії:
/// <list type="bullet">
/// <item><description>широка категорія — вага 30: значення дорівнює частоті категорії в профілі;</description></item>
/// <item><description>формат роботи — вага 20: сума частот форматів, які дозволяє вакансія (не більше 1);</description></item>
/// <item><description>досвід — вага 20: 1.0, якщо вакансія вимагає не більше типового досвіду користувача,
/// далі лінійне зниження до 0 на двох «запасах» по <see cref="ExperienceMarginMonths"/> місяців;</description></item>
/// <item><description>зарплата — вага 15: перетин діапазонів у тій самій валюті, інакше — відношення меж;</description></item>
/// <item><description>ключові слова — вага 15: частка ваги слів профілю, що є у вакансії.</description></item>
/// </list>
/// Якщо в користувача немає збережених вакансій, оцінки немає (null), а <c>basis_count</c> = 0.
/// </summary>
public static class RelevanceScorer
{
    /// <summary>Вага збігу широкої категорії.</summary>
    public const int CategoryWeight = 30;

    /// <summary>Вага збігу формату роботи.</summary>
    public const int WorkFormatWeight = 20;

    /// <summary>Вага відповідності досвіду.</summary>
    public const int ExperienceWeight = 20;

    /// <summary>Вага перетину зарплатних очікувань.</summary>
    public const int SalaryWeight = 15;

    /// <summary>Вага збігу ключових слів.</summary>
    public const int KeywordWeight = 15;

    /// <summary>«Запас» досвіду: скільки місяців понад типовий досвід користувача ще вважаємо прийнятним.</summary>
    public const int ExperienceMarginMonths = 12;

    /// <summary>Скільки ключових слів максимум показуємо в <see cref="VacancyRelevance.Matched"/>.</summary>
    public const int MaxMatchedKeywords = 5;

    /// <summary>Формати в сталому порядку — він визначає порядок збігів у відповіді.</summary>
    private static readonly WorkFormat[] FormatOrder = [WorkFormat.Remote, WorkFormat.Hybrid, WorkFormat.Office];

    /// <summary>
    /// Оцінити вакансію за профілем.
    /// </summary>
    /// <param name="profile">Профіль переваг (може бути <see cref="PreferenceProfile.Empty"/>).</param>
    /// <param name="vacancy">Вакансія з каталогу.</param>
    /// <returns>
    /// Оцінка 0..100 зі збігами та <c>basis_count</c>; для профілю без збережених вакансій —
    /// <see cref="VacancyRelevance.Score"/> = null і <see cref="VacancyRelevance.BasisCount"/> = 0.
    /// </returns>
    public static VacancyRelevance Score(PreferenceProfile profile, VacancyDto vacancy)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(vacancy);

        if (!profile.HasBasis)
        {
            return new VacancyRelevance(vacancy.Id, null, Array.Empty<string>(), 0);
        }

        List<string> matched = [];
        double totalWeight = 0;
        double totalScore = 0;

        // 1. Широка категорія: частка збережених вакансій із цією категорією
        if (profile.CategoryWeights.Count > 0 && !string.IsNullOrWhiteSpace(vacancy.CategoryGroup))
        {
            totalWeight += CategoryWeight;
            if (TryGetWeight(profile.CategoryWeights, vacancy.CategoryGroup, out double categoryValue))
            {
                totalScore += CategoryWeight * Math.Clamp(categoryValue, 0, 1);
                matched.Add(vacancy.CategoryGroup);
            }
        }

        // 2. Формат роботи: сума частот форматів, які дозволяє вакансія
        if (profile.WorkFormatWeights.Count > 0 && vacancy.WorkFormat != WorkFormat.None)
        {
            totalWeight += WorkFormatWeight;
            double formatValue = 0;

            foreach (WorkFormat format in FormatOrder)
            {
                if (!vacancy.WorkFormat.HasFlag(format))
                {
                    continue;
                }

                if (profile.WorkFormatWeights.TryGetValue(format, out double weight))
                {
                    formatValue += weight;
                    matched.Add(DescribeWorkFormat(format));
                }
            }

            totalScore += WorkFormatWeight * Math.Clamp(formatValue, 0, 1);
        }

        // 3. Досвід: вакансія не повинна вимагати суттєво більше, ніж типово в збережених
        if (profile.MedianExperienceMonths is int typicalExperience && vacancy.ExperienceMonths is int requiredExperience)
        {
            totalWeight += ExperienceWeight;
            totalScore += ExperienceWeight * ExperienceFit(typicalExperience, requiredExperience);
        }

        // 4. Зарплата: лише та сама валюта, курси не вигадуємо
        if (profile.Salary is SalaryPreference salary
            && !string.IsNullOrWhiteSpace(vacancy.Currency)
            && (vacancy.SalaryMin is not null || vacancy.SalaryMax is not null)
            && string.Equals(vacancy.Currency.Trim(), salary.Currency, StringComparison.OrdinalIgnoreCase))
        {
            totalWeight += SalaryWeight;
            totalScore += SalaryWeight * SalaryFit(salary, vacancy);
        }

        // 5. Ключові слова: частка ваги слів профілю, що трапляються у вакансії
        if (profile.KeywordWeights.Count > 0)
        {
            totalWeight += KeywordWeight;

            HashSet<string> vacancyTokens = new(VacancyTextTokenizer.TokenizeVacancy(vacancy), StringComparer.Ordinal);
            double matchedWeight = 0;
            double allWeight = 0;
            List<(string Token, double Weight)> overlaps = [];

            foreach ((string token, double weight) in profile.KeywordWeights)
            {
                allWeight += weight;
                if (vacancyTokens.Contains(token))
                {
                    matchedWeight += weight;
                    overlaps.Add((token, weight));
                }
            }

            if (allWeight > 0)
            {
                totalScore += KeywordWeight * Math.Clamp(matchedWeight / allWeight, 0, 1);
            }

            matched.AddRange(overlaps
                .OrderByDescending(overlap => overlap.Weight)
                .ThenBy(overlap => overlap.Token, StringComparer.Ordinal)
                .Take(MaxMatchedKeywords)
                .Select(overlap => overlap.Token));
        }

        int score = totalWeight <= 0
            ? 0
            : (int)Math.Round(100 * totalScore / totalWeight, MidpointRounding.AwayFromZero);

        return new VacancyRelevance(
            vacancy.Id,
            Math.Clamp(score, 0, 100),
            matched.Distinct(StringComparer.Ordinal).ToList(),
            profile.BasisCount);
    }

    /// <summary>
    /// Відповідність досвіду: 1.0 до типового досвіду користувача, далі пів бала за кожен
    /// «запас» у <see cref="ExperienceMarginMonths"/> місяців, 0 — якщо вакансія вимагає
    /// більш ніж два запаси понад типовий досвід.
    /// </summary>
    private static double ExperienceFit(int typicalExperience, int requiredExperience)
    {
        int excess = requiredExperience - typicalExperience;
        if (excess <= 0)
        {
            return 1.0;
        }

        if (excess <= ExperienceMarginMonths)
        {
            return 1.0 - 0.5 * excess / ExperienceMarginMonths;
        }

        if (excess <= 2 * ExperienceMarginMonths)
        {
            return 0.5 - 0.5 * (excess - ExperienceMarginMonths) / ExperienceMarginMonths;
        }

        return 0;
    }

    /// <summary>
    /// Перетин зарплатних очікувань: повний збіг, якщо діапазони перетинаються; інакше —
    /// наскільки ближня межа вакансії до очікуваного діапазону (0..1).
    /// </summary>
    private static double SalaryFit(SalaryPreference salary, VacancyDto vacancy)
    {
        decimal vacancyMin = vacancy.SalaryMin ?? vacancy.SalaryMax ?? 0;
        decimal vacancyMax = vacancy.SalaryMax ?? vacancy.SalaryMin ?? 0;
        decimal expectedMin = salary.MedianMin ?? salary.MedianMax ?? 0;
        decimal expectedMax = salary.MedianMax ?? salary.MedianMin ?? 0;

        if (vacancyMin > vacancyMax)
        {
            (vacancyMin, vacancyMax) = (vacancyMax, vacancyMin);
        }

        if (expectedMin > expectedMax)
        {
            (expectedMin, expectedMax) = (expectedMax, expectedMin);
        }

        if (vacancyMax >= expectedMin && vacancyMin <= expectedMax)
        {
            return 1.0;
        }

        if (vacancyMax < expectedMin)
        {
            return expectedMin <= 0 ? 1.0 : Math.Clamp((double)(vacancyMax / expectedMin), 0, 1);
        }

        return vacancyMin <= 0 ? 1.0 : Math.Clamp((double)(expectedMax / vacancyMin), 0, 1);
    }

    /// <summary>
    /// Пошук ваги з нечутливістю до регістру навіть для словників, створених без відповідного компаратора.
    /// </summary>
    private static bool TryGetWeight(IReadOnlyDictionary<string, double> weights, string key, out double weight)
    {
        if (weights.TryGetValue(key, out weight))
        {
            return true;
        }

        foreach ((string existing, double value) in weights)
        {
            if (string.Equals(existing, key, StringComparison.OrdinalIgnoreCase))
            {
                weight = value;
                return true;
            }
        }

        weight = 0;
        return false;
    }

    private static string DescribeWorkFormat(WorkFormat format) => format switch
    {
        WorkFormat.Remote => "віддалено",
        WorkFormat.Hybrid => "гібрид",
        WorkFormat.Office => "офіс",
        _ => "не вказано"
    };
}
