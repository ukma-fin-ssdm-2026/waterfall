using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using waterfall.Repository.Migrations;

namespace Waterfall.Repository.Data;

/// <summary>
/// Постачає відповідний <see cref="ModelSnapshot"/> для активного провайдера (SQLite або SQL Server),
/// щоб EF Core не повідомляв про фіктивні зміни моделі між провайдерами з різними типами колонок.
/// </summary>
#pragma warning disable EF1001 // Internal EF Core API usage: розширення стандартної MigrationsAssembly для подвійного провайдера
internal sealed class WaterfallMigrationsAssembly : MigrationsAssembly
{
    private readonly string? _providerName;
    private ModelSnapshot? _resolvedSnapshot;

    public WaterfallMigrationsAssembly(
        ICurrentDbContext currentContext,
        IDbContextOptions options,
        IMigrationsIdGenerator idGenerator,
        IDiagnosticsLogger<DbLoggerCategory.Migrations> logger)
        : base(currentContext, options, idGenerator, logger)
    {
        _providerName = currentContext.Context.Database.ProviderName;
    }

    public override ModelSnapshot? ModelSnapshot =>
        _resolvedSnapshot ??= string.Equals(_providerName, "Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal)
            ? new SqlServerWaterfallDbContextModelSnapshot()
            : base.ModelSnapshot;
}
#pragma warning restore EF1001
