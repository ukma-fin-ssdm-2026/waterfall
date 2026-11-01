// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Entities;
using Waterfall.Services.Models.Ai;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Analysis;

/// <summary>
/// Будує <see cref="PreferenceProfile"/> зі збережених вакансій користувача (FR-010).
/// Уся статистика — прості частоти й медіани без LLM, тому результат детермінований:
/// той самий набір збережених вакансій завжди дає той самий профіль.
/// </summary>
public static class PreferenceProfileBuilder
{
    /// <summary>Скільки ключових слів максимум потрапляє в профіль (решта — шум).</summary>
    public const int MaxKeywords = 12;

    /// <summary>У скільки разів тег вакансії важить більше за слово з назви чи опису.</summary>
    public const int TagVoteWeight = 2;

    /// <summary>
    /// Побудувати профіль переваг зі збережених вакансій.
    /// </summary>
    /// <param name="savedVacancies">Вакансії зі збереженого користувача (без null-елементів).</param>
    /// <returns>
    /// Профіль із <see cref="PreferenceProfile.BasisCount"/> = кількості вакансій;
    /// для порожнього набору — <see cref="PreferenceProfile.Empty"/>.
    /// </returns>
    public static PreferenceProfile Build(IEnumerable<VacancyDto> savedVacancies)
    {
        ArgumentNullException.ThrowIfNull(savedVacancies);

        List<VacancyDto> vacancies = savedVacancies
            .Where(vacancy => vacancy is not null)
            .ToList();

        if (vacancies.Count == 0)
        {
            return PreferenceProfile.Empty;
        }

        return new PreferenceProfile
        {
            BasisCount = vacancies.Count,
            CategoryWeights = BuildCategoryWeights(vacancies),
            WorkFormatWeights = BuildWorkFormatWeights(vacancies),
            MedianExperienceMonths = Median(vacancies
                .Where(vacancy => vacancy.ExperienceMonths is not null)
                .Select(vacancy => vacancy.ExperienceMonths!.Value)
                .ToList()),
            Salary = BuildSalary(vacancies),
            KeywordWeights = BuildKeywordWeights(vacancies)
        };
    }

    /// <summary>
    /// Частота широких категорій: частка збережених вакансій, у яких категорію вказано.
    /// Вакансії без категорії не збільшують знаменник, інакше частота штучно занижувалася б.
    /// </summary>
    private static IReadOnlyDictionary<string, double> BuildCategoryWeights(IReadOnlyList<VacancyDto> vacancies)
    {
        List<string> groups = vacancies
            .Select(vacancy => vacancy.CategoryGroup)
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Select(group => group!)
            .ToList();

        if (groups.Count == 0)
        {
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, double> weights = new(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, string> group in groups.GroupBy(group => group, StringComparer.OrdinalIgnoreCase))
        {
            weights[group.Key] = group.Count() / (double)groups.Count;
        }

        return weights
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Частота форматів роботи: частка збережених вакансій, де формат дозволений.
    /// Вакансія з кількома форматами додає голос кожному з них (сума ваг може перевищувати 1).
    /// </summary>
    private static IReadOnlyDictionary<WorkFormat, double> BuildWorkFormatWeights(IReadOnlyList<VacancyDto> vacancies)
    {
        List<VacancyDto> withFormat = vacancies
            .Where(vacancy => vacancy.WorkFormat != WorkFormat.None)
            .ToList();

        if (withFormat.Count == 0)
        {
            return new Dictionary<WorkFormat, double>();
        }

        Dictionary<WorkFormat, double> weights = [];
        foreach (WorkFormat format in new[] { WorkFormat.Remote, WorkFormat.Hybrid, WorkFormat.Office })
        {
            int count = withFormat.Count(vacancy => vacancy.WorkFormat.HasFlag(format));
            if (count > 0)
            {
                weights[format] = count / (double)withFormat.Count;
            }
        }

        return weights;
    }

    /// <summary>
    /// Медіана досвіду: непарна кількість — середній елемент, парна — середнє двох середніх,
    /// округлене до цілого (детерміновано, без залежності від порядку збережень).
    /// </summary>
    private static int? Median(IReadOnlyList<int> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        int[] sorted = values.OrderBy(value => value).ToArray();
        int middle = sorted.Length / 2;

        if (sorted.Length % 2 == 1)
        {
            return sorted[middle];
        }

        return (int)Math.Round((sorted[middle - 1] + sorted[middle]) / 2.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Типовий діапазон зарплати: беремо найпоширенішу валюту серед збережених вакансій із зарплатою,
    /// а вакансії в інших валютах ігноруємо (курси не вигадуємо). Межі — медіани мінімумів і максимумів.
    /// </summary>
    private static SalaryPreference? BuildSalary(IReadOnlyList<VacancyDto> vacancies)
    {
        List<VacancyDto> withSalary = vacancies
            .Where(vacancy => !string.IsNullOrWhiteSpace(vacancy.Currency)
                && (vacancy.SalaryMin is not null || vacancy.SalaryMax is not null))
            .ToList();

        if (withSalary.Count == 0)
        {
            return null;
        }

        IGrouping<string, VacancyDto>? chosen = withSalary
            .GroupBy(vacancy => vacancy.Currency.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .First();

        decimal? min = MedianDecimal(chosen
            .Where(vacancy => vacancy.SalaryMin is not null)
            .Select(vacancy => vacancy.SalaryMin!.Value)
            .ToList());

        decimal? max = MedianDecimal(chosen
            .Where(vacancy => vacancy.SalaryMax is not null)
            .Select(vacancy => vacancy.SalaryMax!.Value)
            .ToList());

        if (min is null && max is null)
        {
            return null;
        }

        // Вакансія з однією межею дає оцінку «точкою», а не половиною діапазону
        min ??= max;
        max ??= min;

        if (min > max)
        {
            (min, max) = (max, min);
        }

        return new SalaryPreference(chosen.Key, min, max);
    }

    private static decimal? MedianDecimal(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        decimal[] sorted = values.OrderBy(value => value).ToArray();
        int middle = sorted.Length / 2;

        if (sorted.Length % 2 == 1)
        {
            return sorted[middle];
        }

        return Math.Round((sorted[middle - 1] + sorted[middle]) / 2m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Ваги ключових слів: нормована частота токена (частка збережених вакансій, де він є),
    /// тег важить <see cref="TagVoteWeight"/> голосів. Беремо топ <see cref="MaxKeywords"/>
    /// за вагою, за рівної ваги — за алфавітом (детермінований порядок).
    /// </summary>
    private static IReadOnlyDictionary<string, double> BuildKeywordWeights(IReadOnlyList<VacancyDto> vacancies)
    {
        Dictionary<string, double> counts = new(StringComparer.Ordinal);

        foreach (VacancyDto vacancy in vacancies)
        {
            foreach (string token in VacancyTextTokenizer.Tokenize(vacancy.Title))
            {
                counts[token] = counts.GetValueOrDefault(token) + 1;
            }

            foreach (string token in VacancyTextTokenizer.Tokenize(vacancy.Description))
            {
                counts[token] = counts.GetValueOrDefault(token) + 1;
            }

            foreach (string skill in vacancy.Skills)
            {
                foreach (string token in VacancyTextTokenizer.Tokenize(skill))
                {
                    counts[token] = counts.GetValueOrDefault(token) + TagVoteWeight;
                }
            }
        }

        if (counts.Count == 0)
        {
            return new Dictionary<string, double>(StringComparer.Ordinal);
        }

        return counts
            .Select(pair => (Token: pair.Key, Weight: Math.Clamp(pair.Value / vacancies.Count, 0, 1)))
            .OrderByDescending(pair => pair.Weight)
            .ThenBy(pair => pair.Token, StringComparer.Ordinal)
            .Take(MaxKeywords)
            .ToDictionary(pair => pair.Token, pair => pair.Weight, StringComparer.Ordinal);
    }
}
