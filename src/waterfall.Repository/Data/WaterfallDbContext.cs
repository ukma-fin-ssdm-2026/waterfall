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
    public DbSet<GeoLocationCacheDB> GeoLocationCache => Set<GeoLocationCacheDB>();
    public DbSet<SearchQueryDB> SearchQueries => Set<SearchQueryDB>();
    public DbSet<VacancyViewDB> VacancyViews => Set<VacancyViewDB>();
    public DbSet<ChatSessionDB> ChatSessions => Set<ChatSessionDB>();
    public DbSet<ChatMessageDB> ChatMessages => Set<ChatMessageDB>();
    public DbSet<ChatMessageVacancyDB> ChatMessageVacancies => Set<ChatMessageVacancyDB>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // У SQLite lower() без цього не працює з кирилицею, і пошук каталогу був би чутливим до регістру
        optionsBuilder.AddInterceptors(SqliteUnicodeLowerInterceptor.Instance);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Складений ключ (message_id, vacancy_id) не дає зберегти ту саму вакансію в одній відповіді двічі
        modelBuilder.Entity<ChatMessageVacancyDB>()
            .HasKey(link => new { link.MessageId, link.VacancyId });

        // Автор репліки зберігається рядком ("User"/"Assistant"), щоб вміст таблиці читався без декодування
        modelBuilder.Entity<ChatMessageDB>()
            .Property(message => message.Author)
            .HasConversion<string>()
            .HasMaxLength(20);

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
