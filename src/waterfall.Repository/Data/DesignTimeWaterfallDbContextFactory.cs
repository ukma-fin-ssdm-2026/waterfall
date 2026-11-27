using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Waterfall.Repository.Data;

/// <summary>
/// Фабрика контексту для інструментів часу проєктування EF Core (dotnet ef migrations).
/// </summary>
public sealed class DesignTimeWaterfallDbContextFactory : IDesignTimeDbContextFactory<WaterfallDbContext>
{
    public WaterfallDbContext CreateDbContext(string[] args)
    {
        string? provider = args.FirstOrDefault(a =>
                a.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("Mssql", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            ?? Environment.GetEnvironmentVariable("WATERFALL_DB_PROVIDER");

        DbContextOptionsBuilder<WaterfallDbContext> builder = new();

        if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(provider, "Mssql", StringComparison.OrdinalIgnoreCase))
        {
            builder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=WaterfallDesign;Trusted_Connection=True;");
        }
        else
        {
            builder.UseSqlite("Data Source=waterfall_design.db");
        }

        return new WaterfallDbContext(builder.Options);
    }
}
