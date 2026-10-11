// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
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
    private readonly IVacancyRepository _vacancyRepository;
    private readonly ISavedVacancyRepository _savedVacancyRepository;
    private readonly IVacancyCacheService _cacheService;
    private readonly ILogger<VacancyService> _logger;

    public VacancyService(
        IVacancyRepository vacancyRepository,
        ISavedVacancyRepository savedVacancyRepository,
        IVacancyCacheService cacheService,
        ILogger<VacancyService> logger)
    {
        _vacancyRepository = vacancyRepository;
        _savedVacancyRepository = savedVacancyRepository;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<PagedResult<VacancyDto>> GetAllAsync(VacancyFilterParameters filter, CancellationToken ct = default)
    {
        PagedResult<VacancyDto>? cached = await _cacheService.GetPagedVacanciesAsync(filter, ct);
        if (cached != null)
        {
            _logger.LogDebug("Cache HIT для сторінки вакансій (Page={Page}, Size={PageSize})", filter.Page, filter.PageSize);
            return cached;
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
                location: null,
                schedule: null,
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
            HashSet<Guid> savedIds = await _savedVacancyRepository.GetSavedVacancyIdsAsync(pageIds, ct);

            List<VacancyDto> items = entities
                .Select(e => MapToDto(e, savedIds.Contains(e.Id)))
                .ToList();

            PagedResult<VacancyDto> result = new PagedResult<VacancyDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = filter.Page,
                PageSize = filter.PageSize
            };

            await _cacheService.SetPagedVacanciesAsync(filter, result, null, ct);
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

    public async Task<VacancyDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        VacancyDto? cached = await _cacheService.GetVacancyByIdAsync(id, ct);
        if (cached != null)
        {
            return cached;
        }

        VacancyDB? entity = await _vacancyRepository.GetByIdAsync(id, ct);
        if (entity == null)
        {
            return null;
        }

        bool isSaved = await _savedVacancyRepository.ExistsForVacancyAsync(id, ct);
        VacancyDto dto = MapToDto(entity, isSaved);

        await _cacheService.SetVacancyByIdAsync(id, dto, null, ct);
        return dto;
    }

    public async Task<VacancyDto> CreateAsync(CreateVacancyRequest request, CancellationToken ct = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        VacancyDB entity = new VacancyDB
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Company = request.Company.Trim(),
            Description = request.Description.Trim(),
            Category = request.Category.Trim(),
            Location = request.Location.Trim(),
            FullAddress = request.FullAddress?.Trim(),
            Schedule = request.Schedule?.Trim(),
            Experience = request.Experience?.Trim(),
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
            return null;
        }

        await _cacheService.InvalidateVacancyAsync(id, ct);

        bool isSaved = await _savedVacancyRepository.ExistsForVacancyAsync(id, ct);
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

    public static VacancyDto MapToDto(VacancyDB entity, bool isSaved = false)
    {
        return new VacancyDto
        {
            Id = entity.Id,
            Title = entity.Title,
            Company = entity.Company,
            Description = entity.Description,
            Category = entity.Category,
            Location = entity.Location,
            FullAddress = entity.FullAddress,
            Schedule = entity.Schedule,
            Experience = entity.Experience,
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

    private static JobSource ParseJobSource(string source)
    {
        return Enum.TryParse(source, true, out JobSource s) ? s : JobSource.Unknown;
    }

    private static IReadOnlyList<string> ParseSkills(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
