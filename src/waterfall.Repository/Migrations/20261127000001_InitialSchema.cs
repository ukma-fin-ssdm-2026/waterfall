using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Waterfall.Repository.Data;

#nullable disable

namespace waterfall.Repository.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        private const string SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";
        private const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(migrationBuilder.ActiveProvider, SqlServerProvider, StringComparison.Ordinal))
            {
                UpSqlServer(migrationBuilder);
                return;
            }

            if (string.Equals(migrationBuilder.ActiveProvider, SqliteProvider, StringComparison.Ordinal))
            {
                UpSqlite(migrationBuilder);
                return;
            }

            throw new NotSupportedException($"Непідтримуваний провайдер БД для міграції InitialSchema: '{migrationBuilder.ActiveProvider}'.");
        }

        private static void UpSqlite(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""GeoLocationCache"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_GeoLocationCache"" PRIMARY KEY,
                    ""NormalizedAddress"" TEXT NOT NULL,
                    ""Latitude"" REAL NOT NULL,
                    ""Longitude"" REAL NOT NULL,
                    ""CreatedAt"" INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS ""SiteSearchCache"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_SiteSearchCache"" PRIMARY KEY,
                    ""CacheKey"" TEXT NOT NULL,
                    ""ExternalIdsJson"" TEXT NOT NULL,
                    ""CachedAt"" INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS ""UserPersonalData"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_UserPersonalData"" PRIMARY KEY,
                    ""UserId"" TEXT NOT NULL,
                    ""Address"" TEXT NULL,
                    ""UpdatedAt"" INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS ""Users"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Users"" PRIMARY KEY,
                    ""Email"" TEXT NOT NULL,
                    ""PasswordHash"" TEXT NOT NULL,
                    ""Role"" TEXT NOT NULL,
                    ""CreatedAt"" INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS ""Vacancies"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Vacancies"" PRIMARY KEY,
                    ""Title"" TEXT NOT NULL,
                    ""Company"" TEXT NOT NULL,
                    ""Description"" TEXT NOT NULL,
                    ""Category"" TEXT NOT NULL,
                    ""CategoryGroup"" TEXT NULL,
                    ""Location"" TEXT NOT NULL,
                    ""FullAddress"" TEXT NULL,
                    ""Schedule"" TEXT NULL,
                    ""Experience"" TEXT NULL,
                    ""ExperienceMonths"" INTEGER NULL,
                    ""WorkFormat"" INTEGER NOT NULL,
                    ""SalaryMin"" decimal(18,2) NULL,
                    ""SalaryMax"" decimal(18,2) NULL,
                    ""Currency"" TEXT NOT NULL,
                    ""Source"" TEXT NOT NULL,
                    ""SourceUrl"" TEXT NOT NULL,
                    ""SkillsJson"" TEXT NOT NULL,
                    ""SearchText"" TEXT NOT NULL,
                    ""PublishedAt"" INTEGER NOT NULL,
                    ""FirstSeenAt"" INTEGER NOT NULL,
                    ""LastSeenAt"" INTEGER NOT NULL,
                    ""ExternalId"" TEXT NULL,
                    ""IsActive"" INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS ""ChatSessions"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_ChatSessions"" PRIMARY KEY,
                    ""UserId"" TEXT NOT NULL,
                    ""ContextFilterJson"" TEXT NOT NULL,
                    ""StartedAt"" INTEGER NOT NULL,
                    ""LastActivityAt"" INTEGER NOT NULL,
                    CONSTRAINT ""FK_ChatSessions_Users_UserId"" FOREIGN KEY (""UserId"") REFERENCES ""Users"" (""Id"") ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS ""SearchQueries"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_SearchQueries"" PRIMARY KEY,
                    ""UserId"" TEXT NOT NULL,
                    ""QueryText"" TEXT NOT NULL,
                    ""SearchedAt"" INTEGER NOT NULL,
                    CONSTRAINT ""FK_SearchQueries_Users_UserId"" FOREIGN KEY (""UserId"") REFERENCES ""Users"" (""Id"") ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS ""SavedVacancies"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_SavedVacancies"" PRIMARY KEY,
                    ""VacancyId"" TEXT NOT NULL,
                    ""UserId"" TEXT NULL,
                    ""Notes"" TEXT NULL,
                    ""SavedAt"" INTEGER NOT NULL,
                    CONSTRAINT ""FK_SavedVacancies_Vacancies_VacancyId"" FOREIGN KEY (""VacancyId"") REFERENCES ""Vacancies"" (""Id"") ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS ""VacancyViews"" (
                    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_VacancyViews"" PRIMARY KEY,
                    ""UserId"" TEXT NOT NULL,
                    ""VacancyId"" TEXT NOT NULL,
                    ""ViewedAt"" INTEGER NOT NULL,
                    CONSTRAINT ""FK_VacancyViews_Users_UserId"" FOREIGN KEY (""UserId"") REFERENCES ""Users"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_VacancyViews_Vacancies_VacancyId"" FOREIGN KEY (""VacancyId"") REFERENCES ""Vacancies"" (""Id"") ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS ""ChatMessages"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_ChatMessages"" PRIMARY KEY AUTOINCREMENT,
                    ""SessionId"" TEXT NOT NULL,
                    ""Author"" TEXT NOT NULL,
                    ""Content"" TEXT NOT NULL,
                    ""CreatedAt"" INTEGER NOT NULL,
                    CONSTRAINT ""FK_ChatMessages_ChatSessions_SessionId"" FOREIGN KEY (""SessionId"") REFERENCES ""ChatSessions"" (""Id"") ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS ""ChatMessageVacancy"" (
                    ""MessageId"" INTEGER NOT NULL,
                    ""VacancyId"" TEXT NOT NULL,
                    CONSTRAINT ""PK_ChatMessageVacancy"" PRIMARY KEY (""MessageId"", ""VacancyId""),
                    CONSTRAINT ""FK_ChatMessageVacancy_ChatMessages_MessageId"" FOREIGN KEY (""MessageId"") REFERENCES ""ChatMessages"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_ChatMessageVacancy_Vacancies_VacancyId"" FOREIGN KEY (""VacancyId"") REFERENCES ""Vacancies"" (""Id"") ON DELETE RESTRICT
                );
            ");

            // Ідемпотентне додавання колонок до вже існуючої до-міграційної таблиці Vacancies (через PRAGMA table_info)
            migrationBuilder.Operations.Add(new EnsureLegacySqliteVacancyColumnsOperation());

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_ChatMessages_SessionId"" ON ""ChatMessages"" (""SessionId"");
                CREATE INDEX IF NOT EXISTS ""IX_ChatMessageVacancy_VacancyId"" ON ""ChatMessageVacancy"" (""VacancyId"");
                CREATE INDEX IF NOT EXISTS ""IX_ChatSessions_UserId"" ON ""ChatSessions"" (""UserId"");
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_GeoLocationCache_NormalizedAddress"" ON ""GeoLocationCache"" (""NormalizedAddress"");
                CREATE INDEX IF NOT EXISTS ""IX_SavedVacancies_VacancyId"" ON ""SavedVacancies"" (""VacancyId"");
                CREATE INDEX IF NOT EXISTS ""IX_SearchQueries_UserId_SearchedAt"" ON ""SearchQueries"" (""UserId"", ""SearchedAt"");
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SiteSearchCache_CacheKey"" ON ""SiteSearchCache"" (""CacheKey"");
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_UserPersonalData_UserId"" ON ""UserPersonalData"" (""UserId"");
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Users_Email"" ON ""Users"" (""Email"");
                CREATE INDEX IF NOT EXISTS ""IX_Vacancies_Category"" ON ""Vacancies"" (""Category"");
                CREATE INDEX IF NOT EXISTS ""IX_Vacancies_CategoryGroup"" ON ""Vacancies"" (""CategoryGroup"");
                CREATE INDEX IF NOT EXISTS ""IX_Vacancies_IsActive"" ON ""Vacancies"" (""IsActive"");
                CREATE INDEX IF NOT EXISTS ""IX_Vacancies_PublishedAt"" ON ""Vacancies"" (""PublishedAt"");
                CREATE INDEX IF NOT EXISTS ""IX_Vacancies_Source"" ON ""Vacancies"" (""Source"");
                CREATE INDEX IF NOT EXISTS ""IX_VacancyViews_UserId_VacancyId"" ON ""VacancyViews"" (""UserId"", ""VacancyId"");
                CREATE INDEX IF NOT EXISTS ""IX_VacancyViews_UserId_ViewedAt"" ON ""VacancyViews"" (""UserId"", ""ViewedAt"");
                CREATE INDEX IF NOT EXISTS ""IX_VacancyViews_VacancyId"" ON ""VacancyViews"" (""VacancyId"");
            ");
        }

        private static void UpSqlServer(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF OBJECT_ID(N'[GeoLocationCache]', N'U') IS NULL
                CREATE TABLE [GeoLocationCache] (
                    [Id] uniqueidentifier NOT NULL,
                    [NormalizedAddress] nvarchar(300) NOT NULL,
                    [Latitude] float NOT NULL,
                    [Longitude] float NOT NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_GeoLocationCache] PRIMARY KEY ([Id])
                );

                IF OBJECT_ID(N'[SiteSearchCache]', N'U') IS NULL
                CREATE TABLE [SiteSearchCache] (
                    [Id] uniqueidentifier NOT NULL,
                    [CacheKey] nvarchar(500) NOT NULL,
                    [ExternalIdsJson] nvarchar(max) NOT NULL,
                    [CachedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_SiteSearchCache] PRIMARY KEY ([Id])
                );

                IF OBJECT_ID(N'[UserPersonalData]', N'U') IS NULL
                CREATE TABLE [UserPersonalData] (
                    [Id] uniqueidentifier NOT NULL,
                    [UserId] uniqueidentifier NOT NULL,
                    [Address] nvarchar(300) NULL,
                    [UpdatedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_UserPersonalData] PRIMARY KEY ([Id])
                );

                IF OBJECT_ID(N'[Users]', N'U') IS NULL
                CREATE TABLE [Users] (
                    [Id] uniqueidentifier NOT NULL,
                    [Email] nvarchar(80) NOT NULL,
                    [PasswordHash] nvarchar(1000) NOT NULL,
                    [Role] nvarchar(50) NOT NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
                );

                IF OBJECT_ID(N'[Vacancies]', N'U') IS NULL
                CREATE TABLE [Vacancies] (
                    [Id] uniqueidentifier NOT NULL,
                    [Title] nvarchar(200) NOT NULL,
                    [Company] nvarchar(200) NOT NULL,
                    [Description] nvarchar(max) NOT NULL,
                    [Category] nvarchar(100) NOT NULL,
                    [CategoryGroup] nvarchar(100) NULL,
                    [Location] nvarchar(200) NOT NULL,
                    [FullAddress] nvarchar(300) NULL,
                    [Schedule] nvarchar(100) NULL,
                    [Experience] nvarchar(100) NULL,
                    [ExperienceMonths] int NULL,
                    [WorkFormat] int NOT NULL,
                    [SalaryMin] decimal(18,2) NULL,
                    [SalaryMax] decimal(18,2) NULL,
                    [Currency] nvarchar(10) NOT NULL,
                    [Source] nvarchar(50) NOT NULL,
                    [SourceUrl] nvarchar(1000) NOT NULL,
                    [SkillsJson] nvarchar(max) NOT NULL,
                    [SearchText] nvarchar(max) NOT NULL,
                    [PublishedAt] datetimeoffset NOT NULL,
                    [FirstSeenAt] datetimeoffset NOT NULL,
                    [LastSeenAt] datetimeoffset NOT NULL,
                    [ExternalId] nvarchar(100) NULL,
                    [IsActive] bit NOT NULL,
                    CONSTRAINT [PK_Vacancies] PRIMARY KEY ([Id])
                );

                IF OBJECT_ID(N'[Vacancies]', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'Vacancies', N'CategoryGroup') IS NULL ALTER TABLE [Vacancies] ADD [CategoryGroup] nvarchar(100) NULL;
                    IF COL_LENGTH(N'Vacancies', N'FullAddress') IS NULL ALTER TABLE [Vacancies] ADD [FullAddress] nvarchar(300) NULL;
                    IF COL_LENGTH(N'Vacancies', N'Schedule') IS NULL ALTER TABLE [Vacancies] ADD [Schedule] nvarchar(100) NULL;
                    IF COL_LENGTH(N'Vacancies', N'Experience') IS NULL ALTER TABLE [Vacancies] ADD [Experience] nvarchar(100) NULL;
                    IF COL_LENGTH(N'Vacancies', N'ExperienceMonths') IS NULL ALTER TABLE [Vacancies] ADD [ExperienceMonths] int NULL;
                    IF COL_LENGTH(N'Vacancies', N'WorkFormat') IS NULL ALTER TABLE [Vacancies] ADD [WorkFormat] int NOT NULL CONSTRAINT [DF_Vacancies_WorkFormat] DEFAULT 0;
                    IF COL_LENGTH(N'Vacancies', N'SearchText') IS NULL ALTER TABLE [Vacancies] ADD [SearchText] nvarchar(max) NOT NULL CONSTRAINT [DF_Vacancies_SearchText] DEFAULT N'';
                    IF COL_LENGTH(N'Vacancies', N'ExternalId') IS NULL ALTER TABLE [Vacancies] ADD [ExternalId] nvarchar(100) NULL;
                END;

                IF OBJECT_ID(N'[ChatSessions]', N'U') IS NULL
                CREATE TABLE [ChatSessions] (
                    [Id] uniqueidentifier NOT NULL,
                    [UserId] uniqueidentifier NOT NULL,
                    [ContextFilterJson] nvarchar(max) NOT NULL,
                    [StartedAt] datetimeoffset NOT NULL,
                    [LastActivityAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_ChatSessions] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_ChatSessions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
                );

                IF OBJECT_ID(N'[SearchQueries]', N'U') IS NULL
                CREATE TABLE [SearchQueries] (
                    [Id] uniqueidentifier NOT NULL,
                    [UserId] uniqueidentifier NOT NULL,
                    [QueryText] nvarchar(300) NOT NULL,
                    [SearchedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_SearchQueries] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_SearchQueries_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
                );

                IF OBJECT_ID(N'[SavedVacancies]', N'U') IS NULL
                CREATE TABLE [SavedVacancies] (
                    [Id] uniqueidentifier NOT NULL,
                    [VacancyId] uniqueidentifier NOT NULL,
                    [UserId] uniqueidentifier NULL,
                    [Notes] nvarchar(1000) NULL,
                    [SavedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_SavedVacancies] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_SavedVacancies_Vacancies_VacancyId] FOREIGN KEY ([VacancyId]) REFERENCES [Vacancies] ([Id]) ON DELETE CASCADE
                );

                IF OBJECT_ID(N'[VacancyViews]', N'U') IS NULL
                CREATE TABLE [VacancyViews] (
                    [Id] uniqueidentifier NOT NULL,
                    [UserId] uniqueidentifier NOT NULL,
                    [VacancyId] uniqueidentifier NOT NULL,
                    [ViewedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_VacancyViews] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_VacancyViews_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_VacancyViews_Vacancies_VacancyId] FOREIGN KEY ([VacancyId]) REFERENCES [Vacancies] ([Id]) ON DELETE CASCADE
                );

                IF OBJECT_ID(N'[ChatMessages]', N'U') IS NULL
                CREATE TABLE [ChatMessages] (
                    [Id] bigint NOT NULL IDENTITY,
                    [SessionId] uniqueidentifier NOT NULL,
                    [Author] nvarchar(20) NOT NULL,
                    [Content] nvarchar(max) NOT NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    CONSTRAINT [PK_ChatMessages] PRIMARY KEY ([Id]),
                    CONSTRAINT [FK_ChatMessages_ChatSessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [ChatSessions] ([Id]) ON DELETE CASCADE
                );

                IF OBJECT_ID(N'[ChatMessageVacancy]', N'U') IS NULL
                CREATE TABLE [ChatMessageVacancy] (
                    [MessageId] bigint NOT NULL,
                    [VacancyId] uniqueidentifier NOT NULL,
                    CONSTRAINT [PK_ChatMessageVacancy] PRIMARY KEY ([MessageId], [VacancyId]),
                    CONSTRAINT [FK_ChatMessageVacancy_ChatMessages_MessageId] FOREIGN KEY ([MessageId]) REFERENCES [ChatMessages] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_ChatMessageVacancy_Vacancies_VacancyId] FOREIGN KEY ([VacancyId]) REFERENCES [Vacancies] ([Id]) ON DELETE NO ACTION
                );

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ChatMessages_SessionId' AND object_id = OBJECT_ID(N'[ChatMessages]'))
                CREATE INDEX [IX_ChatMessages_SessionId] ON [ChatMessages] ([SessionId]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ChatMessageVacancy_VacancyId' AND object_id = OBJECT_ID(N'[ChatMessageVacancy]'))
                CREATE INDEX [IX_ChatMessageVacancy_VacancyId] ON [ChatMessageVacancy] ([VacancyId]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ChatSessions_UserId' AND object_id = OBJECT_ID(N'[ChatSessions]'))
                CREATE INDEX [IX_ChatSessions_UserId] ON [ChatSessions] ([UserId]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_GeoLocationCache_NormalizedAddress' AND object_id = OBJECT_ID(N'[GeoLocationCache]'))
                CREATE UNIQUE INDEX [IX_GeoLocationCache_NormalizedAddress] ON [GeoLocationCache] ([NormalizedAddress]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SavedVacancies_VacancyId' AND object_id = OBJECT_ID(N'[SavedVacancies]'))
                CREATE INDEX [IX_SavedVacancies_VacancyId] ON [SavedVacancies] ([VacancyId]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SearchQueries_UserId_SearchedAt' AND object_id = OBJECT_ID(N'[SearchQueries]'))
                CREATE INDEX [IX_SearchQueries_UserId_SearchedAt] ON [SearchQueries] ([UserId], [SearchedAt]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SiteSearchCache_CacheKey' AND object_id = OBJECT_ID(N'[SiteSearchCache]'))
                CREATE UNIQUE INDEX [IX_SiteSearchCache_CacheKey] ON [SiteSearchCache] ([CacheKey]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UserPersonalData_UserId' AND object_id = OBJECT_ID(N'[UserPersonalData]'))
                CREATE UNIQUE INDEX [IX_UserPersonalData_UserId] ON [UserPersonalData] ([UserId]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Users_Email' AND object_id = OBJECT_ID(N'[Users]'))
                CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Vacancies_Category' AND object_id = OBJECT_ID(N'[Vacancies]'))
                CREATE INDEX [IX_Vacancies_Category] ON [Vacancies] ([Category]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Vacancies_CategoryGroup' AND object_id = OBJECT_ID(N'[Vacancies]'))
                CREATE INDEX [IX_Vacancies_CategoryGroup] ON [Vacancies] ([CategoryGroup]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Vacancies_IsActive' AND object_id = OBJECT_ID(N'[Vacancies]'))
                CREATE INDEX [IX_Vacancies_IsActive] ON [Vacancies] ([IsActive]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Vacancies_PublishedAt' AND object_id = OBJECT_ID(N'[Vacancies]'))
                CREATE INDEX [IX_Vacancies_PublishedAt] ON [Vacancies] ([PublishedAt]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Vacancies_Source' AND object_id = OBJECT_ID(N'[Vacancies]'))
                CREATE INDEX [IX_Vacancies_Source] ON [Vacancies] ([Source]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VacancyViews_UserId_VacancyId' AND object_id = OBJECT_ID(N'[VacancyViews]'))
                CREATE INDEX [IX_VacancyViews_UserId_VacancyId] ON [VacancyViews] ([UserId], [VacancyId]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VacancyViews_UserId_ViewedAt' AND object_id = OBJECT_ID(N'[VacancyViews]'))
                CREATE INDEX [IX_VacancyViews_UserId_ViewedAt] ON [VacancyViews] ([UserId], [ViewedAt]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VacancyViews_VacancyId' AND object_id = OBJECT_ID(N'[VacancyViews]'))
                CREATE INDEX [IX_VacancyViews_VacancyId] ON [VacancyViews] ([VacancyId]);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatMessageVacancy");

            migrationBuilder.DropTable(
                name: "GeoLocationCache");

            migrationBuilder.DropTable(
                name: "SavedVacancies");

            migrationBuilder.DropTable(
                name: "SearchQueries");

            migrationBuilder.DropTable(
                name: "SiteSearchCache");

            migrationBuilder.DropTable(
                name: "UserPersonalData");

            migrationBuilder.DropTable(
                name: "VacancyViews");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "Vacancies");

            migrationBuilder.DropTable(
                name: "ChatSessions");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
