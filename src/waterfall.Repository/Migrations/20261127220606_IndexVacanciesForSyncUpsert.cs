using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace waterfall.Repository.Migrations
{
    /// <inheritdoc />
    public partial class IndexVacanciesForSyncUpsert : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.IsSqlite())
            {
                migrationBuilder.Sql("""
                    UPDATE "Vacancies"
                    SET "SourceUrl" = SUBSTR("SourceUrl", 1, 450)
                    WHERE LENGTH("SourceUrl") > 450;

                    DELETE FROM "ChatMessageVacancy"
                    WHERE "VacancyId" IN (
                        SELECT v."Id"
                        FROM "Vacancies" v
                        WHERE v."ExternalId" IS NOT NULL
                          AND EXISTS (
                              SELECT 1 FROM "Vacancies" newer
                              WHERE newer."Source" = v."Source"
                                AND newer."ExternalId" = v."ExternalId"
                                AND (newer."LastSeenAt" > v."LastSeenAt"
                                     OR (newer."LastSeenAt" = v."LastSeenAt" AND newer.rowid > v.rowid))
                          )
                    );

                    DELETE FROM "Vacancies"
                    WHERE "ExternalId" IS NOT NULL
                      AND EXISTS (
                          SELECT 1 FROM "Vacancies" newer
                          WHERE newer."Source" = "Vacancies"."Source"
                            AND newer."ExternalId" = "Vacancies"."ExternalId"
                            AND (newer."LastSeenAt" > "Vacancies"."LastSeenAt"
                                 OR (newer."LastSeenAt" = "Vacancies"."LastSeenAt" AND newer.rowid > "Vacancies".rowid))
                      );
                    """);
            }
            else if (migrationBuilder.IsSqlServer())
            {
                migrationBuilder.Sql("""
                    UPDATE [Vacancies]
                    SET [SourceUrl] = LEFT([SourceUrl], 450)
                    WHERE LEN([SourceUrl]) > 450;

                    DELETE FROM [ChatMessageVacancy]
                    WHERE [VacancyId] IN (
                        SELECT v.[Id]
                        FROM [Vacancies] v
                        WHERE v.[ExternalId] IS NOT NULL
                          AND EXISTS (
                              SELECT 1 FROM [Vacancies] newer
                              WHERE newer.[Source] = v.[Source]
                                AND newer.[ExternalId] = v.[ExternalId]
                                AND (newer.[LastSeenAt] > v.[LastSeenAt]
                                     OR (newer.[LastSeenAt] = v.[LastSeenAt] AND newer.[Id] > v.[Id]))
                          )
                    );

                    DELETE FROM [Vacancies]
                    WHERE [ExternalId] IS NOT NULL
                      AND EXISTS (
                          SELECT 1 FROM [Vacancies] newer
                          WHERE newer.[Source] = [Vacancies].[Source]
                            AND newer.[ExternalId] = [Vacancies].[ExternalId]
                            AND (newer.[LastSeenAt] > [Vacancies].[LastSeenAt]
                                 OR (newer.[LastSeenAt] = [Vacancies].[LastSeenAt] AND newer.[Id] > [Vacancies].[Id]))
                      );
                    """);

                migrationBuilder.AlterColumn<string>(
                    name: "SourceUrl",
                    table: "Vacancies",
                    type: "nvarchar(450)",
                    maxLength: 450,
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "nvarchar(1000)",
                    oldMaxLength: 1000);
            }

            string externalIdFilter = migrationBuilder.IsSqlServer()
                ? "[ExternalId] IS NOT NULL"
                : "\"ExternalId\" IS NOT NULL";

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_Source_ExternalId",
                table: "Vacancies",
                columns: new[] { "Source", "ExternalId" },
                unique: true,
                filter: externalIdFilter);

            migrationBuilder.CreateIndex(
                name: "IX_Vacancies_SourceUrl",
                table: "Vacancies",
                column: "SourceUrl");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vacancies_Source_ExternalId",
                table: "Vacancies");

            migrationBuilder.DropIndex(
                name: "IX_Vacancies_SourceUrl",
                table: "Vacancies");

            if (migrationBuilder.IsSqlServer())
            {
                migrationBuilder.AlterColumn<string>(
                    name: "SourceUrl",
                    table: "Vacancies",
                    type: "nvarchar(1000)",
                    maxLength: 1000,
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "nvarchar(450)",
                    oldMaxLength: 450);
            }
        }
    }
}
