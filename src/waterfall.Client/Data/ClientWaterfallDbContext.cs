// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Waterfall.Client.Entities;
using Waterfall.Repository.Entities;

namespace Waterfall.Client.Data;

/// <summary>
/// Загальний контекст локальної бази даних клієнта на базі Entity Framework Core.
/// Повністю дзеркальний до серверного WaterfallDbContext (вакансії, збережені вакансії, користувачі, персональні дані),
/// але в таблиці геокешу використовує ClientGeoLocationCacheDB із прапорцем IsSynced.
/// </summary>
public class ClientWaterfallDbContext : DbContext
{
    public ClientWaterfallDbContext(DbContextOptions<ClientWaterfallDbContext> options) : base(options)
    {
    }

    public DbSet<VacancyDB> Vacancies => Set<VacancyDB>();
    public DbSet<SavedVacancyDB> SavedVacancies => Set<SavedVacancyDB>();
    public DbSet<UserDB> Users => Set<UserDB>();
    public DbSet<UserPersonalDataDB> UserPersonalData => Set<UserPersonalDataDB>();
    public DbSet<ClientGeoLocationCacheDB> GeoLocationCache => Set<ClientGeoLocationCacheDB>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // SQLite не має нативного типу DateTimeOffset, тому зберігаємо бінарно або числовим форматом
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
