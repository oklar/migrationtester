using System.Globalization;
using System.Text;
using MigrationTester.Schema;

namespace MigrationTester.Seeding;

/// <summary>SQL literals per column type. Every method returns null for types it cannot produce values for.</summary>
internal static class SqlLiterals
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// An ordinary value for the column. Different <paramref name="ordinal"/>s give different values,
    /// so copies of a row can satisfy unique keys (the baseline row uses ordinal 1).
    /// </summary>
    public static string? Value(SqlType type, int ordinal)
    {
        string n = ordinal.ToString(Invariant);
        var date = new DateTime(2000, 1, 1).AddDays(ordinal - 1);
        return type.Name switch
        {
            "tinyint" or "smallint" or "int" or "bigint" or "money" or "smallmoney" or "float" or "real" => n,
            "decimal" or "numeric" => DecimalValue(type, n),
            "bit" => ordinal % 2 == 1 ? "0" : "1",
            "uniqueidentifier" => $"'00000000-0000-0000-0000-{ordinal:D12}'",
            // yyyyMMdd is read the same way under every DATEFORMAT and language setting.
            "date" or "datetime" or "datetime2" or "smalldatetime" => $"'{date.ToString("yyyyMMdd", Invariant)}'",
            "datetimeoffset" => $"'{date.ToString("yyyy-MM-dd", Invariant)}T00:00:00+00:00'",
            "time" => $"'{TimeSpan.FromSeconds(ordinal - 1).ToString(@"hh\:mm\:ss", Invariant)}'",
            "xml" => $"N'<v{n}/>'",
            "binary" or "varbinary" or "image" => $"0x{ordinal % 256:X2}",
            "sql_variant" => NString($"v{n}"),
            "hierarchyid" => $"hierarchyid::Parse('/{n}/')",
            "geography" => $"geography::Point(0, {ordinal % 90}, 4326)",
            "geometry" => $"geometry::Point(0, {n}, 0)",
            _ when type.IsString => NString(Fit($"v{n}", type, ordinal)),
            _ => null
        };
    }

    /// <summary>
    /// A value for a primary or unique key column. Integers start high so they miss the small ids that
    /// HasData seed rows use, both those already in the table and those a later migration inserts.
    /// </summary>
    public static string? KeyValue(SqlType type, int ordinal)
        => KeyOffset(type) is long offset
            ? (offset + ordinal).ToString(Invariant)
            : Value(type, ordinal);

    /// <summary>
    /// Where to move an identity column's counter before seeding, for the same reason as
    /// <see cref="KeyValue"/>; null for identity types it does not apply to.
    /// </summary>
    public static long? KeyOffset(SqlType type) => type.Name switch
    {
        "tinyint" => 100,
        "smallint" => 30_000,
        "int" or "bigint" => 1_000_000,
        "decimal" or "numeric" when (type.Precision ?? 18) - (type.Scale ?? 0) >= 8 => 1_000_000,
        _ => null
    };

    /// <summary>
    /// Where keys start that must differ from every key seeded in another table: ordinary seed rows, from
    /// <see cref="KeyOffset"/> up, stay far below it. Null for types without one.
    /// </summary>
    public static long? SeparateKeyOffset(SqlType type) => type.Name switch
    {
        "tinyint" => 200,
        "smallint" => 32_000,
        "int" or "bigint" => 2_000_000,
        "decimal" or "numeric" when (type.Precision ?? 18) - (type.Scale ?? 0) >= 8 => 2_000_000,
        _ => null
    };

    /// <summary>
    /// A key value no ordinary seed row of any table holds, for a row whose key must differ from another table's
    /// seeded keys: from <see cref="SeparateKeyOffset"/> up, a GUID with a fourth group of 0001, or a string that
    /// starts with w. Null when the type cannot hold one.
    /// </summary>
    public static string? SeparateKeyValue(SqlType type, int ordinal)
    {
        string n = ordinal.ToString(Invariant);
        if (SeparateKeyOffset(type) is long offset)
        {
            long value = offset + ordinal;
            return value <= type.Name switch { "tinyint" => byte.MaxValue, "smallint" => short.MaxValue, _ => int.MaxValue }
                ? value.ToString(Invariant)
                : null;
        }

        return type.Name switch
        {
            "uniqueidentifier" => $"'00000000-0000-0000-0001-{ordinal:D12}'",
            _ when type.IsString && (type.Length is not int max || max == SqlType.Max || max > n.Length) => NString($"w{n}"),
            _ => null
        };
    }

    /// <summary>A value no principal row holds: the type's maximum, or an out-of-pattern string.</summary>
    public static string? OrphanValue(SqlType type) => type.Name switch
    {
        "tinyint" => "255",
        "smallint" => "32767",
        "int" => "2147483647",
        "bigint" => "9223372036854775807",
        "decimal" or "numeric" => DecimalMaximum(type),
        "smallmoney" => "214748",
        "money" => "922337203685477",
        "float" or "real" => "1E30",
        "uniqueidentifier" => "'FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF'",
        "date" or "datetime" or "datetime2" => "'99991231'",
        "smalldatetime" => "'20790101'",
        "datetimeoffset" => "'9999-12-31T00:00:00+00:00'",
        "time" => "'23:59:59'",
        "binary" or "varbinary" => "0xFF",
        _ when type.IsString => NString(Fit("~orphan~", type, 1, fallback: '~')),
        _ => null
    };

    /// <summary>A value of exactly <paramref name="length"/> characters (bytes for binary).</summary>
    public static string? LongValue(SqlType type, int length)
    {
        string n = length.ToString(Invariant);
        if (type.IsUnicodeString)
        {
            return $"REPLICATE(CAST(N'x' AS nvarchar(max)), {n})";
        }

        if (type.IsString)
        {
            return $"REPLICATE(CAST('x' AS varchar(max)), {n})";
        }

        return type.IsBinary ? $"CAST(REPLICATE(CAST('x' AS varchar(max)), {n}) AS varbinary(max))" : null;
    }

    public static string NString(string s)
    {
        var sb = new StringBuilder("N'");
        foreach (char ch in s)
        {
            sb.Append(ch == '\'' ? "''" : ch);
        }

        sb.Append('\'');
        return sb.ToString();
    }

    private static string Fit(string value, SqlType type, int ordinal, char? fallback = null)
    {
        if (type.Length is not int max || max == SqlType.Max || value.Length <= max)
        {
            return value;
        }

        // Too short for the usual value: one character, still distinct per ordinal.
        return max >= 1 ? (fallback ?? (char)('a' + (ordinal - 1) % 26)).ToString() : "";
    }

    private static string? DecimalValue(SqlType type, string n)
    {
        int scale = type.Scale ?? 0;
        int integerDigits = (type.Precision ?? 18) - scale;
        if (n.Length <= integerDigits)
        {
            return n;
        }

        // e.g. decimal(2,2) only holds fractions.
        return n.Length <= scale ? "0." + n.PadLeft(scale, '0') : null;
    }

    private static string DecimalMaximum(SqlType type)
    {
        int scale = type.Scale ?? 0;
        int integerDigits = (type.Precision ?? 18) - scale;
        return integerDigits > 0 ? new string('9', integerDigits) : "0." + new string('9', scale);
    }
}
