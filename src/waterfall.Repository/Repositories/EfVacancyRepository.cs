// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: метод BulkUpsertAsync для фонового збору вакансій (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests на SQLite, самоперевірка автора (§8)
// AI: пошук по полях вакансії і фільтри формату роботи, досвіду в місяцях, широкої категорії та навичок; перевірено: тести EfVacancyRepositoryTests на SQLite
// AI: метод GetBySourceIdsAsync для пошуку на сайтах на запит (issue #62); перевірено: тест EfVacancyRepositoryTests на SQLite, самоперевірка автора (§8)

using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Helpers;
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
        WorkFormat? workFormat = null,
        int? maxExperienceMonths = null,
        string? categoryGroup = null,
        IReadOnlyList<string>? skills = null,
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
            // SqliteUnicodeLowerInterceptor підміняє lower() на SQLite, тож ToLower() без урахування регістру працює і для кирилиці
            string term = search.Trim().ToLowerInvariant();
            query = query.Where(v => v.Title.ToLower().Contains(term) || v.Company.ToLower().Contains(term) || v.Description.ToLower().Contains(term) || v.SkillsJson.ToLower().Contains(term));
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

        if (workFormat is { } formats && formats != WorkFormat.None)
        {
            // Прапорці: вакансія підходить, якщо дозволяє будь-який із вибраних форматів
            int mask = (int)formats;
            query = query.Where(v => ((int)v.WorkFormat & mask) != 0);
        }

        if (maxExperienceMonths is { } maxMonths)
        {
            // Верхня межа включна, а невідомий досвід не відсіюємо: «досвід не вказано» — не привід ховати вакансію
            query = query.Where(v => v.ExperienceMonths == null || v.ExperienceMonths <= maxMonths);
        }

        if (!string.IsNullOrWhiteSpace(categoryGroup))
        {
            string group = categoryGroup.Trim();
            query = query.Where(v => v.CategoryGroup == group);
        }

        List<string> skillTerms = skills is { Count: > 0 }
            ? skills.Where(skill => !string.IsNullOrWhiteSpace(skill)).Select(skill => skill.Trim()).ToList()
            : [];

        if (skillTerms.Count > 0)
        {
            // Попередній SQL-фільтр звужує вибірку, а точні межі слів і теги перевіряються нижче через VacancyWordMatcher
            foreach (string skill in skillTerms)
            {
                string term = skill.ToLowerInvariant();
                query = query.Where(v => v.SkillsJson.ToLower().Contains(term) || v.Title.ToLower().Contains(term) || v.Company.ToLower().Contains(term) || v.Description.ToLower().Contains(term));
            }
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
        if (skillTerms.Count > 0)
        {
            List<VacancyDB> candidates = await query.ToListAsync(ct);
            List<VacancyDB> matched = candidates
                .Where(v => skillTerms.All(term =>
                    VacancyWordMatcher.MatchesVacancySkill(v.SkillsJson, v.Title, v.Company, v.Description, term)))
                .ToList();
            return (matched.Skip(offset).Take(pageSize).ToList(), matched.Count);
        }

        int totalCount = await query.CountAsync(ct);
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

    public async Task<IReadOnlyList<VacancyDB>> GetBySourceIdsAsync(string source, IReadOnlyCollection<string> externalIds, CancellationToken ct = default)
    {
        if (externalIds.Count == 0)
        {
            return [];
        }

        if (externalIds.Count <= VacancyCandidateLookup.CandidateChunkSize)
        {
            return await _context.Vacancies
                .AsNoTracking()
                .Where(v => v.Source == source && v.ExternalId != null && externalIds.Contains(v.ExternalId))
                .ToListAsync(ct);
        }

        List<VacancyDB> results = [];
        foreach (string[] chunk in externalIds.Distinct(StringComparer.Ordinal).Chunk(VacancyCandidateLookup.CandidateChunkSize))
        {
            List<VacancyDB> rows = await _context.Vacancies
                .AsNoTracking()
                .Where(v => v.Source == source && v.ExternalId != null && chunk.Contains(v.ExternalId))
                .ToListAsync(ct);
            results.AddRange(rows);
        }

        return results;
    }

    public async Task<(int Created, int Updated)> BulkUpsertAsync(IReadOnlyList<VacancyDB> vacancies, CancellationToken ct = default)
    {
        if (vacancies.Count == 0)
        {
            return (0, 0);
        }

        // Уже збережені вакансії шукаємо чанкованими індексними запитами лише за (Source, ExternalId)
        List<VacancyDB> candidates = await VacancyCandidateLookup.LoadByExternalKeysAsync(
            _context.Vacancies,
            vacancies.Where(v => !string.IsNullOrWhiteSpace(v.ExternalId)).Select(v => (v.Source, v.ExternalId!)),
            ct);
        VacancyMatchIndex<VacancyDB> index = new(candidates);

        int created = 0;
        int updated = 0;
        foreach (VacancyDB vacancy in vacancies)
        {
            VacancyDB? current = index.FindByExternalId(vacancy.Source, vacancy.ExternalId);
            if (current == null)
            {
                _context.Vacancies.Add(vacancy);
                index.Add(vacancy);
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

    public async Task<int> DeactivateExpiredAsync(DateTimeOffset publishedBefore, CancellationToken ct = default)
    {
        List<VacancyDB> expired = await _context.Vacancies
            .Where(v => v.IsActive && v.PublishedAt < publishedBefore)
            .ToListAsync(ct);

        if (expired.Count == 0)
        {
            return 0;
        }

        foreach (VacancyDB vacancy in expired)
        {
            vacancy.IsActive = false;
        }

        await _context.SaveChangesAsync(ct);
        return expired.Count;
    }

    public async Task<IReadOnlyList<VacancyDB>> GetAllForSyncAsync(
        DateTimeOffset? since = null,
        IReadOnlyCollection<Guid>? includeIds = null,
        CancellationToken ct = default)
    {
        IQueryable<VacancyDB> query = _context.Vacancies.AsNoTracking();

        if (since.HasValue)
        {
            DateTimeOffset threshold = since.Value;
            if (includeIds is { Count: > 0 })
            {
                query = query.Where(v => v.LastSeenAt >= threshold || v.FirstSeenAt >= threshold || includeIds.Contains(v.Id));
            }
            else
            {
                query = query.Where(v => v.LastSeenAt >= threshold || v.FirstSeenAt >= threshold);
            }
        }

        return await query
            .OrderByDescending(v => v.LastSeenAt)
            .ThenByDescending(v => v.PublishedAt)
            .ToListAsync(ct);
    }

    public Task<int> CountAllAsync(CancellationToken ct = default) =>
        _context.Vacancies.AsNoTracking().CountAsync(ct);

    public async Task<VacancySyncMergeResult> SyncMergeAsync(IReadOnlyList<VacancyDB> vacancies, CancellationToken ct = default)
    {
        if (vacancies.Count == 0)
        {
            return new VacancySyncMergeResult(0, 0, []);
        }

        List<VacancyDB> candidates = await VacancyCandidateLookup.LoadAsync(
            _context.Vacancies,
            vacancies.Select(v => v.Id),
            vacancies.Where(v => !string.IsNullOrWhiteSpace(v.ExternalId)).Select(v => (v.Source, v.ExternalId!)),
            vacancies.Select(v => v.SourceUrl),
            ct);

        VacancyMatchIndex<VacancyDB> index = new(candidates);

        int created = 0;
        int updated = 0;
        HashSet<Guid> canonicalIds = [];

        foreach (VacancyDB incoming in vacancies)
        {
            VacancyDB? current = index.FindMatch(incoming.Id, incoming.Source, incoming.ExternalId, incoming.SourceUrl);
            if (current is null)
            {
                if (incoming.Id == Guid.Empty)
                {
                    incoming.Id = Guid.NewGuid();
                }

                _context.Vacancies.Add(incoming);
                index.Add(incoming);
                canonicalIds.Add(incoming.Id);
                created++;
                continue;
            }

            canonicalIds.Add(current.Id);
            string? previousExternalId = current.ExternalId;
            if (MergeIntoExisting(current, incoming))
            {
                if (string.IsNullOrWhiteSpace(previousExternalId) && !string.IsNullOrWhiteSpace(current.ExternalId))
                {
                    index.IndexExternalId(current);
                }

                updated++;
            }
        }

        if (created > 0 || updated > 0)
        {
            await _context.SaveChangesAsync(ct);
        }

        return new VacancySyncMergeResult(created, updated, canonicalIds.ToList());
    }

    private static bool MergeIntoExisting(VacancyDB current, VacancyDB incoming)
    {
        bool changed = false;

        if (incoming.FirstSeenAt != default && (current.FirstSeenAt == default || incoming.FirstSeenAt < current.FirstSeenAt))
        {
            current.FirstSeenAt = incoming.FirstSeenAt;
            changed = true;
        }

        if (incoming.LastSeenAt > current.LastSeenAt)
        {
            if (!string.IsNullOrWhiteSpace(incoming.Title))
            {
                current.Title = incoming.Title;
            }

            if (!string.IsNullOrWhiteSpace(incoming.Company))
            {
                current.Company = incoming.Company;
            }

            if (!string.IsNullOrWhiteSpace(incoming.Description) && incoming.Description.Length >= current.Description.Length)
            {
                current.Description = incoming.Description;
            }

            if (!string.IsNullOrWhiteSpace(incoming.Category))
            {
                current.Category = incoming.Category;
            }

            if (!string.IsNullOrWhiteSpace(incoming.Location))
            {
                current.Location = incoming.Location;
            }

            if (!string.IsNullOrWhiteSpace(incoming.FullAddress))
            {
                current.FullAddress = incoming.FullAddress;
            }

            if (!string.IsNullOrWhiteSpace(incoming.Schedule))
            {
                current.Schedule = incoming.Schedule;
            }

            if (!string.IsNullOrWhiteSpace(incoming.Experience))
            {
                current.Experience = incoming.Experience;
            }

            if (incoming.ExperienceMonths.HasValue)
            {
                current.ExperienceMonths = incoming.ExperienceMonths;
            }

            if (incoming.SalaryMin.HasValue || incoming.SalaryMax.HasValue)
            {
                current.SalaryMin = incoming.SalaryMin;
                current.SalaryMax = incoming.SalaryMax;
                if (!string.IsNullOrWhiteSpace(incoming.Currency))
                {
                    current.Currency = incoming.Currency;
                }
            }

            if (!string.IsNullOrWhiteSpace(incoming.SkillsJson) && incoming.SkillsJson != "[]")
            {
                current.SkillsJson = incoming.SkillsJson;
            }

            if (string.IsNullOrWhiteSpace(current.ExternalId) && !string.IsNullOrWhiteSpace(incoming.ExternalId))
            {
                current.ExternalId = incoming.ExternalId;
            }

            current.IsActive = incoming.IsActive;
            current.LastSeenAt = incoming.LastSeenAt;
            return true;
        }

        // Серверний запис новіший або такий самий — доповнюємо лише порожні необов'язкові поля
        if (string.IsNullOrWhiteSpace(current.ExternalId) && !string.IsNullOrWhiteSpace(incoming.ExternalId))
        {
            current.ExternalId = incoming.ExternalId;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(current.FullAddress) && !string.IsNullOrWhiteSpace(incoming.FullAddress))
        {
            current.FullAddress = incoming.FullAddress;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(current.Schedule) && !string.IsNullOrWhiteSpace(incoming.Schedule))
        {
            current.Schedule = incoming.Schedule;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(current.Experience) && !string.IsNullOrWhiteSpace(incoming.Experience))
        {
            current.Experience = incoming.Experience;
            changed = true;
        }

        if (!current.ExperienceMonths.HasValue && incoming.ExperienceMonths.HasValue)
        {
            current.ExperienceMonths = incoming.ExperienceMonths;
            changed = true;
        }

        if ((current.SkillsJson == "[]" || string.IsNullOrWhiteSpace(current.SkillsJson))
            && !string.IsNullOrWhiteSpace(incoming.SkillsJson) && incoming.SkillsJson != "[]")
        {
            current.SkillsJson = incoming.SkillsJson;
            changed = true;
        }

        if (!current.SalaryMin.HasValue && !current.SalaryMax.HasValue
            && (incoming.SalaryMin.HasValue || incoming.SalaryMax.HasValue))
        {
            current.SalaryMin = incoming.SalaryMin;
            current.SalaryMax = incoming.SalaryMax;
            if (!string.IsNullOrWhiteSpace(incoming.Currency))
            {
                current.Currency = incoming.Currency;
            }
            changed = true;
        }

        return changed;
    }
}
