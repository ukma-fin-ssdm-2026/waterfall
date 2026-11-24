// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Waterfall.Client.Entities;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Normalization;

namespace Waterfall.Client.Data;

/// <summary>
/// Загальний контекст локальної бази даних клієнта на базі Entity Framework Core.
/// Дзеркальний до серверного WaterfallDbContext (вакансії, збережені вакансії, користувачі, персональні дані),
/// але для таблиць вакансій і геокешу використовує ClientVacancyDB / ClientGeoLocationCacheDB із прапорцем IsSynced
/// та зберігає стан синхронізації у SyncState.
/// </summary>
public class ClientWaterfallDbContext : DbContext
{
    internal static readonly SemaphoreSlim SharedInitLock = new(1, 1);

    public ClientWaterfallDbContext(DbContextOptions<ClientWaterfallDbContext> options) : base(options)
    {
    }

    public DbSet<ClientVacancyDB> Vacancies => Set<ClientVacancyDB>();
    public DbSet<SavedVacancyDB> SavedVacancies => Set<SavedVacancyDB>();
    public DbSet<UserDB> Users => Set<UserDB>();
    public DbSet<UserPersonalDataDB> UserPersonalData => Set<UserPersonalDataDB>();
    public DbSet<ClientGeoLocationCacheDB> GeoLocationCache => Set<ClientGeoLocationCacheDB>();
    public DbSet<ClientSyncStateDB> SyncStates => Set<ClientSyncStateDB>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        NormalizeVacancies();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        NormalizeVacancies();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(SqliteUnicodeLowerInterceptor.Instance);
    }

    private void NormalizeVacancies()
    {
        foreach (EntityEntry<ClientVacancyDB> entry in ChangeTracker.Entries<ClientVacancyDB>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                VacancyNormalizer.Apply(entry.Entity);
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Щоб EF Core не створював TPH-дискримінатор між VacancyDB та ClientVacancyDB
        // (та між GeoLocationCacheDB і ClientGeoLocationCacheDB), виключаємо базові класи з моделі.
        modelBuilder.Entity<SavedVacancyDB>().Ignore(s => s.Vacancy);
        modelBuilder.Ignore<VacancyDB>();
        modelBuilder.Ignore<GeoLocationCacheDB>();
        modelBuilder.Entity<ClientVacancyDB>().HasBaseType((Type?)null);
        modelBuilder.Entity<ClientGeoLocationCacheDB>().HasBaseType((Type?)null);

        modelBuilder.Entity<ClientSyncStateDB>(entity =>
        {
            entity.Property(e => e.Key).HasMaxLength(64);
            entity.Property(e => e.LastSummary).HasMaxLength(500);
        });

        // SQLite не має нативного типу DateTimeOffset, тому зберігаємо бінарно (як у серверному WaterfallDbContext)
        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (IMutableProperty property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter());
                }
            }
        }
    }
}
