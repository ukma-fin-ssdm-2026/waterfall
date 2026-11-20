// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Waterfall.Client.Data;
using Waterfall.Client.Entities;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Repository.Normalization;

namespace Waterfall.Client.Services;

/// <summary>
/// Реалізація локального сховища вакансій клієнта поверх EF Core SQLite (<see cref="ClientWaterfallDbContext"/>).
/// Підтримує офлайн-збір, редагування категорій, дедуплікацію та двосторонню синхронізацію із сервером.
/// </summary>
public sealed class EfLocalVacancyStore : ILocalVacancyStore, IDisposable
{
    private const string VacancySyncStateKey = "vacancies";

    private readonly IDbContextFactory<ClientWaterfallDbContext>? _contextFactory;
    private readonly DbContextOptions<ClientWaterfallDbContext>? _options;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;

    public EfLocalVacancyStore(IDbContextFactory<ClientWaterfallDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public EfLocalVacancyStore(string? databasePath = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "Waterfall");
            Directory.CreateDirectory(folder);
            databasePath = Path.Combine(folder, "waterfall_local.db");
        }
        else
        {
            string? dir = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        _options = new DbContextOptionsBuilder<ClientWaterfallDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
    }

    private ClientWaterfallDbContext CreateContext()
    {
        if (_contextFactory is not null)
        {
            return _contextFactory.CreateDbContext();
        }

        if (_options is not null)
        {
            return new ClientWaterfallDbContext(_options);
        }

        throw new InvalidOperationException("Неможливо створити контекст ClientWaterfallDbContext.");
    }

    private async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initLock.WaitAsync(ct);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            await using ClientWaterfallDbContext context = CreateContext();
            await context.Database.EnsureCreatedAsync(ct);
            await EnsureSchemaUpgradedAsync(context, ct);
            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Ідемпотентно оновлює схему вже існуючого локального файлу waterfall_local.db,
    /// якщо він був створений попередньою версією клієнта без колонок IsSynced/LocalUpdatedAt або таблиці SyncState.
    /// </summary>
    private static async Task EnsureSchemaUpgradedAsync(ClientWaterfallDbContext context, CancellationToken ct)
    {
        DbConnection connection = context.Database.GetDbConnection();
        bool shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            HashSet<string> existingColumns = new(StringComparer.OrdinalIgnoreCase);
            await using (DbCommand pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA table_info('Vacancies');";
                await using DbDataReader reader = await pragma.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    existingColumns.Add(reader.GetString(1));
                }
            }

            if (existingColumns.Count > 0)
            {
                (string Column, string Sql)[] requiredColumns =
                [
                    ("CategoryGroup", "ALTER TABLE Vacancies ADD COLUMN CategoryGroup TEXT NULL;"),
                    ("FullAddress", "ALTER TABLE Vacancies ADD COLUMN FullAddress TEXT NULL;"),
                    ("Schedule", "ALTER TABLE Vacancies ADD COLUMN Schedule TEXT NULL;"),
                    ("Experience", "ALTER TABLE Vacancies ADD COLUMN Experience TEXT NULL;"),
                    ("ExperienceMonths", "ALTER TABLE Vacancies ADD COLUMN ExperienceMonths INTEGER NULL;"),
                    ("WorkFormat", "ALTER TABLE Vacancies ADD COLUMN WorkFormat INTEGER NOT NULL DEFAULT 0;"),
                    ("SearchText", "ALTER TABLE Vacancies ADD COLUMN SearchText TEXT NOT NULL DEFAULT '';"),
                    ("ExternalId", "ALTER TABLE Vacancies ADD COLUMN ExternalId TEXT NULL;"),
                    ("IsSynced", "ALTER TABLE Vacancies ADD COLUMN IsSynced INTEGER NOT NULL DEFAULT 1;"),
                    ("LocalUpdatedAt", "ALTER TABLE Vacancies ADD COLUMN LocalUpdatedAt INTEGER NOT NULL DEFAULT 0;")
                ];

                foreach ((string column, string sql) in requiredColumns)
                {
                    if (!existingColumns.Contains(column))
                    {
                        await context.Database.ExecuteSqlRawAsync(sql, ct);
                    }
                }
            }

            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS SyncState (
                    Key TEXT NOT NULL CONSTRAINT PK_SyncState PRIMARY KEY,
                    LastSyncedAt INTEGER NULL,
                    LastSummary TEXT NULL
                );
                """, ct);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<IReadOnlyList<VacancyDto>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using ClientWaterfallDbContext context = CreateContext();

        List<ClientVacancyDB> rows = await context.Vacancies
            .AsNoTracking()
            .OrderByDescending(v => v.PublishedAt)
            .ThenByDescending(v => v.LastSeenAt)
            .ToListAsync(ct);

        return rows.Select(MapToDto).ToList();
    }

    public async Task<int> SaveVacanciesAsync(IEnumerable<VacancyDto> vacancies, bool isSynced, CancellationToken ct = default)
    {
        List<VacancyDto> list = vacancies.Where(v => !string.IsNullOrWhiteSpace(v.Title)).ToList();
        if (list.Count == 0)
        {
            return 0;
        }

        await EnsureInitializedAsync(ct);
        await using ClientWaterfallDbContext context = CreateContext();
        List<ClientVacancyDB> existing = await context.Vacancies.ToListAsync(ct);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int affected = 0;

        foreach (VacancyDto dto in list)
        {
            ClientVacancyDB? match = FindMatch(existing, dto.Id, dto.Source.ToString(), dto.ExternalId, dto.SourceUrl);
            if (match is null)
            {
                ClientVacancyDB entity = MapFromDto(dto, isSynced, now);
                context.Vacancies.Add(entity);
                existing.Add(entity);
                affected++;
                continue;
            }

            // Якщо існуючий локальний запис містить новіші несинхронізовані правки — не затираємо їх старими серверними даними
            if (!match.IsSynced && isSynced && match.LocalUpdatedAt >= dto.LastSeenAt)
            {
                continue;
            }

            ApplyDtoToEntity(match, dto, isSynced, now);
            affected++;
        }

        if (affected > 0)
        {
            await context.SaveChangesAsync(ct);
        }

        return affected;
    }

    public async Task<IReadOnlyList<ClientVacancyDB>> GetUnsyncedAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using ClientWaterfallDbContext context = CreateContext();

        return await context.Vacancies
            .AsNoTracking()
            .Where(v => !v.IsSynced)
            .OrderBy(v => v.LocalUpdatedAt)
            .ToListAsync(ct);
    }

    public async Task MarkAsSyncedAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        List<Guid> idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return;
        }

        await EnsureInitializedAsync(ct);
        await using ClientWaterfallDbContext context = CreateContext();

        List<ClientVacancyDB> rows = await context.Vacancies
            .Where(v => idList.Contains(v.Id))
            .ToListAsync(ct);

        foreach (ClientVacancyDB row in rows)
        {
            row.IsSynced = true;
        }

        await context.SaveChangesAsync(ct);
    }

    public async Task<(int Added, int Updated)> UpsertFromServerAsync(IEnumerable<VacancySyncItemDto> serverItems, CancellationToken ct = default)
    {
        List<VacancySyncItemDto> items = serverItems.Where(i => !string.IsNullOrWhiteSpace(i.Title)).ToList();
        if (items.Count == 0)
        {
            return (0, 0);
        }

        await EnsureInitializedAsync(ct);
        await using ClientWaterfallDbContext context = CreateContext();
        List<ClientVacancyDB> existing = await context.Vacancies.ToListAsync(ct);

        int added = 0;
        int updated = 0;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (VacancySyncItemDto item in items)
        {
            ClientVacancyDB? match = FindMatch(existing, item.Id, item.Source.ToString(), item.ExternalId, item.SourceUrl);
            if (match is null)
            {
                ClientVacancyDB entity = MapFromSyncItem(item, now);
                context.Vacancies.Add(entity);
                existing.Add(entity);
                added++;
                continue;
            }

            // Якщо серверний запис має інший канонічний Id, але той самий (Source, ExternalId) або SourceUrl —
            // вирівнюємо ідентифікатор, щоб надалі Id клієнта та сервера збігалися.
            if (match.Id != item.Id && item.Id != Guid.Empty && !existing.Any(e => e.Id == item.Id))
            {
                context.Vacancies.Remove(match);
                existing.Remove(match);
                ClientVacancyDB replaced = MapFromSyncItem(item, now);
                context.Vacancies.Add(replaced);
                existing.Add(replaced);
                updated++;
                continue;
            }

            // Не перезаписуємо несинхронізовані локальні правки, які новіші за серверний стан
            if (!match.IsSynced && match.LocalUpdatedAt > item.LastSeenAt)
            {
                continue;
            }

            ApplySyncItemToEntity(match, item, now);
            updated++;
        }

        if (added > 0 || updated > 0)
        {
            await context.SaveChangesAsync(ct);
        }

        return (added, updated);
    }

    public async Task<DateTimeOffset?> GetLastSyncedAtAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using ClientWaterfallDbContext context = CreateContext();

        ClientSyncStateDB? state = await context.SyncStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == VacancySyncStateKey, ct);
        return state?.LastSyncedAt;
    }

    public async Task SetLastSyncedAtAsync(DateTimeOffset syncedAt, string? summary = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using ClientWaterfallDbContext context = CreateContext();

        ClientSyncStateDB? state = await context.SyncStates
            .FirstOrDefaultAsync(s => s.Key == VacancySyncStateKey, ct);

        if (state is null)
        {
            context.SyncStates.Add(new ClientSyncStateDB
            {
                Key = VacancySyncStateKey,
                LastSyncedAt = syncedAt,
                LastSummary = summary
            });
        }
        else
        {
            state.LastSyncedAt = syncedAt;
            state.LastSummary = summary;
        }

        await context.SaveChangesAsync(ct);
    }

    public async Task<LocalVacancyStats> GetStatsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await using ClientWaterfallDbContext context = CreateContext();

        int total = await context.Vacancies.AsNoTracking().CountAsync(ct);
        int unsynced = await context.Vacancies.AsNoTracking().CountAsync(v => !v.IsSynced, ct);
        ClientSyncStateDB? state = await context.SyncStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == VacancySyncStateKey, ct);

        return new LocalVacancyStats(total, unsynced, state?.LastSyncedAt);
    }

    public static VacancySyncItemDto MapToSyncItem(ClientVacancyDB entity) => new()
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
        Source = Enum.TryParse(entity.Source, true, out JobSource source) ? source : JobSource.Unknown,
        SourceUrl = entity.SourceUrl,
        Skills = DeserializeSkills(entity.SkillsJson),
        PublishedAt = entity.PublishedAt,
        FirstSeenAt = entity.FirstSeenAt,
        LastSeenAt = entity.LastSeenAt,
        IsActive = entity.IsActive
    };

    private static VacancyDto MapToDto(ClientVacancyDB entity) => new()
    {
        Id = entity.Id,
        ExternalId = entity.ExternalId,
        Title = entity.Title,
        Company = entity.Company,
        Description = entity.Description,
        Category = entity.Category,
        CategoryGroup = entity.CategoryGroup ?? VacancyCategoryMap.Resolve(entity.Category),
        Location = entity.Location,
        FullAddress = entity.FullAddress,
        Schedule = entity.Schedule,
        Experience = entity.Experience,
        ExperienceMonths = entity.ExperienceMonths,
        WorkFormat = entity.WorkFormat,
        SalaryMin = entity.SalaryMin,
        SalaryMax = entity.SalaryMax,
        Currency = entity.Currency,
        Source = Enum.TryParse(entity.Source, true, out JobSource source) ? source : JobSource.Unknown,
        SourceUrl = entity.SourceUrl,
        Skills = DeserializeSkills(entity.SkillsJson),
        PublishedAt = entity.PublishedAt,
        FirstSeenAt = entity.FirstSeenAt,
        LastSeenAt = entity.LastSeenAt,
        IsActive = entity.IsActive
    };

    private static ClientVacancyDB MapFromDto(VacancyDto dto, bool isSynced, DateTimeOffset now)
    {
        string category = string.IsNullOrWhiteSpace(dto.Category) ? "Інше" : dto.Category.Trim();
        return new ClientVacancyDB
        {
            Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id,
            ExternalId = dto.ExternalId,
            Title = dto.Title.Trim(),
            Company = string.IsNullOrWhiteSpace(dto.Company) ? "Не вказано" : dto.Company.Trim(),
            Description = dto.Description ?? string.Empty,
            Category = category,
            CategoryGroup = dto.CategoryGroup ?? VacancyCategoryMap.Resolve(category),
            Location = string.IsNullOrWhiteSpace(dto.Location) ? "Не вказано" : dto.Location.Trim(),
            FullAddress = dto.FullAddress,
            Schedule = dto.Schedule,
            Experience = dto.Experience,
            ExperienceMonths = dto.ExperienceMonths ?? VacancyExperienceParser.ParseMonths(dto.Experience, dto.Description),
            WorkFormat = dto.WorkFormat,
            SalaryMin = dto.SalaryMin,
            SalaryMax = dto.SalaryMax,
            Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "UAH" : dto.Currency.Trim().ToUpperInvariant(),
            Source = dto.Source.ToString(),
            SourceUrl = dto.SourceUrl ?? string.Empty,
            SkillsJson = SerializeSkills(dto.Skills),
            PublishedAt = dto.PublishedAt == default ? now : dto.PublishedAt,
            FirstSeenAt = dto.FirstSeenAt == default ? now : dto.FirstSeenAt,
            LastSeenAt = dto.LastSeenAt == default ? now : dto.LastSeenAt,
            IsActive = dto.IsActive,
            IsSynced = isSynced,
            LocalUpdatedAt = now
        };
    }

    private static void ApplyDtoToEntity(ClientVacancyDB entity, VacancyDto dto, bool isSynced, DateTimeOffset now)
    {
        entity.ExternalId ??= dto.ExternalId;
        if (!string.IsNullOrWhiteSpace(dto.Title)) entity.Title = dto.Title.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Company)) entity.Company = dto.Company.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Description) && dto.Description.Length >= entity.Description.Length)
        {
            entity.Description = dto.Description;
        }

        if (!string.IsNullOrWhiteSpace(dto.Category))
        {
            entity.Category = dto.Category.Trim();
            entity.CategoryGroup = dto.CategoryGroup ?? VacancyCategoryMap.Resolve(entity.Category);
        }

        if (!string.IsNullOrWhiteSpace(dto.Location)) entity.Location = dto.Location.Trim();
        if (!string.IsNullOrWhiteSpace(dto.FullAddress)) entity.FullAddress = dto.FullAddress;
        if (!string.IsNullOrWhiteSpace(dto.Schedule)) entity.Schedule = dto.Schedule;
        if (!string.IsNullOrWhiteSpace(dto.Experience)) entity.Experience = dto.Experience;
        if (dto.ExperienceMonths.HasValue) entity.ExperienceMonths = dto.ExperienceMonths;
        if (dto.SalaryMin.HasValue || dto.SalaryMax.HasValue)
        {
            entity.SalaryMin = dto.SalaryMin;
            entity.SalaryMax = dto.SalaryMax;
            if (!string.IsNullOrWhiteSpace(dto.Currency))
            {
                entity.Currency = dto.Currency.Trim().ToUpperInvariant();
            }
        }

        if (dto.Skills is { Count: > 0 })
        {
            entity.SkillsJson = SerializeSkills(dto.Skills);
        }

        if (dto.PublishedAt != default) entity.PublishedAt = dto.PublishedAt;
        if (dto.FirstSeenAt != default && (entity.FirstSeenAt == default || dto.FirstSeenAt < entity.FirstSeenAt))
        {
            entity.FirstSeenAt = dto.FirstSeenAt;
        }

        entity.LastSeenAt = dto.LastSeenAt > entity.LastSeenAt ? dto.LastSeenAt : now;
        entity.IsActive = dto.IsActive;
        entity.IsSynced = isSynced;
        entity.LocalUpdatedAt = now;
    }

    private static ClientVacancyDB MapFromSyncItem(VacancySyncItemDto item, DateTimeOffset now)
    {
        string category = string.IsNullOrWhiteSpace(item.Category) ? "Інше" : item.Category.Trim();
        return new ClientVacancyDB
        {
            Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id,
            ExternalId = item.ExternalId,
            Title = item.Title.Trim(),
            Company = string.IsNullOrWhiteSpace(item.Company) ? "Не вказано" : item.Company.Trim(),
            Description = item.Description ?? string.Empty,
            Category = category,
            CategoryGroup = item.CategoryGroup ?? VacancyCategoryMap.Resolve(category),
            Location = string.IsNullOrWhiteSpace(item.Location) ? "Не вказано" : item.Location.Trim(),
            FullAddress = item.FullAddress,
            Schedule = item.Schedule,
            Experience = item.Experience,
            ExperienceMonths = item.ExperienceMonths ?? VacancyExperienceParser.ParseMonths(item.Experience, item.Description),
            WorkFormat = item.WorkFormat,
            SalaryMin = item.SalaryMin,
            SalaryMax = item.SalaryMax,
            Currency = string.IsNullOrWhiteSpace(item.Currency) ? "UAH" : item.Currency.Trim().ToUpperInvariant(),
            Source = item.Source.ToString(),
            SourceUrl = item.SourceUrl ?? string.Empty,
            SkillsJson = SerializeSkills(item.Skills),
            PublishedAt = item.PublishedAt == default ? now : item.PublishedAt,
            FirstSeenAt = item.FirstSeenAt == default ? now : item.FirstSeenAt,
            LastSeenAt = item.LastSeenAt == default ? now : item.LastSeenAt,
            IsActive = item.IsActive,
            IsSynced = true,
            LocalUpdatedAt = item.LastSeenAt == default ? now : item.LastSeenAt
        };
    }

    private static void ApplySyncItemToEntity(ClientVacancyDB entity, VacancySyncItemDto item, DateTimeOffset now)
    {
        entity.ExternalId = item.ExternalId ?? entity.ExternalId;
        entity.Title = item.Title.Trim();
        entity.Company = string.IsNullOrWhiteSpace(item.Company) ? entity.Company : item.Company.Trim();
        if (!string.IsNullOrWhiteSpace(item.Description)) entity.Description = item.Description;
        if (!string.IsNullOrWhiteSpace(item.Category))
        {
            entity.Category = item.Category.Trim();
            entity.CategoryGroup = item.CategoryGroup ?? VacancyCategoryMap.Resolve(entity.Category);
        }

        if (!string.IsNullOrWhiteSpace(item.Location)) entity.Location = item.Location.Trim();
        entity.FullAddress = item.FullAddress ?? entity.FullAddress;
        entity.Schedule = item.Schedule ?? entity.Schedule;
        entity.Experience = item.Experience ?? entity.Experience;
        entity.ExperienceMonths = item.ExperienceMonths ?? entity.ExperienceMonths;
        entity.SalaryMin = item.SalaryMin;
        entity.SalaryMax = item.SalaryMax;
        if (!string.IsNullOrWhiteSpace(item.Currency)) entity.Currency = item.Currency.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(item.SourceUrl)) entity.SourceUrl = item.SourceUrl;
        entity.SkillsJson = SerializeSkills(item.Skills);
        if (item.PublishedAt != default) entity.PublishedAt = item.PublishedAt;
        if (item.FirstSeenAt != default) entity.FirstSeenAt = item.FirstSeenAt;
        entity.LastSeenAt = item.LastSeenAt == default ? now : item.LastSeenAt;
        entity.IsActive = item.IsActive;
        entity.IsSynced = true;
        entity.LocalUpdatedAt = entity.LastSeenAt;
    }

    private static ClientVacancyDB? FindMatch(
        List<ClientVacancyDB> existing,
        Guid id,
        string source,
        string? externalId,
        string? sourceUrl)
    {
        if (!string.IsNullOrWhiteSpace(externalId))
        {
            ClientVacancyDB? byExternal = existing.FirstOrDefault(v =>
                string.Equals(v.Source, source, StringComparison.OrdinalIgnoreCase)
                && string.Equals(v.ExternalId, externalId, StringComparison.Ordinal));
            if (byExternal is not null)
            {
                return byExternal;
            }
        }

        if (id != Guid.Empty)
        {
            ClientVacancyDB? byId = existing.FirstOrDefault(v => v.Id == id);
            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            return existing.FirstOrDefault(v =>
                string.Equals(v.SourceUrl, sourceUrl, StringComparison.OrdinalIgnoreCase)
                && string.Equals(v.Source, source, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(externalId)
                    || string.IsNullOrWhiteSpace(v.ExternalId)
                    || string.Equals(v.ExternalId, externalId, StringComparison.Ordinal)));
        }

        return null;
    }

    // AOT-безпечна серіалізація списку навичок через згенерований ClientJsonContext
    private static string SerializeSkills(IEnumerable<string>? skills)
    {
        List<string> cleaned = (skills ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return JsonSerializer.Serialize(cleaned, ClientJsonContext.Default.ListString);
    }

    private static List<string> DeserializeSkills(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize(json, ClientJsonContext.Default.ListString) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void Dispose() => _initLock.Dispose();
}
