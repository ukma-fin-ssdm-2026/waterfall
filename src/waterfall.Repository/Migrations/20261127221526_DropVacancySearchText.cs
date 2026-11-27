using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace waterfall.Repository.Migrations
{
    /// <inheritdoc />
    public partial class DropVacancySearchText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "Vacancies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "Vacancies",
                type: migrationBuilder.IsSqlServer() ? "nvarchar(max)" : "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
