// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Analytics;

namespace Waterfall.Services;

/// <summary>
/// Обчислення 5 блоків ринкової аналітики з реальної бази вакансій без моків (FR-003, FR-008).
/// </summary>
public class AnalyticsService : IAnalyticsService
{
    public const int MinVacanciesForAnalytics = 5;
    private const int MaxVacancySample = 2000;
    private const int TopLimit = 5;

    private readonly IVacancyRepository _vacancyRepository;
    private readonly TimeProvider _time;

    public AnalyticsService(IVacancyRepository vacancyRepository, TimeProvider? time = null)
    {
        _vacancyRepository = vacancyRepository;
        _time = time ?? TimeProvider.System;
    }

    public async Task<MarketAnalyticsDto> GetMarketAnalyticsAsync(
        string? category = null,
        int months = 3,
        CancellationToken ct = default)
    {
        int effectiveMonths = Math.Clamp(months, 1, 12);
        DateTimeOffset now = _time.GetUtcNow();
        DateTimeOffset periodStart = now.AddMonths(-effectiveMonths);

        (IReadOnlyList<VacancyDB> allActive, _) = await _vacancyRepository.GetAllAsync(
            onlyActive: true,
            sortBy: "publishedAt",
            sortOrder: "desc",
            page: 1,
            pageSize: MaxVacancySample,
            ct: ct);

        List<string> availableCategories = allActive
            .Select(v => !string.IsNullOrWhiteSpace(v.CategoryGroup) ? v.CategoryGroup!.Trim() : v.Category?.Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? trimmedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();

        List<VacancyDB> inPeriod = allActive
            .Where(v => v.PublishedAt >= periodStart)
            .Where(v => trimmedCategory is null || MatchesCategory(v, trimmedCategory))
            .ToList();

        if (inPeriod.Count < MinVacanciesForAnalytics)
        {
            return new MarketAnalyticsDto
            {
                TotalVacancies = inPeriod.Count,
                HasEnoughData = false,
                AvailableCategories = availableCategories
            };
        }

        string? primaryCurrency = DetectPrimaryCurrency(inPeriod);
        int withSalaryCount = inPeriod.Count(v => !string.IsNullOrWhiteSpace(v.Currency) && (v.SalaryMin.HasValue || v.SalaryMax.HasValue));
        double salaryTransparency = RoundPercent(withSalaryCount, inPeriod.Count);

        int entryLevelCount = inPeriod.Count(v => v.ExperienceMonths is >= 0 and <= 12);
        double entryLevelPercent = RoundPercent(entryLevelCount, inPeriod.Count);

        return new MarketAnalyticsDto
        {
            TotalVacancies = inPeriod.Count,
            HasEnoughData = true,
            PrimaryCurrency = primaryCurrency,
            SalaryTransparencyPercent = salaryTransparency,
            EntryLevelCount = entryLevelCount,
            EntryLevelPercent = entryLevelPercent,
            SalariesByGrade = ComputeSalariesByGrade(inPeriod, primaryCurrency),
            TopPayingTechnologies = ComputeTopPayingTechnologies(inPeriod, primaryCurrency),
            WorkFormats = ComputeWorkFormats(inPeriod),
            TopCities = ComputeTopCities(inPeriod),
            Sources = ComputeSources(inPeriod),
            TopCompanies = ComputeTopCompanies(inPeriod),
            AvailableCategories = availableCategories
        };
    }

    private static bool MatchesCategory(VacancyDB vacancy, string category)
    {
        if (!string.IsNullOrWhiteSpace(vacancy.CategoryGroup)
            && string.Equals(vacancy.CategoryGroup.Trim(), category, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(vacancy.Category)
            && vacancy.Category.Contains(category, StringComparison.OrdinalIgnoreCase);
    }

    private static string? DetectPrimaryCurrency(IReadOnlyList<VacancyDB> vacancies)
    {
        return vacancies
            .Where(v => !string.IsNullOrWhiteSpace(v.Currency) && (v.SalaryMin.HasValue || v.SalaryMax.HasValue))
            .GroupBy(v => v.Currency!.Trim().ToUpperInvariant())
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .FirstOrDefault();
    }

    private static decimal? ExtractSalaryMidpoint(VacancyDB vacancy, string? currency)
    {
        if (currency is null
            || string.IsNullOrWhiteSpace(vacancy.Currency)
            || !string.Equals(vacancy.Currency.Trim(), currency, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        decimal value = (vacancy.SalaryMin, vacancy.SalaryMax) switch
        {
            (decimal min, decimal max) => (min + max) / 2m,
            (decimal min, null) => min,
            (null, decimal max) => max,
            _ => 0m
        };

        return value > 0 ? value : null;
    }

    private static IReadOnlyList<GradeSalaryDto> ComputeSalariesByGrade(IReadOnlyList<VacancyDB> vacancies, string? currency)
    {
        if (currency is null)
        {
            return [];
        }

        (string Label, Func<VacancyDB, bool> Predicate)[] grades =
        [
            ("Junior (<1 р.)", v => v.ExperienceMonths is >= 0 and < 12),
            ("Middle (1–3 р.)", v => v.ExperienceMonths is >= 12 and < 36),
            ("Senior (3+ р.)", v => v.ExperienceMonths is >= 36)
        ];

        List<GradeSalaryDto> result = [];
        foreach ((string label, Func<VacancyDB, bool> predicate) in grades)
        {
            List<decimal> salaries = vacancies
                .Where(predicate)
                .Select(v => ExtractSalaryMidpoint(v, currency))
                .Where(s => s.HasValue)
                .Select(s => s!.Value)
                .ToList();

            if (salaries.Count == 0)
            {
                continue;
            }

            result.Add(new GradeSalaryDto(
                label,
                Math.Round(salaries.Average(), 0, MidpointRounding.AwayFromZero),
                Math.Round(salaries.Min(), 0, MidpointRounding.AwayFromZero),
                Math.Round(salaries.Max(), 0, MidpointRounding.AwayFromZero),
                salaries.Count,
                currency));
        }

        return result;
    }

    private static IReadOnlyList<TechSalaryDto> ComputeTopPayingTechnologies(IReadOnlyList<VacancyDB> vacancies, string? currency)
    {
        if (currency is null)
        {
            return [];
        }

        Dictionary<string, List<decimal>> salariesBySkill = new(StringComparer.OrdinalIgnoreCase);
        foreach (VacancyDB vacancy in vacancies)
        {
            decimal? salary = ExtractSalaryMidpoint(vacancy, currency);
            if (salary is null)
            {
                continue;
            }

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (string skill in VacancyService.ParseSkills(vacancy.SkillsJson))
            {
                string trimmed = skill.Trim();
                if (!string.IsNullOrEmpty(trimmed) && seen.Add(trimmed))
                {
                    if (!salariesBySkill.TryGetValue(trimmed, out List<decimal>? list))
                    {
                        list = [];
                        salariesBySkill[trimmed] = list;
                    }
                    list.Add(salary.Value);
                }
            }
        }

        if (salariesBySkill.Count == 0)
        {
            return [];
        }

        int minSample = salariesBySkill.Values.Any(list => list.Count >= 2) ? 2 : 1;

        return salariesBySkill
            .Where(pair => pair.Value.Count >= minSample)
            .Select(pair => new TechSalaryDto(
                pair.Key,
                Math.Round(pair.Value.Average(), 0, MidpointRounding.AwayFromZero),
                pair.Value.Count,
                currency))
            .OrderByDescending(item => item.AverageSalary)
            .ThenByDescending(item => item.VacanciesCount)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(TopLimit)
            .ToList();
    }

    private static IReadOnlyList<CategoryShareDto> ComputeWorkFormats(IReadOnlyList<VacancyDB> vacancies)
    {
        int remote = vacancies.Count(v => v.WorkFormat.HasFlag(WorkFormat.Remote));
        int hybrid = vacancies.Count(v => v.WorkFormat.HasFlag(WorkFormat.Hybrid));
        int office = vacancies.Count(v => v.WorkFormat.HasFlag(WorkFormat.Office));
        int unknown = vacancies.Count(v => v.WorkFormat == WorkFormat.None);

        (string Name, int Count)[] raw =
        [
            ("Віддалено", remote),
            ("Гібрид", hybrid),
            ("Офіс", office),
            ("Не вказано", unknown)
        ];

        List<(string Name, int Count)> nonZero = raw.Where(r => r.Count > 0).ToList();
        int totalTags = nonZero.Sum(r => r.Count);
        if (totalTags == 0)
        {
            return [];
        }

        return nonZero
            .Select(r => new CategoryShareDto(r.Name, r.Count, RoundPercent(r.Count, totalTags)))
            .ToList();
    }

    private static IReadOnlyList<CategoryShareDto> ComputeTopCities(IReadOnlyList<VacancyDB> vacancies)
    {
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        foreach (VacancyDB vacancy in vacancies)
        {
            string city = NormalizeCity(vacancy.Location);
            if (!string.IsNullOrEmpty(city))
            {
                counts[city] = counts.GetValueOrDefault(city) + 1;
            }
        }

        return counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(TopLimit)
            .Select(pair => new CategoryShareDto(pair.Key, pair.Value, RoundPercent(pair.Value, vacancies.Count)))
            .ToList();
    }

    private static IReadOnlyList<CategoryShareDto> ComputeSources(IReadOnlyList<VacancyDB> vacancies)
    {
        return vacancies
            .Where(v => !string.IsNullOrWhiteSpace(v.Source))
            .GroupBy(v => v.Source.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new CategoryShareDto(g.Key, g.Count(), RoundPercent(g.Count(), vacancies.Count)))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<CategoryShareDto> ComputeTopCompanies(IReadOnlyList<VacancyDB> vacancies)
    {
        return vacancies
            .Where(v => !string.IsNullOrWhiteSpace(v.Company))
            .GroupBy(v => v.Company.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new CategoryShareDto(g.Key, g.Count(), RoundPercent(g.Count(), vacancies.Count)))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(TopLimit)
            .ToList();
    }

    private static string NormalizeCity(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return string.Empty;
        }

        string cleaned = location.Trim();
        int parenIndex = cleaned.IndexOf('(');
        if (parenIndex > 0)
        {
            cleaned = cleaned[..parenIndex].Trim();
        }

        int commaIndex = cleaned.IndexOf(',');
        if (commaIndex > 0)
        {
            cleaned = cleaned[..commaIndex].Trim();
        }

        return cleaned;
    }

    private static double RoundPercent(int part, int total) =>
        total <= 0 ? 0.0 : Math.Round((part * 100.0) / total, 1, MidpointRounding.AwayFromZero);
}
