// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Repository.Models.Common;
using Waterfall.Repository.Models.Vacancies;

namespace Waterfall.Repository.Repositories;

/// <summary>
/// Повноцінний репозиторій вакансій на базі Entity Framework Core.
/// Працює як з SQLite, так і з MSSQL через абстракцію WaterfallDbContext.
/// </summary>
public class EfVacancyRepository : IVacancyRepository
{
    private readonly WaterfallDbContext _context;

    public EfVacancyRepository(WaterfallDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<VacancyDto>> GetAllAsync(VacancyFilterParameters filter, CancellationToken ct = default)
    {
        IQueryable<VacancyEntity> query = _context.Vacancies.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string term = filter.Search.Trim().ToLower();
            query = query.Where(v =>
                v.Title.ToLower().Contains(term) ||
                v.Company.ToLower().Contains(term) ||
                v.Description.ToLower().Contains(term) ||
                v.SkillsJson.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            string cat = filter.Category.Trim().ToLower();
            query = query.Where(v => v.Category.ToLower().Contains(cat));
        }

        if (!string.IsNullOrWhiteSpace(filter.Experience))
        {
            string exp = filter.Experience.Trim().ToLower();
            query = query.Where(v => v.Experience != null && v.Experience.ToLower().Contains(exp));
        }

        if (filter.MinSalary.HasValue)
        {
            decimal min = filter.MinSalary.Value;
            query = query.Where(v => v.SalaryMin >= min || v.SalaryMax >= min);
        }

        if (filter.MaxSalary.HasValue)
        {
            decimal max = filter.MaxSalary.Value;
            query = query.Where(v => v.SalaryMin <= max || (v.SalaryMax.HasValue && v.SalaryMax <= max));
        }

        if (filter.Source.HasValue)
        {
            string sourceStr = filter.Source.Value.ToString();
            query = query.Where(v => v.Source == sourceStr);
        }

        if (filter.OnlyActive)
        {
            query = query.Where(v => v.IsActive);
        }
        else
        {
            // Усі активні + не більше 1000 найновіших архівних вакансій за один запит
            IQueryable<VacancyEntity> activeQuery = query.Where(v => v.IsActive);
            IQueryable<VacancyEntity> archiveQuery = query
                .Where(v => !v.IsActive)
                .OrderByDescending(v => v.PublishedAt)
                .Take(1000);

            query = activeQuery.Concat(archiveQuery);
        }

        int totalCount = await query.CountAsync(ct);

        List<VacancyEntity> entities = await query
            .OrderByDescending(v => v.PublishedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        List<Guid> pageIds = entities.Select(e => e.Id).ToList();
        HashSet<Guid> savedIds = (await _context.SavedVacancies
            .AsNoTracking()
            .Where(s => pageIds.Contains(s.VacancyId))
            .Select(s => s.VacancyId)
            .ToListAsync(ct))
            .ToHashSet();

        List<VacancyDto> items = entities
            .Select(e => MapToDto(e, savedIds.Contains(e.Id)))
            .ToList();

        return new PagedResult<VacancyDto>(items, totalCount, filter.Page, filter.PageSize);
    }

    public async Task<VacancyDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        VacancyEntity? entity = await _context.Vacancies
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, ct);

        if (entity is null)
        {
            return null;
        }

        bool isSaved = await _context.SavedVacancies
            .AsNoTracking()
            .AnyAsync(s => s.VacancyId == id, ct);

        return MapToDto(entity, isSaved);
    }

    public async Task<VacancyDto> CreateAsync(CreateVacancyRequest request, CancellationToken ct = default)
    {
        Guid id = Guid.NewGuid();
        VacancyEntity entity = new VacancyEntity
        {
            Id = id,
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
            Currency = request.Currency.Trim().ToUpperInvariant(),
            Source = request.Source.ToString(),
            SourceUrl = request.SourceUrl.Trim(),
            SkillsJson = JsonSerializer.Serialize(request.Skills ?? []),
            PublishedAt = DateTimeOffset.UtcNow,
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

        _context.Vacancies.Add(entity);
        await _context.SaveChangesAsync(ct);

        return MapToDto(entity, isSaved: false);
    }

    public async Task<VacancyDto?> UpdateAsync(Guid id, UpdateVacancyRequest request, CancellationToken ct = default)
    {
        VacancyEntity? entity = await _context.Vacancies.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (entity is null)
        {
            return null;
        }

        entity.Title = request.Title.Trim();
        entity.Company = request.Company.Trim();
        entity.Description = request.Description.Trim();
        entity.Category = request.Category.Trim();
        entity.Location = request.Location.Trim();
        entity.FullAddress = request.FullAddress?.Trim() ?? entity.FullAddress;
        entity.Schedule = request.Schedule?.Trim() ?? entity.Schedule;
        entity.Experience = request.Experience?.Trim() ?? entity.Experience;
        entity.SalaryMin = request.SalaryMin;
        entity.SalaryMax = request.SalaryMax;
        entity.Currency = request.Currency.Trim().ToUpperInvariant();
        entity.SkillsJson = JsonSerializer.Serialize(request.Skills ?? []);
        entity.IsActive = request.IsActive;
        entity.LastSeenAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(ct);

        bool isSaved = await _context.SavedVacancies
            .AsNoTracking()
            .AnyAsync(s => s.VacancyId == id, ct);

        return MapToDto(entity, isSaved);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        VacancyEntity? entity = await _context.Vacancies.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (entity is null)
        {
            return false;
        }

        // За інваріантом моделі даних (docs/architecture/data-model.md §6 Інваріант 4):
        // закрита вакансія не видаляється фізично, а отримує is_active = false
        entity.IsActive = false;
        entity.LastSeenAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(ct);
        return true;
    }

    public static VacancyDto MapToDto(VacancyEntity entity, bool isSaved = false)
    {
        if (!Enum.TryParse(entity.Source, ignoreCase: true, out JobSource source))
        {
            source = JobSource.Unknown;
        }

        List<string> skills = new();

        if (!string.IsNullOrWhiteSpace(entity.SkillsJson))
        {
            try
            {
                skills = JsonSerializer.Deserialize<List<string>>(entity.SkillsJson) ?? [];
            }
            catch
            {
                skills = [];
            }
        }

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
            Source = source,
            SourceUrl = entity.SourceUrl,
            Skills = skills,
            PublishedAt = entity.PublishedAt,
            IsActive = entity.IsActive,
            IsSaved = isSaved //можливо варто буде не шукати по ІД, а зберігати це в SavedVacancy теж
        };
    }
}
