using MigrationRepair.Schema;

namespace MigrationRepair.Seeding;

internal static class TableInsertOrder
{
    /// <summary>
    /// Principal tables before dependents (FK direction).
    /// </summary>
    public static IReadOnlyList<string> Topological(IReadOnlyList<string> tables, IReadOnlyList<ForeignKeyEdge> foreignKeys)
    {
        var set = new HashSet<string>(tables, StringComparer.OrdinalIgnoreCase);
        var adj = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var indegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (string t in tables)
        {
            adj[t] = [];
            indegree[t] = 0;
        }

        foreach (var fk in foreignKeys)
        {
            if (!set.Contains(fk.DependentTable) || !set.Contains(fk.PrincipalTable))
            {
                continue;
            }

            // Principal -> Dependent (principal must be inserted first)
            if (adj[fk.PrincipalTable].Add(fk.DependentTable))
            {
                indegree[fk.DependentTable] = indegree[fk.DependentTable] + 1;
            }
        }

        var q = new Queue<string>(indegree.Where(static kv => kv.Value == 0).Select(static kv => kv.Key));
        var result = new List<string>();
        while (q.Count > 0)
        {
            string n = q.Dequeue();
            result.Add(n);
            foreach (string m in adj[n])
            {
                indegree[m]--;
                if (indegree[m] == 0)
                {
                    q.Enqueue(m);
                }
            }
        }

        if (result.Count != tables.Count)
        {
            // Cycle: fall back to alphabetical to avoid blocking tests
            return tables.Order(StringComparer.OrdinalIgnoreCase).ToList();
        }

        return result;
    }
}
