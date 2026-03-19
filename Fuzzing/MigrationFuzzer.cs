using Microsoft.EntityFrameworkCore;
using MigrationRepair.Hints;
using MigrationRepair.Schema;
using MigrationRepair.Seeding;

namespace MigrationRepair.Fuzzing;

/// <summary>
/// Optional random inserts validated against the N-1 schema (discards failing attempts).
/// </summary>
public static class MigrationFuzzer
{
    public static async Task RunAsync(
        DbContext context,
        RelationalSchemaModel schema,
        DatabaseFamily family,
        int seed,
        int iterations,
        IList<string> sqlLog,
        CancellationToken cancellationToken)
    {
        if (iterations <= 0)
        {
            return;
        }

        var rnd = new Random(seed);
        var tables = schema.TableNames.ToList();
        if (tables.Count == 0)
        {
            return;
        }

        for (var i = 0; i < iterations; i++)
        {
            var table = tables[rnd.Next(tables.Count)];
            if (!schema.ColumnsByTable.TryGetValue(table, out var cols))
            {
                continue;
            }

            var stringCols = cols
                .Where(static c => c.IsNullable || !c.IsPrimaryKey)
                .Where(static c =>
                    c.DeclaredType?.Contains("CHAR", StringComparison.OrdinalIgnoreCase) == true
                    || c.DeclaredType?.Contains("TEXT", StringComparison.OrdinalIgnoreCase) == true
                    || c.DeclaredType?.Contains("STRING", StringComparison.OrdinalIgnoreCase) == true
                    || string.IsNullOrEmpty(c.DeclaredType))
                .ToList();
            if (stringCols.Count == 0)
            {
                continue;
            }

            var col = stringCols[rnd.Next(stringCols.Count)];
            var len = rnd.Next(1, 48);
            var payload = new string((char)('a' + rnd.Next(0, 26)), len);
            var insertCols = new List<string>();
            var vals = new List<string>();
            foreach (var c in cols.OrderBy(static x => x.Ordinal))
            {
                if (c.IsPrimaryKey && c.IsAutoIncrement)
                {
                    continue;
                }

                insertCols.Add(c.Name);
                if (string.Equals(c.Name, col.Name, StringComparison.OrdinalIgnoreCase))
                {
                    vals.Add(QuoteString(payload));
                }
                else if (c.IsNullable)
                {
                    vals.Add("NULL");
                }
                else
                {
                    vals.Add(DefaultLiteral(c, rnd));
                }
            }

            if (insertCols.Count == 0)
            {
                continue;
            }

            var tableQ = SqlDialect.Q(table, family);
            var colList = string.Join(", ", insertCols.Select(c => SqlDialect.Q(c, family)));
            var sql = $"INSERT INTO {tableQ} ({colList}) VALUES ({string.Join(", ", vals)});";

            await using var tx = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await context.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                sqlLog.Add(sql);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static string DefaultLiteral(TableColumnMetadata c, Random rnd)
    {
        var t = c.DeclaredType ?? "";
        if (t.Contains("INT", StringComparison.OrdinalIgnoreCase)
            && !t.Contains("POINT", StringComparison.OrdinalIgnoreCase))
        {
            return rnd.Next(1, 1_000_000).ToString();
        }

        return QuoteString($"f{rnd.Next()}");
    }

    private static string QuoteString(string s)
    {
        return "'" + s.Replace("'", "''", StringComparison.Ordinal) + "'";
    }
}
