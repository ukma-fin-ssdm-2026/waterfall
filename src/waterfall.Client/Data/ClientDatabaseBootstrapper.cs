using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Waterfall.Client.Data;

/// <summary>
/// Ініціалізує та ідемпотентно оновлює схему локальної SQLite-бази клієнта
/// з використанням <c>PRAGMA user_version</c> замість розкиданого по сервісах raw DDL.
/// </summary>
public static class ClientDatabaseBootstrapper
{
    public const int CurrentSchemaVersion = 3;

    private static readonly ConcurrentDictionary<string, byte> s_initializedDatabases =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>
    /// Гарантує, що локальна база клієнта створена та приведена до <see cref="CurrentSchemaVersion"/>.
    /// Безпечно працює при одночасному виклику з кількох сервісів і для різних шляхів БД.
    /// </summary>
    public static async Task EnsureInitializedAsync(ClientWaterfallDbContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        DbConnection connection = context.Database.GetDbConnection();
        if (TryGetFileCacheKey(connection, out string cacheKey)
            && s_initializedDatabases.ContainsKey(cacheKey)
            && File.Exists(cacheKey))
        {
            return;
        }

        await ClientWaterfallDbContext.SharedInitLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (TryGetFileCacheKey(connection, out cacheKey))
            {
                if (s_initializedDatabases.ContainsKey(cacheKey) && File.Exists(cacheKey))
                {
                    return;
                }

                s_initializedDatabases.TryRemove(cacheKey, out _);
            }

            bool shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
            }

            try
            {
                int version = await ReadSchemaVersionAsync(connection, ct).ConfigureAwait(false);

                if (version == CurrentSchemaVersion)
                {
                    if (!string.IsNullOrEmpty(cacheKey))
                    {
                        s_initializedDatabases.TryAdd(cacheKey, 0);
                    }

                    return;
                }

                if (version > CurrentSchemaVersion)
                {
                    await RebuildLocalCacheDatabaseAsync(context, connection, shouldClose, ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(cacheKey))
                    {
                        s_initializedDatabases.TryAdd(cacheKey, 0);
                    }

                    return;
                }

                if (version == 0)
                {
                    bool hasVacanciesTable = await TableExistsAsync(connection, "Vacancies", ct).ConfigureAwait(false);
                    if (!hasVacanciesTable)
                    {
                        await context.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
                        await SetSchemaVersionAsync(context, 1, ct).ConfigureAwait(false);
                        version = 1;
                    }
                    else
                    {
                        await UpgradeFrom0To1Async(context, connection, ct).ConfigureAwait(false);
                        await SetSchemaVersionAsync(context, 1, ct).ConfigureAwait(false);
                        version = 1;
                    }
                }

                while (version < CurrentSchemaVersion)
                {
                    version = await ApplyUpgradeStepAsync(context, connection, version, ct).ConfigureAwait(false);
                    await SetSchemaVersionAsync(context, version, ct).ConfigureAwait(false);
                }

                if (!string.IsNullOrEmpty(cacheKey))
                {
                    s_initializedDatabases.TryAdd(cacheKey, 0);
                }
            }
            finally
            {
                if (shouldClose && connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            ClientWaterfallDbContext.SharedInitLock.Release();
        }
    }

    private static bool TryGetFileCacheKey(DbConnection connection, out string cacheKey)
    {
        string dataSource = connection.DataSource;
        string connectionString = connection.ConnectionString;

        if (string.IsNullOrWhiteSpace(dataSource)
            || string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase)
            || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            cacheKey = string.Empty;
            return false;
        }

        cacheKey = Path.GetFullPath(dataSource);
        return true;
    }

    private static async Task<int> ReadSchemaVersionAsync(DbConnection connection, CancellationToken ct)
    {
        await using DbCommand cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        object? raw = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(raw);
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, string tableName, CancellationToken ct)
    {
        await using DbCommand cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        DbParameter param = cmd.CreateParameter();
        param.ParameterName = "$name";
        param.Value = tableName;
        cmd.Parameters.Add(param);

        object? raw = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(raw) > 0;
    }

    private static async Task SetSchemaVersionAsync(ClientWaterfallDbContext context, int version, CancellationToken ct)
    {
        switch (version)
        {
            case 1:
                await context.Database.ExecuteSqlRawAsync("PRAGMA user_version = 1;", ct).ConfigureAwait(false);
                break;
            case 2:
                await context.Database.ExecuteSqlRawAsync("PRAGMA user_version = 2;", ct).ConfigureAwait(false);
                break;
            case 3:
                await context.Database.ExecuteSqlRawAsync("PRAGMA user_version = 3;", ct).ConfigureAwait(false);
                break;
            default:
                throw new NotSupportedException($"Непідтримувана версія схеми клієнта: {version}.");
        }
    }

    private static async Task<int> ApplyUpgradeStepAsync(
        ClientWaterfallDbContext context,
        DbConnection connection,
        int currentVersion,
        CancellationToken ct)
    {
        switch (currentVersion)
        {
            case 1:
                await context.Database.ExecuteSqlRawAsync(
                    """
                    DROP TABLE IF EXISTS "SavedVacancies";
                    DROP TABLE IF EXISTS "Users";
                    DROP TABLE IF EXISTS "UserPersonalData";
                    """, ct).ConfigureAwait(false);
                return 2;
            case 2:
                await context.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE "Vacancies"
                    SET "SourceUrl" = SUBSTR("SourceUrl", 1, 450)
                    WHERE LENGTH("SourceUrl") > 450;

                    DELETE FROM "Vacancies"
                    WHERE "ExternalId" IS NOT NULL
                      AND EXISTS (
                          SELECT 1 FROM "Vacancies" newer
                          WHERE newer."Source" = "Vacancies"."Source"
                            AND newer."ExternalId" = "Vacancies"."ExternalId"
                            AND (newer."LastSeenAt" > "Vacancies"."LastSeenAt"
                                 OR (newer."LastSeenAt" = "Vacancies"."LastSeenAt" AND newer.rowid > "Vacancies".rowid))
                      );

                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_Vacancies_Source_ExternalId"
                        ON "Vacancies" ("Source", "ExternalId")
                        WHERE "ExternalId" IS NOT NULL;
                    CREATE INDEX IF NOT EXISTS "IX_Vacancies_SourceUrl"
                        ON "Vacancies" ("SourceUrl");
                    """, ct).ConfigureAwait(false);
                return 3;
            default:
                throw new NotSupportedException($"Непідтримуваний перехід схеми клієнта з версії {currentVersion}.");
        }
    }

    private static async Task RebuildLocalCacheDatabaseAsync(
        ClientWaterfallDbContext context,
        DbConnection connection,
        bool reopenOnExit,
        CancellationToken ct)
    {
        if (TryGetFileCacheKey(connection, out _))
        {
            if (connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }

            if (connection is SqliteConnection sqliteConn)
            {
                SqliteConnection.ClearPool(sqliteConn);
            }

            await context.Database.EnsureDeletedAsync(ct).ConfigureAwait(false);
            await context.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);

            if (!reopenOnExit && connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(ct).ConfigureAwait(false);
            }
        }
        else
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"SyncState\";", ct).ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"GeoLocationCache\";", ct).ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"UserPersonalData\";", ct).ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"SavedVacancies\";", ct).ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"Users\";", ct).ConfigureAwait(false);
            await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"Vacancies\";", ct).ConfigureAwait(false);
            await context.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
        }

        await SetSchemaVersionAsync(context, CurrentSchemaVersion, ct).ConfigureAwait(false);
    }

    private static async Task UpgradeFrom0To1Async(
        ClientWaterfallDbContext context,
        DbConnection connection,
        CancellationToken ct)
    {
        HashSet<string> vacancyColumns = await GetTableColumnsAsync(connection, "PRAGMA table_info('Vacancies');", ct).ConfigureAwait(false);
        if (vacancyColumns.Count > 0)
        {
            if (!vacancyColumns.Contains("CategoryGroup"))
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE Vacancies ADD COLUMN CategoryGroup TEXT NULL;", ct).ConfigureAwait(false);
            if (!vacancyColumns.Contains("FullAddress"))
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE Vacancies ADD COLUMN FullAddress TEXT NULL;", ct).ConfigureAwait(false);
            if (!vacancyColumns.Contains("Schedule"))
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE Vacancies ADD COLUMN Schedule TEXT NULL;", ct).ConfigureAwait(false);
            if (!vacancyColumns.Contains("Experience"))
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE Vacancies ADD COLUMN Experience TEXT NULL;", ct).ConfigureAwait(false);
            if (!vacancyColumns.Contains("ExperienceMonths"))
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE Vacancies ADD COLUMN ExperienceMonths INTEGER NULL;", ct).ConfigureAwait(false);
            if (!vacancyColumns.Contains("WorkFormat"))
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE Vacancies ADD COLUMN WorkFormat INTEGER NOT NULL DEFAULT 0;", ct).ConfigureAwait(false);
            if (!vacancyColumns.Contains("SearchText"))
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE Vacancies ADD COLUMN SearchText TEXT NOT NULL DEFAULT '';", ct).ConfigureAwait(false);
            if (!vacancyColumns.Contains("ExternalId"))
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE Vacancies ADD COLUMN ExternalId TEXT NULL;", ct).ConfigureAwait(false);
            if (!vacancyColumns.Contains("IsSynced"))
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE Vacancies ADD COLUMN IsSynced INTEGER NOT NULL DEFAULT 1;", ct).ConfigureAwait(false);
            if (!vacancyColumns.Contains("LocalUpdatedAt"))
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE Vacancies ADD COLUMN LocalUpdatedAt INTEGER NOT NULL DEFAULT 0;", ct).ConfigureAwait(false);

            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE INDEX IF NOT EXISTS "IX_Vacancies_Category" ON "Vacancies" ("Category");
                CREATE INDEX IF NOT EXISTS "IX_Vacancies_CategoryGroup" ON "Vacancies" ("CategoryGroup");
                CREATE INDEX IF NOT EXISTS "IX_Vacancies_PublishedAt" ON "Vacancies" ("PublishedAt");
                CREATE INDEX IF NOT EXISTS "IX_Vacancies_IsActive" ON "Vacancies" ("IsActive");
                CREATE INDEX IF NOT EXISTS "IX_Vacancies_Source" ON "Vacancies" ("Source");
                CREATE INDEX IF NOT EXISTS "IX_Vacancies_IsSynced" ON "Vacancies" ("IsSynced");
                """, ct).ConfigureAwait(false);
        }

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "SyncState" (
                "Key" TEXT NOT NULL CONSTRAINT "PK_SyncState" PRIMARY KEY,
                "LastSyncedAt" INTEGER NULL,
                "LastSummary" TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS "GeoLocationCache" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_GeoLocationCache" PRIMARY KEY,
                "NormalizedAddress" TEXT NOT NULL,
                "Latitude" REAL NOT NULL,
                "Longitude" REAL NOT NULL,
                "CreatedAt" INTEGER NOT NULL,
                "IsSynced" INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS "SavedVacancies" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_SavedVacancies" PRIMARY KEY,
                "VacancyId" TEXT NOT NULL,
                "UserId" TEXT NULL,
                "Notes" TEXT NULL,
                "SavedAt" INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS "Users" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY,
                "Email" TEXT NOT NULL,
                "PasswordHash" TEXT NOT NULL,
                "Role" TEXT NOT NULL,
                "CreatedAt" INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS "UserPersonalData" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_UserPersonalData" PRIMARY KEY,
                "UserId" TEXT NOT NULL,
                "Address" TEXT NULL,
                "UpdatedAt" INTEGER NOT NULL
            );
            """, ct).ConfigureAwait(false);

        HashSet<string> geoColumns = await GetTableColumnsAsync(connection, "PRAGMA table_info('GeoLocationCache');", ct).ConfigureAwait(false);
        if (geoColumns.Count > 0 && !geoColumns.Contains("IsSynced"))
        {
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE GeoLocationCache ADD COLUMN IsSynced INTEGER NOT NULL DEFAULT 1;", ct).ConfigureAwait(false);
        }

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_GeoLocationCache_NormalizedAddress" ON "GeoLocationCache" ("NormalizedAddress");
            CREATE INDEX IF NOT EXISTS "IX_GeoLocationCache_IsSynced" ON "GeoLocationCache" ("IsSynced");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Email" ON "Users" ("Email");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserPersonalData_UserId" ON "UserPersonalData" ("UserId");
            """, ct).ConfigureAwait(false);
    }

    private static async Task<HashSet<string>> GetTableColumnsAsync(
        DbConnection connection,
        string pragmaCommandText,
        CancellationToken ct)
    {
        HashSet<string> columns = new(StringComparer.OrdinalIgnoreCase);
        await using DbCommand pragma = connection.CreateCommand();
        pragma.CommandText = pragmaCommandText;
        await using DbDataReader reader = await pragma.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }
}
