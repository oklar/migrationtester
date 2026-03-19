using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MigrationRepair.Runner;

public static class ActiveProviderResolver
{
    /// <summary>Provider key for <see cref="Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder"/> (SQL Server).</summary>
    public const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";

    public static string Resolve(DbContext context)
    {
        var opts = context.GetService<IDbContextOptions>();
        foreach (var ext in opts.Extensions)
        {
            var n = ext.GetType().FullName ?? "";
            if (n.Contains("SqlServer", StringComparison.Ordinal))
            {
                return SqlServerProvider;
            }
        }

        return SqlServerProvider;
    }
}
