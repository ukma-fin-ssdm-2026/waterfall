// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.ViewModels;
using Waterfall.Repository.Normalization;

namespace Waterfall.Client.Services;

/// <summary>
/// Спільний рушій локальної фільтрації, крос-валютного порівняння зарплат, сортування,
/// передрозрахунку відстаней та керування списками слів для <c>CatalogViewModel</c> і <c>DatabaseViewModel</c>.
/// </summary>
public static class VacancyFilterEngine
{
    public static bool IsDistanceSort(string? sortOption) =>
        sortOption is "За відстанню (спочатку ближчі)" or "За відстанню (спочатку дальші)";

    public static decimal GetSalaryInUah(VacancyDto v)
    {
        decimal raw = v.SalaryMax ?? v.SalaryMin ?? 0m;
        if (raw <= 0m) return 0m;
        if (v.Currency.Contains("USD", StringComparison.OrdinalIgnoreCase) || v.Currency.Contains('$')) return raw * 41.5m;
        if (v.Currency.Contains("EUR", StringComparison.OrdinalIgnoreCase) || v.Currency.Contains('€')) return raw * 45.0m;
        return raw;
    }

    public static decimal ConvertSalary(VacancyDto v, string targetCurrency)
    {
        decimal uah = GetSalaryInUah(v);
        if (uah <= 0m) return 0m;
        if (targetCurrency.Contains('$') || targetCurrency.Contains("USD", StringComparison.OrdinalIgnoreCase)) return uah / 41.5m;
        if (targetCurrency.Contains('€') || targetCurrency.Contains("EUR", StringComparison.OrdinalIgnoreCase)) return uah / 45.0m;
        return uah;
    }

    public static bool MatchesSalary(
        VacancyDto v,
        bool onlyWithSalary,
        string? minSalaryText,
        string? maxSalaryText,
        string targetCurrency,
        bool includeNoSalary)
    {
        bool hasSalary = v.SalaryMin.HasValue || v.SalaryMax.HasValue;
        if (onlyWithSalary && !hasSalary)
        {
            return false;
        }

        bool hasMin = decimal.TryParse(minSalaryText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal minVal) && minVal > 0;
        bool hasMax = decimal.TryParse(maxSalaryText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal maxVal) && maxVal > 0;
        if (!hasMin && !hasMax)
        {
            return true;
        }

        if (!hasSalary)
        {
            return includeNoSalary;
        }

        decimal salary = ConvertSalary(v, targetCurrency);
        if (hasMin && salary < minVal) return false;
        if (hasMax && salary > maxVal) return false;
        return true;
    }

    public static bool MatchesWordLists(
        VacancyDto v,
        IReadOnlyCollection<string> whiteList,
        bool whiteListMatchAll,
        IReadOnlyCollection<string> banList,
        bool titleOnly,
        bool includeLocationFields = false)
    {
        if (whiteList.Count > 0)
        {
            bool ok = whiteListMatchAll
                ? whiteList.All(w => ContainsWord(v, w, titleOnly, includeLocationFields))
                : whiteList.Any(w => ContainsWord(v, w, titleOnly, includeLocationFields));
            if (!ok) return false;
        }

        if (banList.Count > 0 && banList.Any(w => ContainsWord(v, w, titleOnly: false, includeLocationFields)))
        {
            return false;
        }

        return true;
    }

    public static bool ContainsAllWords(VacancyDto v, string query, bool titleOnly, bool includeLocationFields = false)
    {
        string[] words = query.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.All(w => ContainsWord(v, w, titleOnly, includeLocationFields));
    }

    public static bool ContainsWord(VacancyDto v, string word, bool titleOnly, bool includeLocationFields = false)
    {
        if (string.IsNullOrWhiteSpace(word)) return true;
        if (v.Title.Contains(word, StringComparison.OrdinalIgnoreCase)) return true;
        if (titleOnly) return false;

        if (v.Skills.Any(s => s.Contains(word, StringComparison.OrdinalIgnoreCase))
            || v.Company.Contains(word, StringComparison.OrdinalIgnoreCase)
            || v.Category.Contains(word, StringComparison.OrdinalIgnoreCase)
            || v.Description.Contains(word, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return includeLocationFields
            && ((v.CategoryGroup?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false)
                || v.Location.Contains(word, StringComparison.OrdinalIgnoreCase)
                || (v.FullAddress?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    public static IEnumerable<VacancyDto> SortVacancies(
        IEnumerable<VacancyDto> items,
        string sortOption,
        Func<VacancyDto, double?> getDistanceKm)
    {
        return sortOption switch
        {
            "За датою (спочатку старі)" or "За датою публікації (спочатку старі)" => items
                .OrderBy(v => v.PublishedAt)
                .ThenBy(v => v.FirstSeenAt),
            "За датою додавання в БД (спочатку нові)" => items
                .OrderByDescending(v => v.FirstSeenAt)
                .ThenByDescending(v => v.LastSeenAt),
            "За зарплатою (від найвищої ↓)" => items
                .OrderByDescending(v => v.SalaryMin.HasValue || v.SalaryMax.HasValue)
                .ThenByDescending(GetSalaryInUah)
                .ThenByDescending(v => v.PublishedAt),
            "За зарплатою (від найнижчої ↑)" => items
                .OrderByDescending(v => v.SalaryMin.HasValue || v.SalaryMax.HasValue)
                .ThenBy(GetSalaryInUah)
                .ThenByDescending(v => v.PublishedAt),
            "За досвідом (від меншого ↑)" => items
                .OrderByDescending(v => EffectiveExperienceMonths(v).HasValue)
                .ThenBy(v => EffectiveExperienceMonths(v) ?? int.MaxValue)
                .ThenByDescending(v => v.PublishedAt),
            "За досвідом (від більшого ↓)" => items
                .OrderByDescending(v => EffectiveExperienceMonths(v).HasValue)
                .ThenByDescending(v => EffectiveExperienceMonths(v) ?? int.MinValue)
                .ThenByDescending(v => v.PublishedAt),
            "За відстанню (спочатку ближчі)" => items
                .OrderByDescending(v => getDistanceKm(v).HasValue)
                .ThenBy(v => getDistanceKm(v) ?? double.MaxValue)
                .ThenByDescending(v => v.PublishedAt),
            "За відстанню (спочатку дальші)" => items
                .OrderByDescending(v => getDistanceKm(v).HasValue)
                .ThenByDescending(v => getDistanceKm(v) ?? double.MinValue)
                .ThenByDescending(v => v.PublishedAt),
            "За назвою посади (А → Я)" => items
                .OrderBy(v => v.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(v => v.PublishedAt),
            "За назвою посади (Я → А)" => items
                .OrderByDescending(v => v.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(v => v.PublishedAt),
            "За компанією (А → Я)" => items
                .OrderBy(v => v.Company, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(v => v.Title, StringComparer.CurrentCultureIgnoreCase),
            "За категорією (А → Я)" => items
                .OrderBy(v => v.CategoryGroup ?? VacancyCategoryMap.Resolve(v.Category), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(v => v.Category, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(v => v.PublishedAt),
            _ => items
                .OrderByDescending(v => v.PublishedAt)
                .ThenByDescending(v => v.LastSeenAt)
        };
    }

    public static int? EffectiveExperienceMonths(VacancyDto v) =>
        v.ExperienceMonths ?? VacancyExperienceParser.ParseMonths(v.Experience, v.Description);

    public static async Task PrecalculateDistancesAsync(
        IEnumerable<VacancyDto> items,
        IDistanceCalculatorService? distanceService,
        ConcurrentDictionary<Guid, double?> distanceCache,
        CancellationToken ct)
    {
        if (distanceService is null || string.IsNullOrWhiteSpace(distanceService.UserAddress))
        {
            return;
        }

        List<Task> tasks = [];
        foreach (VacancyDto v in items)
        {
            if (distanceCache.ContainsKey(v.Id))
            {
                continue;
            }

            string target = !string.IsNullOrWhiteSpace(v.FullAddress) ? v.FullAddress : v.Location;
            if (string.IsNullOrWhiteSpace(target)
                || target.Contains("Remote", StringComparison.OrdinalIgnoreCase)
                || target.Contains("Віддалено", StringComparison.OrdinalIgnoreCase)
                || target.Contains("не вказано точну адресу", StringComparison.OrdinalIgnoreCase))
            {
                distanceCache[v.Id] = null;
                continue;
            }

            tasks.Add(CalculateAndCacheDistanceAsync(v.Id, target, distanceService, distanceCache, ct));
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }
    }

    public static async Task PrefetchDistancesAsync(
        IReadOnlyList<VacancyItemViewModel> cards,
        IDistanceCalculatorService? distanceService,
        CancellationToken ct,
        int maxCount = 20,
        int initialDelayMs = 600,
        int interCardDelayMs = 100)
    {
        if (distanceService is null || string.IsNullOrWhiteSpace(distanceService.UserAddress) || cards.Count == 0)
        {
            return;
        }

        try
        {
            if (initialDelayMs > 0)
            {
                await Task.Delay(initialDelayMs, ct);
            }

            int limit = Math.Min(cards.Count, maxCount);
            for (int i = 0; i < limit; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (!cards[i].CanShowOnMap)
                {
                    continue;
                }

                await cards[i].EnsureDistanceLoadedAsync(ct);

                if (i < limit - 1 && interCardDelayMs > 0)
                {
                    await Task.Delay(interCardDelayMs, ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task CalculateAndCacheDistanceAsync(
        Guid id,
        string target,
        IDistanceCalculatorService distanceService,
        ConcurrentDictionary<Guid, double?> distanceCache,
        CancellationToken ct)
    {
        try
        {
            distanceCache[id] = await distanceService.CalculateDistanceKmAsync(target, ct);
        }
        catch
        {
            distanceCache[id] = null;
        }
    }

    public static bool TryAddWords(string? input, ObservableCollection<string> target)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        bool added = false;
        string[] parts = input.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string part in parts)
        {
            if (!target.Any(existing => string.Equals(existing, part, StringComparison.OrdinalIgnoreCase)))
            {
                target.Add(part);
                added = true;
            }
        }

        return added;
    }
}
