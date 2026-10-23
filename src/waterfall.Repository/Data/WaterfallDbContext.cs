// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Waterfall.Repository.Entities;

namespace Waterfall.Repository.Data;

/// <summary>
/// Основний контекст бази даних Entity Framework Core платформи Waterfall.
/// Підтримує подвійний провайдер SQLite (локально) та MSSQL (продакшн)
/// </summary>
public class WaterfallDbContext : DbContext
{
    public WaterfallDbContext(DbContextOptions<WaterfallDbContext> options) : base(options)
    {
    }

    public DbSet<VacancyDB> Vacancies => Set<VacancyDB>();
    public DbSet<SavedVacancyDB> SavedVacancies => Set<SavedVacancyDB>();
    public DbSet<UserDB> Users => Set<UserDB>();
    public DbSet<UserPersonalDataDB> UserPersonalData => Set<UserPersonalDataDB>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
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
}
