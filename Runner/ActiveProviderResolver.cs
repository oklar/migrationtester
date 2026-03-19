using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MigrationRepair.Runner;

public static class ActiveProviderResolver
{
    public static string Resolve(DbContext context)
    {
        var opts = context.GetService<IDbContextOptions>();
        foreach (var ext in opts.Extensions)
        {
            var n = ext.GetType().FullName ?? "";
            if (n.Contains("Sqlite", StringComparison.Ordinal))
            {
                return "Microsoft.EntityFrameworkCore.Sqlite";
            }

            if (n.Contains("MySql", StringComparison.Ordinal) || n.Contains("MySQL", StringComparison.Ordinal))
            {
                return "Pomelo.EntityFrameworkCore.MySql";
            }

            if (n.Contains("Npgsql", StringComparison.Ordinal))
            {
                return "Npgsql.EntityFrameworkCore.PostgreSQL";
            }

            if (n.Contains("SqlServer", StringComparison.Ordinal))
            {
                return "Microsoft.EntityFrameworkCore.SqlServer";
            }
        }

        return "Microsoft.EntityFrameworkCore.Sqlite";
    }

    public static Hints.DatabaseFamily DetectFamily(string activeProvider)
    {
        if (activeProvider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        {
            return Hints.DatabaseFamily.MySql;
        }

        if (activeProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return Hints.DatabaseFamily.SqlServer;
        }

        if (activeProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return Hints.DatabaseFamily.PostgreSql;
        }

        return Hints.DatabaseFamily.Sqlite;
    }
}
