using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Waterfall.Repository.Data;

/// <summary>
/// Спеціальна операція міграції для ідемпотентного додавання відсутніх колонок таблиці <c>Vacancies</c>
/// у вже існуючій до-міграційній SQLite-базі даних.
/// </summary>
internal sealed class EnsureLegacySqliteVacancyColumnsOperation : MigrationOperation
{
}

/// <summary>
/// Генератор SQL-команд міграцій SQLite, що розширює стандартний <see cref="SqliteMigrationsSqlGenerator"/>
/// перевіркою <c>PRAGMA table_info('Vacancies')</c> під час виконання живої міграції.
/// </summary>
internal sealed class WaterfallSqliteMigrationsSqlGenerator : SqliteMigrationsSqlGenerator
{
    private bool _isGeneratingScript;

    public WaterfallSqliteMigrationsSqlGenerator(
        MigrationsSqlGeneratorDependencies dependencies,
        IRelationalAnnotationProvider migrationsAnnotations)
        : base(dependencies, migrationsAnnotations)
    {
    }

    public override IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
    {
        bool previous = _isGeneratingScript;
        _isGeneratingScript = options.HasFlag(MigrationsSqlGenerationOptions.Script);
        try
        {
            return base.Generate(operations, model, options);
        }
        finally
        {
            _isGeneratingScript = previous;
        }
    }

    protected override void Generate(
        MigrationOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        if (operation is EnsureLegacySqliteVacancyColumnsOperation)
        {
            GenerateLegacyVacancyColumns(builder);
            return;
        }

        base.Generate(operation, model, builder);
    }

    private void GenerateLegacyVacancyColumns(MigrationCommandListBuilder builder)
    {
        if (_isGeneratingScript)
        {
            return;
        }

        HashSet<string> existingColumns = ReadExistingVacancyColumns();
        if (existingColumns.Count == 0)
        {
            return;
        }

        (string Column, string Sql)[] requiredColumns =
        [
            ("CategoryGroup", @"ALTER TABLE ""Vacancies"" ADD COLUMN ""CategoryGroup"" TEXT NULL;"),
            ("FullAddress", @"ALTER TABLE ""Vacancies"" ADD COLUMN ""FullAddress"" TEXT NULL;"),
            ("Schedule", @"ALTER TABLE ""Vacancies"" ADD COLUMN ""Schedule"" TEXT NULL;"),
            ("Experience", @"ALTER TABLE ""Vacancies"" ADD COLUMN ""Experience"" TEXT NULL;"),
            ("ExperienceMonths", @"ALTER TABLE ""Vacancies"" ADD COLUMN ""ExperienceMonths"" INTEGER NULL;"),
            ("WorkFormat", @"ALTER TABLE ""Vacancies"" ADD COLUMN ""WorkFormat"" INTEGER NOT NULL DEFAULT 0;"),
            ("SearchText", @"ALTER TABLE ""Vacancies"" ADD COLUMN ""SearchText"" TEXT NOT NULL DEFAULT '';"),
            ("ExternalId", @"ALTER TABLE ""Vacancies"" ADD COLUMN ""ExternalId"" TEXT NULL;")
        ];

        foreach ((string column, string sql) in requiredColumns)
        {
            if (!existingColumns.Contains(column))
            {
                builder.Append(sql).EndCommand();
            }
        }
    }

    private HashSet<string> ReadExistingVacancyColumns()
    {
        HashSet<string> existingColumns = new(StringComparer.OrdinalIgnoreCase);
        DbConnection connection = Dependencies.CurrentContext.Context.Database.GetDbConnection();
        bool shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            using DbCommand pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA table_info('Vacancies');";
            using DbDataReader reader = pragma.ExecuteReader();
            while (reader.Read())
            {
                existingColumns.Add(reader.GetString(1));
            }
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }

        return existingColumns;
    }
}
