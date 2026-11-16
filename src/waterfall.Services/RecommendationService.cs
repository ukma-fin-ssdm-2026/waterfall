// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services.Analysis;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Ai;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services;

/// <summary>
/// Сервіс персоналізованих рекомендацій вакансій (FR-006, US-004).
/// Будує профіль переваг із «Збережених» вакансій та історії пошукових запитів,
/// оцінює активні вакансії каталогу через <see cref="RelevanceScorer"/> і виключає вже збережені.
/// </summary>
public class RecommendationService : IRecommendationService
{
    private const int CandidatePoolSize = 200;
    private const int RecentSearchesLimit = 10;
    private const int MaxBasisKeywords = 5;
    private const int MaxBasisSearches = 3;

    private readonly ISavedVacancyRepository _savedVacancyRepository;
    private readonly ISearchQueryRepository _searchQueryRepository;
    private readonly IVacancyRepository _vacancyRepository;

    public RecommendationService(
        ISavedVacancyRepository savedVacancyRepository,
        ISearchQueryRepository searchQueryRepository,
        IVacancyRepository vacancyRepository)
    {
        _savedVacancyRepository = savedVacancyRepository;
        _searchQueryRepository = searchQueryRepository;
        _vacancyRepository = vacancyRepository;
    }

    public async Task<RecommendationsResultDto> GetRecommendationsAsync(
        Guid userId,
        int limit = 20,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("Ідентифікатор користувача не може бути порожнім (Guid.Empty).", nameof(userId));
        }

        int effectiveLimit = Math.Clamp(limit, 1, 100);

        IReadOnlyList<SavedVacancyDB> saved = await _savedVacancyRepository.GetAllAsync(userId, onlyActive: true, ct);
        if (saved.Count == 0)
        {
            return new RecommendationsResultDto
            {
                SavedCount = 0,
                Items = [],
                BasisKeywords = [],
                BasisSearches = []
            };
        }

        HashSet<Guid> savedIds = saved.Select(s => s.VacancyId).ToHashSet();
        List<VacancyDto> savedDtos = saved
            .Where(s => s.Vacancy is not null)
            .Select(s => VacancyService.MapToDto(s.Vacancy!, isSaved: true))
            .ToList();

        PreferenceProfile baseProfile = PreferenceProfileBuilder.Build(savedDtos);

        IReadOnlyList<SearchQueryDB> recentSearchEntities = await _searchQueryRepository.GetRecentAsync(userId, RecentSearchesLimit, ct);
        List<string> recentSearches = recentSearchEntities
            .Select(q => q.QueryText.Trim())
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        PreferenceProfile enrichedProfile = EnrichProfileWithSearches(baseProfile, recentSearches);

        (IReadOnlyList<VacancyDB> candidates, _) = await _vacancyRepository.GetAllAsync(
            onlyActive: true,
            sortBy: "publishedAt",
            sortOrder: "desc",
            page: 1,
            pageSize: CandidatePoolSize,
            ct: ct);

        List<VacancyDto> recommended = candidates
            .Where(entity => !savedIds.Contains(entity.Id))
            .Select(entity => VacancyService.MapToDto(entity, isSaved: false))
            .Select(dto => (Dto: dto, Relevance: RelevanceScorer.Score(enrichedProfile, dto)))
            .Where(scored => scored.Relevance.Score is > 0)
            .OrderByDescending(scored => scored.Relevance.Score!.Value)
            .ThenByDescending(scored => scored.Dto.PublishedAt)
            .Take(effectiveLimit)
            .Select(scored => scored.Dto)
            .ToList();

        return new RecommendationsResultDto
        {
            Items = recommended,
            SavedCount = saved.Count,
            BasisKeywords = ExtractBasisKeywords(baseProfile, savedDtos),
            BasisSearches = recentSearches.Take(MaxBasisSearches).ToList()
        };
    }

    private static PreferenceProfile EnrichProfileWithSearches(
        PreferenceProfile profile,
        IReadOnlyList<string> recentSearches)
    {
        if (recentSearches.Count == 0)
        {
            return profile;
        }

        Dictionary<string, double> mergedKeywords = new(profile.KeywordWeights, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < recentSearches.Count; i++)
        {
            double searchWeight = Math.Max(0.2, 0.6 - (i * 0.05));
            foreach (string token in VacancyTextTokenizer.Tokenize(recentSearches[i]))
            {
                if (mergedKeywords.TryGetValue(token, out double existing))
                {
                    mergedKeywords[token] = Math.Min(1.0, existing + (searchWeight * 0.5));
                }
                else
                {
                    mergedKeywords[token] = searchWeight;
                }
            }
        }

        return new PreferenceProfile
        {
            BasisCount = profile.BasisCount,
            CategoryWeights = profile.CategoryWeights,
            WorkFormatWeights = profile.WorkFormatWeights,
            MedianExperienceMonths = profile.MedianExperienceMonths,
            Salary = profile.Salary,
            KeywordWeights = mergedKeywords
        };
    }

    private static IReadOnlyList<string> ExtractBasisKeywords(
        PreferenceProfile profile,
        IReadOnlyList<VacancyDto> savedDtos)
    {
        if (profile.KeywordWeights.Count == 0)
        {
            return [];
        }

        Dictionary<string, string> displayNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (string skill in savedDtos.SelectMany(v => v.Skills))
        {
            if (!string.IsNullOrWhiteSpace(skill) && !displayNames.ContainsKey(skill))
            {
                displayNames[skill] = skill.Trim();
            }
        }

        return profile.KeywordWeights
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(MaxBasisKeywords)
            .Select(pair => displayNames.TryGetValue(pair.Key, out string? display) ? display : pair.Key)
            .ToList();
    }
}
