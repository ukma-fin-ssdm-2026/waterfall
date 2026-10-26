// AI: пошук без урахування регістру для кирилиці в SQLite (issue #62, на прохання команди); перевірено: тести EfVacancyRepositoryTests на SQLite, самоперевірка автора (Саєнко Данило, §8)

using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Waterfall.Repository.Data;

/// <summary>
/// Вбудована в SQLite функція lower() знає лише латиницю: lower('Київ') лишається 'Київ', тож фільтри каталогу
/// на кшталт <c>v.Location.ToLower().Contains("київ")</c> (EF Core перекладає ToLower() у lower()) не знаходили кирилицю.
/// Перехоплювач підміняє lower() на кожному з'єднанні SQLite версією з .NET, яка знає всі алфавіти.
/// SQL Server не зачіпає: там LOWER() і так працює з кирилицею.
/// </summary>
public sealed class SqliteUnicodeLowerInterceptor : DbCommandInterceptor
{
    public static readonly SqliteUnicodeLowerInterceptor Instance = new();

    // З'єднання, на яких функцію вже підмінено. Microsoft.Data.Sqlite сам відновлює її після повторного відкриття,
    // тож досить одного разу на з'єднання. Слабкі посилання не тримають закриті з'єднання в пам'яті.
    private readonly ConditionalWeakTable<SqliteConnection, object> _prepared = new();

    private SqliteUnicodeLowerInterceptor()
    {
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Prepare(command.Connection);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Prepare(command.Connection);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Prepare(command.Connection);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Prepare(command.Connection);
        return ValueTask.FromResult(result);
    }

    private void Prepare(DbConnection? connection)
    {
        if (connection is SqliteConnection sqlite && _prepared.TryAdd(sqlite, this))
        {
            // Та сама сигнатура, що й у вбудованої lower(X): SQLite віддає перевагу функції застосунку
            sqlite.CreateFunction("lower", (string? value) => value?.ToLowerInvariant(), isDeterministic: true);
        }
    }
}
