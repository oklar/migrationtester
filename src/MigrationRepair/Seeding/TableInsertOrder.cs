using MigrationRepair.Schema;

namespace MigrationRepair.Seeding;

internal static class TableInsertOrder
{
    /// <summary>
    /// Principal tables before dependents, following only required foreign keys (nullable ones are
    /// seeded as NULL). Tables caught in a cycle of required foreign keys come last, by name, so they
    /// are still attempted and their failure reported.
    /// </summary>
    public static IReadOnlyList<TableMetadata> Topological(IReadOnlyList<TableMetadata> tables)
    {
        var byName = tables.ToDictionary(static t => t.Name);
        var dependents = tables.ToDictionary(static t => t.Name, static _ => new HashSet<TableName>());
        var indegree = tables.ToDictionary(static t => t.Name, static _ => 0);

        foreach (var table in tables)
        {
            foreach (var fk in table.ForeignKeys)
            {
                bool required = fk.Columns.Any(c => table.FindColumn(c) is { IsNullable: false });
                if (!required || fk.PrincipalTable.Equals(table.Name) || !byName.ContainsKey(fk.PrincipalTable))
                {
                    continue;
                }

                if (dependents[fk.PrincipalTable].Add(table.Name))
                {
                    indegree[table.Name]++;
                }
            }
        }

        var queue = new Queue<TableName>(tables.Select(static t => t.Name).Where(n => indegree[n] == 0));
        var result = new List<TableMetadata>(tables.Count);
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            result.Add(byName[name]);
            foreach (var dependent in dependents[name])
            {
                if (--indegree[dependent] == 0)
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        result.AddRange(tables
            .Where(t => indegree[t.Name] > 0)
            .OrderBy(static t => t.Name.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static t => t.Name.Name, StringComparer.OrdinalIgnoreCase));
        return result;
    }
}
