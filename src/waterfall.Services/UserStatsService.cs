// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.Extensions.Logging;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Auth;
using Waterfall.Services.Models.Profile;

namespace Waterfall.Services;

/// <summary>
/// Сервіс особистої статистики активності користувача (FR-007, US-008).
/// Реалізує частину «статистика активності користувачів» компонента
/// «Analytics &amp; Statistics Service» (high-level-design.md §3.4).
/// Поведінкові ПД: читання і запис — лише для userId власника (THR-12);
/// текст пошукових запитів у логи не потрапляє (THR-08);
/// успішні події активності не логуються (THR-05).
/// </summary>
public class UserStatsService : IUserStatsService
{
    /// <summary>Розмір топ-списків технологій і категорій (відповідно до US-008, критерій 1).</summary>
    private const int TopListSize = 5;

    /// <summary>Кількість останніх запитів у відповіді статистики.</summary>
    private const int RecentSearchesCount = 10;

    /// <summary>Максимальна довжина записаного запиту (SearchQueryDB.QueryText.MaxLength).</summary>
    private const int MaxQueryLength = 300;

    /// <summary>Повторний перегляд тієї ж вакансії протягом цього інтервалу не записується.</summary>
    private static readonly TimeSpan ViewDedupWindow = TimeSpan.FromSeconds(60);

    private readonly ISavedVacancyRepository _savedVacancyRepository;
    private readonly ISearchQueryRepository _searchQueryRepository;
    private readonly IVacancyViewRepository _vacancyViewRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UserStatsService> _logger;

    public UserStatsService(
        ISavedVacancyRepository savedVacancyRepository,
        ISearchQueryRepository searchQueryRepository,
        IVacancyViewRepository vacancyViewRepository,
        IUserRepository userRepository,
        ILogger<UserStatsService> logger)
    {
        _savedVacancyRepository = savedVacancyRepository;
        _searchQueryRepository = searchQueryRepository;
        _vacancyViewRepository = vacancyViewRepository;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<UserStatsDto> GetStatsAsync(Guid userId, CancellationToken ct = default)
    {
        IReadOnlyList<SavedVacancyDB> saved = await _savedVacancyRepository.GetAllAsync(userId, onlyActive: true, ct);
        IReadOnlyList<VacancyViewDB> viewed = await _vacancyViewRepository.GetForUserAsync(userId, ct);

        Dictionary<string, int> technologies = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> categories = new(StringComparer.OrdinalIgnoreCase);
        TallyVacancies(saved.Select(s => s.Vacancy), technologies, categories);
        TallyVacancies(viewed.Select(v => v.Vacancy), technologies, categories);

        IReadOnlyList<SearchQueryDB> recent = await _searchQueryRepository.GetRecentAsync(userId, RecentSearchesCount, ct);

        return new UserStatsDto
        {
            SavedVacanciesCount = saved.Count,
            TotalSearches = await _searchQueryRepository.GetCountAsync(userId, ct),
            TopTechnologies = TakeTop(technologies),
            TopCategories = TakeTop(categories),
            RecentSearches = [.. recent.Select(q => q.QueryText)],
        };
    }

    public async Task<UserStatsRecordResult> RecordSearchAsync(Guid userId, string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return UserStatsRecordResult.Skipped;
        }

        if (!await IsJobSeekerAsync(userId, ct))
        {
            return UserStatsRecordResult.Forbidden;
        }

        string trimmed = query.Trim();
        if (trimmed.Length > MaxQueryLength)
        {
            trimmed = trimmed[..MaxQueryLength];
        }

        SearchQueryDB? latest = await _searchQueryRepository.GetLatestAsync(userId, ct);
        if (latest is not null && string.Equals(latest.QueryText, trimmed, StringComparison.Ordinal))
        {
            return UserStatsRecordResult.Skipped;
        }

        await _searchQueryRepository.AddAsync(new SearchQueryDB { UserId = userId, QueryText = trimmed }, ct);
        return UserStatsRecordResult.Recorded;
    }

    public async Task<UserStatsRecordResult> RecordVacancyViewAsync(Guid userId, Guid vacancyId, CancellationToken ct = default)
    {
        if (!await IsJobSeekerAsync(userId, ct))
        {
            return UserStatsRecordResult.Forbidden;
        }

        if (await _vacancyViewRepository.ExistsRecentAsync(userId, vacancyId, ViewDedupWindow, ct))
        {
            return UserStatsRecordResult.Skipped;
        }

        await _vacancyViewRepository.AddAsync(new VacancyViewDB { UserId = userId, VacancyId = vacancyId }, ct);
        return UserStatsRecordResult.Recorded;
    }

    public async Task<bool> ClearSearchHistoryAsync(Guid userId, CancellationToken ct = default)
    {
        bool deleted = await _searchQueryRepository.DeleteAllForUserAsync(userId, ct);
        if (deleted)
        {
            _logger.LogInformation("Очищено історію пошукових запитів користувача {UserId}", userId);
        }

        return deleted;
    }

    private async Task<bool> IsJobSeekerAsync(Guid userId, CancellationToken ct)
    {
        UserDB? user = await _userRepository.GetByIdAsync(userId, ct);
        return user is not null
            && Enum.TryParse(user.Role, ignoreCase: true, out UserRole role)
            && role == UserRole.JobSeeker;
    }

    private static void TallyVacancies(IEnumerable<VacancyDB?> vacancies, Dictionary<string, int> technologies, Dictionary<string, int> categories)
    {
        foreach (VacancyDB? vacancy in vacancies)
        {
            if (vacancy is null)
            {
                continue;
            }

            foreach (string skill in VacancyService.ParseSkills(vacancy.SkillsJson))
            {
                Increment(technologies, skill);
            }

            Increment(categories, vacancy.Category);
        }

        static void Increment(Dictionary<string, int> counts, string key)
        {
            key = key.Trim();
            if (key.Length > 0)
            {
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }
    }

    private static List<StatCountDto> TakeTop(Dictionary<string, int> counts)
    {
        return [.. counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(TopListSize)
            .Select(kv => new StatCountDto { Name = kv.Key, Count = kv.Value })];
    }
}
