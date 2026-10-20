using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MigrationRepair.Runner;

internal static class ActiveProviderResolver
{
    /// <summary>Provider key for <see cref="Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder"/> (SQL Server).</summary>
    public const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";

    public static string Resolve(DbContext context)
    {
        var opts = context.GetService<IDbContextOptions>();
        foreach (var ext in opts.Extensions)
        {
            string n = ext.GetType().FullName ?? "";
            if (n.Contains("SqlServer", StringComparison.Ordinal))
            {
                return SqlServerProvider;
            }
        }

        throw new InvalidOperationException(
            "No SQL Server provider extension found on this DbContext. MigrationRepair currently supports SQL Server only.");
    }
}
