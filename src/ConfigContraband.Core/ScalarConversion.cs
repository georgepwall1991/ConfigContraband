using System;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ConfigContraband;

/// <summary>
/// Decides whether an appsettings scalar value provably cannot be bound to a target CLR property
/// type by the runtime <c>ConfigurationBinder</c> (which converts every value through
/// <c>TypeDescriptor.GetConverter(type).ConvertFromInvariantString(value)</c> under the invariant
/// culture). Biased hard to the safe side: null/non-scalar values, unsupported target types,
/// and any parsing ambiguity all return <c>false</c> (do not report), so the rule only fires on a
/// provable failure. Where the invariant <c>TryParse</c> is stricter than the runtime converter
/// (hex integers, char padding, enum comma-lists, empty date strings) the check is deliberately
/// widened so it never reports a value the binder would actually accept.
/// </summary>
internal static class ScalarConversion
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // The runtime integral/floating converters use NumberStyles.Integer / NumberStyles.Float and
    // reject thousands separators, so a value like "8,080" genuinely throws and must be reported.
    private const NumberStyles IntegerStyles = NumberStyles.Integer;
    private const NumberStyles FloatStyles = NumberStyles.Float;

    public static bool IsProvablyNotConvertible(
        ITypeSymbol? target,
        ScalarKind kind,
        string? rawValue
    )
    {
        if (target is null)
        {
            return false;
        }

        // Non-scalar (object/array/malformed) and JSON null are other rules' concerns.
        if (kind is ScalarKind.None or ScalarKind.Null)
        {
            return false;
        }

        if (rawValue is null)
        {
            return false;
        }

        var value = rawValue;
        var isNullableTarget = target is INamedTypeSymbol nullableTarget &&
            nullableTarget.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        var type = UnwrapNullable(target);
        if ((isNullableTarget && value.Length == 0) ||
            (string.IsNullOrWhiteSpace(value) &&
             (type.SpecialType == SpecialType.System_Char ||
              type.ToDisplayString() is "System.DateTime" or "System.DateTimeOffset")))
        {
            // NullableConverter maps exactly empty text to null but delegates whitespace to the
            // underlying converter. CharConverter and the date/time converters accept both forms.
            return false;
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            return !IsConvertibleToEnum(enumType, value);
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
                return !bool.TryParse(value.Trim(), out _);
            case SpecialType.System_Char:
                // The runtime CharConverter returns '\0' for "" and trims longer strings to their
                // first char, so only a trimmed multi-character value genuinely throws.
                return value.Trim().Length > 1;
            case SpecialType.System_SByte:
                return !IsConvertibleInteger(
                    value,
                    s => sbyte.TryParse(s, IntegerStyles, Invariant, out _),
                    s => sbyte.TryParse(s, NumberStyles.HexNumber, Invariant, out _)
                );
            case SpecialType.System_Byte:
                return !IsConvertibleInteger(
                    value,
                    s => byte.TryParse(s, IntegerStyles, Invariant, out _),
                    s => byte.TryParse(s, NumberStyles.HexNumber, Invariant, out _)
                );
            case SpecialType.System_Int16:
                return !IsConvertibleInteger(
                    value,
                    s => short.TryParse(s, IntegerStyles, Invariant, out _),
                    s => short.TryParse(s, NumberStyles.HexNumber, Invariant, out _)
                );
            case SpecialType.System_UInt16:
                return !IsConvertibleInteger(
                    value,
                    s => ushort.TryParse(s, IntegerStyles, Invariant, out _),
                    s => ushort.TryParse(s, NumberStyles.HexNumber, Invariant, out _)
                );
            case SpecialType.System_Int32:
                return !IsConvertibleInteger(
                    value,
                    s => int.TryParse(s, IntegerStyles, Invariant, out _),
                    s => int.TryParse(s, NumberStyles.HexNumber, Invariant, out _)
                );
            case SpecialType.System_UInt32:
                return !IsConvertibleInteger(
                    value,
                    s => uint.TryParse(s, IntegerStyles, Invariant, out _),
                    s => uint.TryParse(s, NumberStyles.HexNumber, Invariant, out _)
                );
            case SpecialType.System_Int64:
                return !IsConvertibleInteger(
                    value,
                    s => long.TryParse(s, IntegerStyles, Invariant, out _),
                    s => long.TryParse(s, NumberStyles.HexNumber, Invariant, out _)
                );
            case SpecialType.System_UInt64:
                return !IsConvertibleInteger(
                    value,
                    s => ulong.TryParse(s, IntegerStyles, Invariant, out _),
                    s => ulong.TryParse(s, NumberStyles.HexNumber, Invariant, out _)
                );
            case SpecialType.System_Single:
                return !float.TryParse(value, FloatStyles, Invariant, out _);
            case SpecialType.System_Double:
                return !double.TryParse(value, FloatStyles, Invariant, out _);
            case SpecialType.System_Decimal:
                return !decimal.TryParse(value, FloatStyles, Invariant, out _);
        }

        switch (type.ToDisplayString())
        {
            case "System.Guid":
                return !Guid.TryParse(value, out _);
            case "System.TimeSpan":
                return !TimeSpan.TryParse(value, Invariant, out _);
            case "System.DateTime":
                return !DateTime.TryParse(value, Invariant, DateTimeStyles.None, out _);
            case "System.DateTimeOffset":
                return !DateTimeOffset.TryParse(value, Invariant, DateTimeStyles.None, out _);
        }

        // Not a convertible scalar target (string, object, class, collection, dictionary, ...).
        return false;
    }

    private static bool IsConvertibleInteger(
        string value,
        Func<string, bool> tryDecimal,
        Func<string, bool> tryHex
    )
    {
        var trimmed = value.Trim();
        if (tryDecimal(trimmed))
        {
            return true;
        }

        // The runtime integral converters also accept "0x"/"0X"/"#"-prefixed hex.
        var hex = StripHexPrefix(trimmed);
        return hex is not null && tryHex(hex);
    }

    private static string? StripHexPrefix(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && value.Length > 2)
        {
            return value.Substring(2);
        }

        if (value.StartsWith("#", StringComparison.Ordinal) && value.Length > 1)
        {
            return value.Substring(1);
        }

        return null;
    }

    private static bool IsConvertibleToEnum(INamedTypeSymbol enumType, string value)
    {
        var memberNames = enumType
            .GetMembers()
            .OfType<IFieldSymbol>()
            .Where(field => field.IsConst)
            .Select(field => field.Name)
            .ToArray();

        // Enum.Parse accepts comma-separated flag combinations and any value parseable to the
        // underlying integral type (even undefined members), so every token must be a member name
        // or a numeric literal for the value to be convertible.
        foreach (var token in value.Split(','))
        {
            var trimmed = token.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            if (
                memberNames.Any(name =>
                    string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                continue;
            }

            if (IsIntegerLiteral(enumType, trimmed))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsIntegerLiteral(INamedTypeSymbol enumType, string value)
    {
        return enumType.EnumUnderlyingType?.SpecialType switch
        {
            SpecialType.System_SByte => sbyte.TryParse(value, IntegerStyles, Invariant, out _),
            SpecialType.System_Byte => byte.TryParse(value, IntegerStyles, Invariant, out _),
            SpecialType.System_Int16 => short.TryParse(value, IntegerStyles, Invariant, out _),
            SpecialType.System_UInt16 => ushort.TryParse(value, IntegerStyles, Invariant, out _),
            SpecialType.System_Int32 => int.TryParse(value, IntegerStyles, Invariant, out _),
            SpecialType.System_UInt32 => uint.TryParse(value, IntegerStyles, Invariant, out _),
            SpecialType.System_Int64 => long.TryParse(value, IntegerStyles, Invariant, out _),
            SpecialType.System_UInt64 => ulong.TryParse(value, IntegerStyles, Invariant, out _),
            _ => false,
        };
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        if (
            type is INamedTypeSymbol named
            && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && named.TypeArguments.Length == 1
        )
        {
            return named.TypeArguments[0];
        }

        return type;
    }
}
