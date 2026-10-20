using System.Globalization;
using System.Numerics;
using MigrationRepair.Schema;

namespace MigrationRepair.Planning;

/// <summary>
/// Which column type changes can fail on existing data under SQL Server's ALTER COLUMN conversion,
/// and a value that proves it. Changes that only lose data silently (datetime2 → date, scale
/// reduction, nvarchar → varchar) are not failures and return null.
/// </summary>
internal static class TypeConversionRules
{
    /// <summary>
    /// A SQL literal that the column accepts as <paramref name="from"/> but that fails conversion to
    /// <paramref name="to"/>, or null when every <paramref name="from"/> value converts.
    /// </summary>
    public static string? FailingValue(SqlType from, SqlType to)
    {
        if (from.IsString && from.Name is not ("text" or "ntext"))
        {
            if (to.IsNumeric || to.IsDateTime || to.Name is "bit" or "uniqueidentifier" or "time")
            {
                return "N'x'";
            }

            return to.Name == "xml" ? "N'<'" : null;
        }

        if (from.IsNumeric && to.IsNumeric)
        {
            return NumericOverflow(from, to);
        }

        if (to.Name == "datetime" && from.Name is "date" or "datetime2" or "datetimeoffset")
        {
            // datetime starts at 1753-01-01.
            return from.Name == "datetimeoffset" ? "'1000-01-01T00:00:00+00:00'" : "'10000101'";
        }

        if (to.Name == "smalldatetime" && from.IsDateTime && from.Name != "smalldatetime")
        {
            // smalldatetime starts at 1900-01-01.
            return from.Name == "datetimeoffset" ? "'1800-01-01T00:00:00+00:00'" : "'18000101'";
        }

        return null;
    }

    private static string? NumericOverflow(SqlType from, SqlType to)
    {
        if (from.Name == "float" && to.Name == "real")
        {
            return "1E39";
        }

        if (MaxIntegerPart(to) is not { } toMax)
        {
            // float/real targets hold any value an exact type can.
            return null;
        }

        BigInteger value = toMax + 1;
        if (MaxIntegerPart(from) is { } fromMax && value > fromMax)
        {
            return null;
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Largest whole number the type holds, or null for float/real.</summary>
    private static BigInteger? MaxIntegerPart(SqlType type) => type.Name switch
    {
        "tinyint" => 255,
        "smallint" => short.MaxValue,
        "int" => int.MaxValue,
        "bigint" => long.MaxValue,
        "smallmoney" => 214_748,
        "money" => 922_337_203_685_477,
        "decimal" or "numeric" => BigInteger.Pow(10, (type.Precision ?? 18) - (type.Scale ?? 0)) - 1,
        _ => null
    };
}
