// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Waterfall.Client.Data;
using Waterfall.Client.Entities;
using Waterfall.Repository.Entities;

namespace Waterfall.Client.Tests;

/// <summary>
/// Перевіряє, що клієнтська локальна БД (<see cref="ClientWaterfallDbContext"/>) містить лише
/// клієнтські таблиці (вакансії, геокеш, стан синхронізації) без серверних таблиць користувачів і закладок.
/// </summary>
public sealed class ClientWaterfallDbContextTests
{
    [Fact]
    public void Model_ExcludesSavedVacanciesUsersAndUserPersonalData()
    {
        DbContextOptions<ClientWaterfallDbContext> options = new DbContextOptionsBuilder<ClientWaterfallDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using ClientWaterfallDbContext context = new(options);
        IModel model = context.Model;

        Assert.Null(model.FindEntityType(typeof(SavedVacancyDB)));
        Assert.Null(model.FindEntityType(typeof(UserDB)));
        Assert.Null(model.FindEntityType(typeof(UserPersonalDataDB)));

        IReadOnlyList<string?> tableNames = model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .ToList();

        Assert.DoesNotContain("SavedVacancies", tableNames);
        Assert.DoesNotContain("Users", tableNames);
        Assert.DoesNotContain("UserPersonalData", tableNames);

        Assert.NotNull(model.FindEntityType(typeof(ClientVacancyDB)));
        Assert.NotNull(model.FindEntityType(typeof(ClientGeoLocationCacheDB)));
        Assert.NotNull(model.FindEntityType(typeof(ClientSyncStateDB)));
    }
}
