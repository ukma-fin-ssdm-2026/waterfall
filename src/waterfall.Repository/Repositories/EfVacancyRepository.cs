// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: метод BulkUpsertAsync для фонового збору вакансій (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests на SQLite, самоперевірка автора (§8)

using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;

namespace Waterfall.Repository.Repositories;

/// <summary>
/// Репозиторій доступу до таблиці вакансій на базі Entity Framework Core.
/// Працює виключно із сутностями VacancyDB згідно з Clean Architecture.
/// </summary>
public class EfVacancyRepository : IVacancyRepository
{
    private readonly WaterfallDbContext _context;

    public EfVacancyRepository(WaterfallDbContext context)
    {
        _context = context;
    }

    public async Task<(IReadOnlyList<VacancyDB> Items, int TotalCount)> GetAllAsync(
        string? search = null,
        string? category = null,
        string? experience = null,
        string? location = null,
        string? schedule = null,
        decimal? minSalary = null,
        decimal? maxSalary = null,
        string? source = null,
        bool onlyActive = true,
        string? sortBy = null,
        string? sortOrder = null,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default)
    {
        IQueryable<VacancyDB> query = _context.Vacancies.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Той самий регістр, що й у lower() на боці БД (SqliteUnicodeLowerInterceptor)
            string term = search.Trim().ToLowerInvariant();
            query = query.Where(v =>
                v.Title.ToLower().Contains(term) ||
                v.Company.ToLower().Contains(term) ||
                v.Description.ToLower().Contains(term) ||
                v.SkillsJson.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            string cat = category.Trim().ToLowerInvariant();
            query = query.Where(v => v.Category.ToLower().Contains(cat));
        }

        if (!string.IsNullOrWhiteSpace(experience))
        {
            string exp = experience.Trim().ToLowerInvariant();
            query = query.Where(v => v.Experience != null && v.Experience.ToLower().Contains(exp));
        }

        if (!string.IsNullOrWhiteSpace(location))
        {
            string loc = location.Trim().ToLowerInvariant();
            query = query.Where(v => v.Location.ToLower().Contains(loc));
        }

        if (!string.IsNullOrWhiteSpace(schedule))
        {
            string sch = schedule.Trim().ToLowerInvariant();
            query = query.Where(v => v.Schedule != null && v.Schedule.ToLower().Contains(sch));
        }

        if (minSalary.HasValue)
        {
            query = query.Where(v => v.SalaryMin >= minSalary.Value || v.SalaryMax >= minSalary.Value);
        }

        if (maxSalary.HasValue)
        {
            query = query.Where(v => v.SalaryMax <= maxSalary.Value || v.SalaryMin <= maxSalary.Value);
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            query = query.Where(v => v.Source == source);
        }

        if (onlyActive)
        {
            query = query.Where(v => v.IsActive);
        }
        else
        {
            IQueryable<VacancyDB> activeQuery = query.Where(v => v.IsActive);
            IQueryable<VacancyDB> archiveQuery = query
                .Where(v => !v.IsActive)
                .OrderByDescending(v => v.PublishedAt)
                .Take(1000);

            query = activeQuery.Concat(archiveQuery);
        }

        int totalCount = await query.CountAsync(ct);

        bool desc = !string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase);
        query = (sortBy?.ToLower()) switch
        {
            "title" => desc ? query.OrderByDescending(v => v.Title) : query.OrderBy(v => v.Title),
            "salarymin" => desc ? query.OrderByDescending(v => v.SalaryMin) : query.OrderBy(v => v.SalaryMin),
            "salarymax" => desc ? query.OrderByDescending(v => v.SalaryMax) : query.OrderBy(v => v.SalaryMax),
            "firstseenat" => desc ? query.OrderByDescending(v => v.FirstSeenAt) : query.OrderBy(v => v.FirstSeenAt),
            _ => desc ? query.OrderByDescending(v => v.PublishedAt) : query.OrderBy(v => v.PublishedAt)
        };

        int offset = (page - 1) * pageSize;
        List<VacancyDB> items = await query
            .Skip(offset)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<VacancyDB?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Vacancies
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, ct);
    }

    public async Task<(int Created, int Updated)> BulkUpsertAsync(IReadOnlyList<VacancyDB> vacancies, CancellationToken ct = default)
    {
        // Уже збережені вакансії шукаємо одним запитом, а не по одній
        List<string?> externalIds = vacancies.Select(v => v.ExternalId).ToList();
        List<VacancyDB> existing = await _context.Vacancies
            .Where(v => externalIds.Contains(v.ExternalId))
            .ToListAsync(ct);

        int created = 0;
        int updated = 0;
        foreach (VacancyDB vacancy in vacancies)
        {
            VacancyDB? current = existing.FirstOrDefault(v => v.Source == vacancy.Source && v.ExternalId == vacancy.ExternalId);
            if (current == null)
            {
                _context.Vacancies.Add(vacancy);
                existing.Add(vacancy);
                created++;
                continue;
            }

            vacancy.Id = current.Id;
            vacancy.PublishedAt = current.PublishedAt;
            vacancy.FirstSeenAt = current.FirstSeenAt;
            _context.Entry(current).CurrentValues.SetValues(vacancy);
            updated++;
        }

        await _context.SaveChangesAsync(ct);
        return (created, updated);
    }

    public async Task<VacancyDB> CreateAsync(VacancyDB entity, CancellationToken ct = default)
    {
        _context.Vacancies.Add(entity);
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<VacancyDB?> UpdateAsync(VacancyDB entity, CancellationToken ct = default)
    {
        VacancyDB? existing = await _context.Vacancies.FirstOrDefaultAsync(v => v.Id == entity.Id, ct);
        if (existing == null)
        {
            return null;
        }

        _context.Entry(existing).CurrentValues.SetValues(entity);
        await _context.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        VacancyDB? entity = await _context.Vacancies.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (entity == null)
        {
            return false;
        }

        _context.Vacancies.Remove(entity);
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
