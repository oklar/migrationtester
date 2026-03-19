using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MigrationRepair.Capture;

/// <summary>
/// Extracts <see cref="MigrationOperation"/> instances produced by a migration's <c>Up</c> method.
/// </summary>
public static class MigrationOperationCapture
{
    private static readonly Action<Migration, MigrationBuilder> InvokeUp = CreateUpInvoker();

    private static Action<Migration, MigrationBuilder> CreateUpInvoker()
    {
        var mi = typeof(Migration).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (mi is null)
        {
            throw new InvalidOperationException("Could not resolve Migration.Up via reflection.");
        }

        return (migration, builder) =>
        {
            try
            {
                mi.Invoke(migration, [builder]);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
        };
    }

    /// <summary>
    /// Invokes <see cref="Migration.Up"/> and returns captured operations. Uses reflection over
    /// <see cref="MigrationBuilder"/> internal storage; EF Core version differences may require updates.
    /// </summary>
    public static IReadOnlyList<MigrationOperation> Capture(Migration migration, string activeProvider)
    {
        ArgumentNullException.ThrowIfNull(migration);
        ArgumentException.ThrowIfNullOrEmpty(activeProvider);

        var builder = new MigrationBuilder(activeProvider);
        InvokeUp(migration, builder);

        return builder.Operations.ToList();
    }

    /// <summary>
    /// Returns true if the captured operations include raw SQL.
    /// </summary>
    public static bool ContainsRawSql(IReadOnlyList<MigrationOperation> operations)
        => operations.Any(static o => o is SqlOperation);

    /// <summary>
    /// Returns true if <see cref="Migration.Up"/> records at least one <see cref="SqlOperation"/>.
    /// </summary>
    public static bool ContainsRawSql(Migration migration, string activeProvider)
        => ContainsRawSql(Capture(migration, activeProvider));
}
