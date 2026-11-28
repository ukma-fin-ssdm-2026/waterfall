using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace waterfall.Repository.Migrations
{
    /// <inheritdoc />
    public partial class RestoreSavedVacanciesIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.IsSqlite())
            {
                migrationBuilder.Sql("""
                    DELETE FROM "SavedVacancies"
                    WHERE "UserId" IS NULL
                       OR NOT EXISTS (SELECT 1 FROM "Users" u WHERE u."Id" = "SavedVacancies"."UserId")
                       OR NOT EXISTS (SELECT 1 FROM "Vacancies" v WHERE v."Id" = "SavedVacancies"."VacancyId")
                       OR EXISTS (
                           SELECT 1 FROM "SavedVacancies" newer
                           WHERE newer."UserId" = "SavedVacancies"."UserId"
                             AND newer."VacancyId" = "SavedVacancies"."VacancyId"
                             AND (newer."SavedAt" > "SavedVacancies"."SavedAt"
                                  OR (newer."SavedAt" = "SavedVacancies"."SavedAt" AND newer.rowid > "SavedVacancies".rowid))
                       );
                    DELETE FROM "UserPersonalData"
                    WHERE NOT EXISTS (SELECT 1 FROM "Users" u WHERE u."Id" = "UserPersonalData"."UserId");
                    """);
            }
            else if (migrationBuilder.IsSqlServer())
            {
                migrationBuilder.Sql("""
                    DELETE FROM [SavedVacancies]
                    WHERE [UserId] IS NULL
                       OR NOT EXISTS (SELECT 1 FROM [Users] u WHERE u.[Id] = [SavedVacancies].[UserId])
                       OR NOT EXISTS (SELECT 1 FROM [Vacancies] v WHERE v.[Id] = [SavedVacancies].[VacancyId])
                       OR EXISTS (
                           SELECT 1 FROM [SavedVacancies] newer
                           WHERE newer.[UserId] = [SavedVacancies].[UserId]
                             AND newer.[VacancyId] = [SavedVacancies].[VacancyId]
                             AND (newer.[SavedAt] > [SavedVacancies].[SavedAt]
                                  OR (newer.[SavedAt] = [SavedVacancies].[SavedAt] AND newer.[Id] > [SavedVacancies].[Id]))
                       );
                    DELETE FROM [UserPersonalData]
                    WHERE NOT EXISTS (SELECT 1 FROM [Users] u WHERE u.[Id] = [UserPersonalData].[UserId]);
                    """);
            }

            string guidColumnType = migrationBuilder.IsSqlServer() ? "uniqueidentifier" : "TEXT";

            migrationBuilder.DropPrimaryKey(
                name: "PK_SavedVacancies",
                table: "SavedVacancies");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "SavedVacancies");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "SavedVacancies",
                type: guidColumnType,
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: guidColumnType,
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_SavedVacancies",
                table: "SavedVacancies",
                columns: new[] { "UserId", "VacancyId" });

            migrationBuilder.AddForeignKey(
                name: "FK_SavedVacancies_Users_UserId",
                table: "SavedVacancies",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPersonalData_Users_UserId",
                table: "UserPersonalData",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            string guidColumnType = migrationBuilder.IsSqlServer() ? "uniqueidentifier" : "TEXT";

            migrationBuilder.DropForeignKey(
                name: "FK_SavedVacancies_Users_UserId",
                table: "SavedVacancies");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPersonalData_Users_UserId",
                table: "UserPersonalData");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SavedVacancies",
                table: "SavedVacancies");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "SavedVacancies",
                type: guidColumnType,
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: guidColumnType);

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "SavedVacancies",
                type: guidColumnType,
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "PK_SavedVacancies",
                table: "SavedVacancies",
                column: "Id");
        }
    }
}
