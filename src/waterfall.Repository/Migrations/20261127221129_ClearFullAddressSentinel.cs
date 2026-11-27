using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace waterfall.Repository.Migrations
{
    /// <inheritdoc />
    public partial class ClearFullAddressSentinel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.IsSqlite())
            {
                migrationBuilder.Sql("""
                    UPDATE "Vacancies"
                    SET "FullAddress" = NULL
                    WHERE "FullAddress" LIKE '%не вказано точну адресу%'
                       OR "FullAddress" LIKE '%(Головний офіс)%'
                       OR TRIM("FullAddress") = '';
                    """);
            }
            else if (migrationBuilder.IsSqlServer())
            {
                migrationBuilder.Sql("""
                    UPDATE [Vacancies]
                    SET [FullAddress] = NULL
                    WHERE [FullAddress] LIKE N'%не вказано точну адресу%'
                       OR [FullAddress] LIKE N'%(Головний офіс)%'
                       OR LTRIM(RTRIM([FullAddress])) = N'';
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
