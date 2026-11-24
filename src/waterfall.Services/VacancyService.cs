// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: передача нормалізованих фільтрів у репозиторій і мапінг нових полів DTO; перевірено: тести VacancyServiceTests

using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Repository.Normalization;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Common;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services;

/// <summary>
/// Сервіс бізнес-логіки управління вакансіями.
/// Реалізує патерн In-Memory Cache-Aside кешування (PERF-001 &lt; 1 с), Stale-Cache режим (REL-001)
/// та мапінг між доменними сутностями БД (VacancyDB) і контрактами клієнтів (VacancyDto).
/// </summary>
public class VacancyService : IVacancyService
{
    private static readonly JsonSerializerOptions SkillsJsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly IVacancyRepository _vacancyRepository;
    private readonly ISavedVacancyRepository _savedVacancyRepository;
    private readonly IVacancyViewRepository? _vacancyViewRepository;
    private readonly IVacancyCacheService _cacheService;
    private readonly ILogger<VacancyService> _logger;

    public VacancyService(
        IVacancyRepository vacancyRepository,
        ISavedVacancyRepository savedVacancyRepository,
        IVacancyCacheService cacheService,
        ILogger<VacancyService> logger,
        IVacancyViewRepository? vacancyViewRepository = null)
    {
        _vacancyRepository = vacancyRepository;
        _savedVacancyRepository = savedVacancyRepository;
        _cacheService = cacheService;
        _logger = logger;
        _vacancyViewRepository = vacancyViewRepository;
    }

    public async Task<PagedResult<VacancyDto>> GetAllAsync(VacancyFilterParameters filter, CancellationToken ct = default, Guid? userId = null)
    {
        // Кеш спільний для всіх, а IsSaved — персональний: залогіненим віддаємо свіжі дані з їхніми флагами.
        if (userId is null)
        {
            PagedResult<VacancyDto>? cached = await _cacheService.GetPagedVacanciesAsync(filter, ct);
            if (cached != null)
            {
                _logger.LogDebug("Cache HIT для сторінки вакансій (Page={Page}, Size={PageSize})", filter.Page, filter.PageSize);
                return cached;
            }
        }

        _logger.LogDebug("Cache MISS для вакансій. Звернення до бази даних...");

        try
        {
            string? sourceFilter = filter.Source.HasValue && filter.Source.Value != JobSource.Unknown
                ? filter.Source.Value.ToString()
                : null;

            (IReadOnlyList<VacancyDB> entities, int totalCount) = await _vacancyRepository.GetAllAsync(
                search: filter.Search,
                category: filter.Category,
                experience: filter.Experience,
                location: filter.Location,
                workFormat: filter.WorkFormat,
                maxExperienceMonths: filter.MaxExperienceMonths,
                categoryGroup: filter.CategoryGroup,
                skills: filter.Skills,
                minSalary: filter.MinSalary,
                maxSalary: filter.MaxSalary,
                source: sourceFilter,
                onlyActive: filter.OnlyActive,
                sortBy: filter.SortBy,
                sortOrder: filter.SortOrder,
                page: filter.Page,
                pageSize: filter.PageSize,
                ct: ct);

            List<Guid> pageIds = entities.Select(e => e.Id).ToList();
            HashSet<Guid> savedIds = userId is null
                ? []
                : await _savedVacancyRepository.GetSavedVacancyIdsAsync(pageIds, userId.Value, ct);
            HashSet<Guid> viewedIds = userId is null || _vacancyViewRepository is null
                ? []
                : await _vacancyViewRepository.GetViewedVacancyIdsAsync(pageIds, userId.Value, ct);

            List<VacancyDto> items = entities
                .Select(e => MapToDto(e, savedIds.Contains(e.Id)) with { IsViewed = viewedIds.Contains(e.Id) })
                .ToList();

            PagedResult<VacancyDto> result = new PagedResult<VacancyDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = filter.Page,
                PageSize = filter.PageSize
            };

            // Персональні флаги в спільний кеш не кладемо, щоб не просвітити чуже «Обране».
            if (userId is null)
            {
                await _cacheService.SetPagedVacanciesAsync(filter, result, null, ct);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Помилка читання вакансій з БД. Спроба використати Stale-Cache...");

            PagedResult<VacancyDto>? stale = _cacheService.GetStaleCatalog();
            if (stale != null)
            {
                _logger.LogWarning("Повернуто застарілий кеш (Stale-Cache) через недоступність БД");
                return stale;
            }

            throw;
        }
    }

    public async Task<VacancyDto?> GetByIdAsync(Guid id, CancellationToken ct = default, Guid? userId = null)
    {
        if (userId is null)
        {
            VacancyDto? cached = await _cacheService.GetVacancyByIdAsync(id, ct);
            if (cached != null)
            {
                return cached;
            }
        }

        VacancyDB? entity = await _vacancyRepository.GetByIdAsync(id, ct);
        if (entity == null)
        {
            _logger.LogWarning("Вакансію з Id {VacancyId} не знайдено в базі даних", id);
            return null;
        }

        bool isSaved = userId is null
            ? false
            : await _savedVacancyRepository.ExistsForVacancyAsync(id, userId.Value, ct);
        bool isViewed = userId is not null && _vacancyViewRepository is not null
            && (await _vacancyViewRepository.GetViewedVacancyIdsAsync([id], userId.Value, ct)).Contains(id);
        VacancyDto dto = MapToDto(entity, isSaved) with { IsViewed = isViewed };

        if (userId is null)
        {
            await _cacheService.SetVacancyByIdAsync(id, dto, null, ct);
        }
        return dto;
    }

    public async Task<VacancyDto> CreateAsync(CreateVacancyRequest request, CancellationToken ct = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string description = request.Description.Trim();
        string? experience = request.Experience?.Trim();
        VacancyDB entity = new VacancyDB
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Company = request.Company.Trim(),
            Description = description,
            Category = request.Category.Trim(),
            Location = request.Location.Trim(),
            FullAddress = request.FullAddress?.Trim(),
            Schedule = request.Schedule?.Trim(),
            Experience = experience,
            // Досвід у місяцях виводимо з тексту: поля для нього в запиті немає, а фільтр каталогу працює з числом
            ExperienceMonths = VacancyExperienceParser.ParseMonths(experience, description),
            SalaryMin = request.SalaryMin,
            SalaryMax = request.SalaryMax,
            Currency = request.Currency.ToUpperInvariant(),
            Source = request.Source.ToString(),
            SourceUrl = request.SourceUrl.Trim(),
            SkillsJson = JsonSerializer.Serialize(request.Skills ?? Array.Empty<string>()),
            PublishedAt = now,
            FirstSeenAt = now,
            LastSeenAt = now,
            IsActive = true
        };

        VacancyDB created = await _vacancyRepository.CreateAsync(entity, ct);

        await _cacheService.InvalidateCatalogAsync(ct);

        return MapToDto(created, isSaved: false);
    }

    public async Task<VacancyDto?> UpdateAsync(Guid id, UpdateVacancyRequest request, CancellationToken ct = default)
    {
        VacancyDB? entity = await _vacancyRepository.GetByIdAsync(id, ct);
        if (entity == null)
        {
            _logger.LogWarning("Спроба оновити неіснуючу вакансію з Id {VacancyId}", id);
            return null;
        }

        if (request.Title != null) entity.Title = request.Title.Trim();
        if (request.Company != null) entity.Company = request.Company.Trim();
        if (request.Description != null) entity.Description = request.Description.Trim();
        if (request.Category != null) entity.Category = request.Category.Trim();
        if (request.Location != null) entity.Location = request.Location.Trim();
        if (request.FullAddress != null) entity.FullAddress = request.FullAddress.Trim();
        if (request.Schedule != null) entity.Schedule = request.Schedule.Trim();
        if (request.Experience != null) entity.Experience = request.Experience.Trim();
        if (request.Experience != null || request.Description != null)
        {
            // Оновлюємо похідне число разом із текстом, інакше фільтр за досвідом розійдеться з описом
            entity.ExperienceMonths = VacancyExperienceParser.ParseMonths(entity.Experience, entity.Description);
        }

        if (request.SalaryMin.HasValue) entity.SalaryMin = request.SalaryMin;
        if (request.SalaryMax.HasValue) entity.SalaryMax = request.SalaryMax;
        if (request.Currency != null) entity.Currency = request.Currency.ToUpperInvariant();
        if (request.Source.HasValue) entity.Source = request.Source.Value.ToString();
        if (request.SourceUrl != null) entity.SourceUrl = request.SourceUrl.Trim();
        if (request.Skills != null) entity.SkillsJson = JsonSerializer.Serialize(request.Skills);
        if (request.IsActive.HasValue) entity.IsActive = request.IsActive.Value;
        entity.LastSeenAt = DateTimeOffset.UtcNow;

        VacancyDB? updated = await _vacancyRepository.UpdateAsync(entity, ct);
        if (updated == null)
        {
            _logger.LogWarning("Не вдалося оновити вакансію з Id {VacancyId}: запис зник між читанням та записом (можлива гонка)", id);
            return null;
        }

        await _cacheService.InvalidateVacancyAsync(id, ct);

        bool isSaved = await _savedVacancyRepository.ExistsForVacancyAsync(id, null, ct);
        return MapToDto(updated, isSaved);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        bool deleted = await _vacancyRepository.DeleteAsync(id, ct);
        if (deleted)
        {
            await _cacheService.InvalidateVacancyAsync(id, ct);
        }

        return deleted;
    }

    public async Task<VacancySyncResponseDto> SyncVacanciesAsync(VacancySyncRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DateTimeOffset serverTime = DateTimeOffset.UtcNow;

        List<VacancyDB> sanitized = [];
        if (request.ClientVacancies is { Count: > 0 })
        {
            // Обмежуємо один пакет до 1000 записів (клієнт нарізає більші обсяги на батчі)
            foreach (VacancySyncItemDto item in request.ClientVacancies.Take(1000))
            {
                VacancyDB? entity = MapFromSyncItem(item, serverTime);
                if (entity is not null)
                {
                    sanitized.Add(entity);
                }
            }
        }

        VacancySyncMergeResult merge = await _vacancyRepository.SyncMergeAsync(sanitized, ct);
        if (merge.Created > 0 || merge.Updated > 0)
        {
            await _cacheService.InvalidateCatalogAsync(ct);
        }

        DateTimeOffset? since = request.FullSync ? null : request.LastSyncedAt;
        IReadOnlyList<VacancyDB> serverRows = await _vacancyRepository.GetAllForSyncAsync(since, merge.CanonicalIds, ct);
        int totalServer = since is null
            ? serverRows.Count
            : await _vacancyRepository.CountAllAsync(ct);

        _logger.LogInformation(
            "Синхронізація вакансій: отримано {Received}, створено {Created}, оновлено {Updated}, віддано клієнту {Returned} із {Total}.",
            sanitized.Count, merge.Created, merge.Updated, serverRows.Count, totalServer);

        return new VacancySyncResponseDto
        {
            ServerVacancies = serverRows.Select(MapToSyncItem).ToList(),
            ServerTime = serverTime,
            ReceivedFromClient = sanitized.Count,
            CreatedOnServer = merge.Created,
            UpdatedOnServer = merge.Updated,
            TotalServerVacancies = totalServer
        };
    }

    public static VacancyDto MapToDto(VacancyDB entity, bool isSaved = false)
    {
        return new VacancyDto
        {
            Id = entity.Id,
            ExternalId = entity.ExternalId,
            Title = entity.Title,
            Company = entity.Company,
            Description = entity.Description,
            Category = entity.Category,
            CategoryGroup = entity.CategoryGroup,
            Location = entity.Location,
            FullAddress = entity.FullAddress,
            Schedule = entity.Schedule,
            Experience = entity.Experience,
            ExperienceMonths = entity.ExperienceMonths,
            WorkFormat = entity.WorkFormat,
            SalaryMin = entity.SalaryMin,
            SalaryMax = entity.SalaryMax,
            Currency = entity.Currency,
            Source = ParseJobSource(entity.Source),
            SourceUrl = entity.SourceUrl,
            Skills = ParseSkills(entity.SkillsJson),
            PublishedAt = entity.PublishedAt,
            FirstSeenAt = entity.FirstSeenAt,
            LastSeenAt = entity.LastSeenAt,
            IsActive = entity.IsActive,
            IsSaved = isSaved
        };
    }

    public static VacancySyncItemDto MapToSyncItem(VacancyDB entity) => new()
    {
        Id = entity.Id,
        ExternalId = entity.ExternalId,
        Title = entity.Title,
        Company = entity.Company,
        Description = entity.Description,
        Category = entity.Category,
        CategoryGroup = entity.CategoryGroup,
        Location = entity.Location,
        FullAddress = entity.FullAddress,
        Schedule = entity.Schedule,
        Experience = entity.Experience,
        ExperienceMonths = entity.ExperienceMonths,
        WorkFormat = entity.WorkFormat,
        SalaryMin = entity.SalaryMin,
        SalaryMax = entity.SalaryMax,
        Currency = entity.Currency,
        Source = ParseJobSource(entity.Source),
        SourceUrl = entity.SourceUrl,
        Skills = ParseSkills(entity.SkillsJson),
        PublishedAt = entity.PublishedAt,
        FirstSeenAt = entity.FirstSeenAt,
        LastSeenAt = entity.LastSeenAt,
        IsActive = entity.IsActive
    };

    private static VacancyDB? MapFromSyncItem(VacancySyncItemDto? item, DateTimeOffset fallbackNow)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Title))
        {
            return null;
        }

        string title = Cut(item.Title.Trim(), 200);
        if (title.Length < 2)
        {
            return null;
        }

        string company = Cut(string.IsNullOrWhiteSpace(item.Company) ? "Не вказано" : item.Company.Trim(), 200);
        string description = string.IsNullOrWhiteSpace(item.Description) ? title : item.Description.Trim();
        string category = Cut(string.IsNullOrWhiteSpace(item.Category) ? "Інше" : item.Category.Trim(), 100);
        string location = Cut(string.IsNullOrWhiteSpace(item.Location) ? "Не вказано" : item.Location.Trim(), 200);
        string sourceUrl = Cut((item.SourceUrl ?? string.Empty).Trim(), 1000);
        string? experience = string.IsNullOrWhiteSpace(item.Experience) ? null : Cut(item.Experience.Trim(), 100);
        string currency = Cut(string.IsNullOrWhiteSpace(item.Currency) ? "UAH" : item.Currency.Trim().ToUpperInvariant(), 10);
        string? externalId = string.IsNullOrWhiteSpace(item.ExternalId) ? null : Cut(item.ExternalId.Trim(), 100);

        DateTimeOffset publishedAt = item.PublishedAt == default ? fallbackNow : item.PublishedAt;
        DateTimeOffset firstSeenAt = item.FirstSeenAt == default ? publishedAt : item.FirstSeenAt;
        DateTimeOffset lastSeenAt = item.LastSeenAt == default ? fallbackNow : item.LastSeenAt;

        IReadOnlyList<string> skills = (item.Skills ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToList();

        return new VacancyDB
        {
            Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id,
            ExternalId = externalId,
            Title = title,
            Company = company,
            Description = description,
            Category = category,
            CategoryGroup = item.CategoryGroup,
            Location = location,
            FullAddress = string.IsNullOrWhiteSpace(item.FullAddress) ? null : Cut(item.FullAddress.Trim(), 300),
            Schedule = string.IsNullOrWhiteSpace(item.Schedule) ? null : Cut(item.Schedule.Trim(), 100),
            Experience = experience,
            ExperienceMonths = item.ExperienceMonths ?? VacancyExperienceParser.ParseMonths(experience, description),
            WorkFormat = item.WorkFormat,
            SalaryMin = item.SalaryMin is >= 0 ? item.SalaryMin : null,
            SalaryMax = item.SalaryMax is >= 0 ? item.SalaryMax : null,
            Currency = currency,
            Source = item.Source.ToString(),
            SourceUrl = sourceUrl,
            SkillsJson = JsonSerializer.Serialize(skills, SkillsJsonOptions),
            PublishedAt = publishedAt,
            FirstSeenAt = firstSeenAt,
            LastSeenAt = lastSeenAt,
            IsActive = item.IsActive
        };
    }

    private static string Cut(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private static JobSource ParseJobSource(string source)
    {
        return Enum.TryParse(source, true, out JobSource s) ? s : JobSource.Unknown;
    }

    public static IReadOnlyList<string> ParseSkills(string? json, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Помилка десеріалізації JSON навичок вакансії: '{Json}'", json);
            return Array.Empty<string>();
        }
    }
}
