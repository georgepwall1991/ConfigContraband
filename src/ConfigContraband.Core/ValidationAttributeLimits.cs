using System;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace ConfigContraband;

/// <summary>
/// Reads DataAnnotations range and length constructor arguments from <see cref="AttributeData"/>.
/// Shared by JSON Schema emission and CFG010 so the two consumers cannot drift on bound parsing.
/// </summary>
internal static class ValidationAttributeLimits
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public const string RangeAttributeName = "System.ComponentModel.DataAnnotations.RangeAttribute";
    public const string MaxLengthAttributeName = "System.ComponentModel.DataAnnotations.MaxLengthAttribute";
    public const string MinLengthAttributeName = "System.ComponentModel.DataAnnotations.MinLengthAttribute";
    public const string StringLengthAttributeName = "System.ComponentModel.DataAnnotations.StringLengthAttribute";
    public const string LengthAttributeName = "System.ComponentModel.DataAnnotations.LengthAttribute";
    public const string AllowedValuesAttributeName = "System.ComponentModel.DataAnnotations.AllowedValuesAttribute";
    public const string DeniedValuesAttributeName = "System.ComponentModel.DataAnnotations.DeniedValuesAttribute";

    public static void ReadRange(
        AttributeData attribute,
        ref string? minimum,
        ref string? maximum,
        ref bool minimumExclusive,
        ref bool maximumExclusive)
    {
        var arguments = attribute.ConstructorArguments;
        switch (arguments.Length)
        {
            case 2:
                // Range(int, int) or Range(double, double).
                var formattedMinimum = FormatNumericConstant(arguments[0]);
                if (formattedMinimum is not null)
                {
                    minimum = formattedMinimum;
                }

                var formattedMaximum = FormatNumericConstant(arguments[1]);
                if (formattedMaximum is not null)
                {
                    maximum = formattedMaximum;
                }

                break;
            case 3:
                // Range(Type, string, string): the operand type plus numeric strings. Bounds are parsed with the
                // invariant culture, NOT the current culture. RangeAttribute defaults to the current culture, but
                // a build-time schema generator cannot know the app's runtime culture, and parsing with the build
                // machine's culture would make the committed schema non-deterministic (breaking `--check` across
                // machines). Invariant parsing is deterministic; a culture-specific bound that does not parse is
                // dropped (the safe under-enforcing direction), matching the recommended ParseLimitsInInvariantCulture.
                var boundType = arguments[0].Value as ITypeSymbol;
                minimum = NormalizeNumericLiteral(arguments[1].Value as string, boundType) ?? minimum;
                maximum = NormalizeNumericLiteral(arguments[2].Value as string, boundType) ?? maximum;
                break;
        }

        ReadExclusiveFlags(attribute, ref minimumExclusive, ref maximumExclusive);
    }

    public static bool TryReadRangeOperands(AttributeData attribute, out RangeOperands operands)
    {
        operands = default;
        var arguments = attribute.ConstructorArguments;
        var minimumExclusive = false;
        var maximumExclusive = false;
        ReadExclusiveFlags(attribute, ref minimumExclusive, ref maximumExclusive);
        var parseInvariant = ReadNamedBoolean(attribute, "ParseLimitsInInvariantCulture");

        if (arguments.Length == 2)
        {
            operands = new RangeOperands(
                arguments[0].Value,
                arguments[1].Value,
                arguments[0].Type,
                minimumExclusive,
                maximumExclusive,
                isTypeStringOverload: false,
                parseLimitsInInvariantCulture: true);
            return true;
        }

        if (arguments.Length == 3 && arguments[0].Value is ITypeSymbol operandType)
        {
            operands = new RangeOperands(
                arguments[1].Value as string,
                arguments[2].Value as string,
                operandType,
                minimumExclusive,
                maximumExclusive,
                isTypeStringOverload: true,
                parseLimitsInInvariantCulture: parseInvariant);
            return true;
        }

        return false;
    }

    public static bool ReadNamedBoolean(AttributeData attribute, string name)
    {
        foreach (var named in attribute.NamedArguments)
        {
            if (string.Equals(named.Key, name, StringComparison.Ordinal) && named.Value.Value is bool value)
            {
                return value;
            }
        }

        return false;
    }

    public static int? ReadIntValue(AttributeData attribute)
    {
        // Negative lengths (e.g. the [MaxLength(-1)] "no maximum" sentinel) are not valid JSON Schema length
        // values and would reject every string, so they are treated as "no constraint".
        if (attribute.ConstructorArguments.Length >= 1 &&
            attribute.ConstructorArguments[0].Value is int value && value >= 0)
        {
            return value;
        }

        return null;
    }

    public static int? ReadStringLengthMaximum(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length >= 1 && attribute.ConstructorArguments[0].Value is int max && max >= 0)
        {
            return max;
        }

        return null;
    }

    public static int ReadStringLengthMinimum(AttributeData attribute)
    {
        foreach (var named in attribute.NamedArguments)
        {
            if (string.Equals(named.Key, "MinimumLength", StringComparison.Ordinal) &&
                named.Value.Value is int minimum &&
                minimum >= 0)
            {
                return minimum;
            }
        }

        return 0;
    }

    public static bool TryReadLength(AttributeData attribute, out int minimum, out int maximum)
    {
        minimum = 0;
        maximum = 0;
        if (attribute.ConstructorArguments.Length >= 2 &&
            attribute.ConstructorArguments[0].Value is int min &&
            attribute.ConstructorArguments[1].Value is int max &&
            min >= 0 &&
            max >= 0 &&
            min <= max)
        {
            minimum = min;
            maximum = max;
            return true;
        }

        return false;
    }

    // DataAnnotations runs every length validator, so combined maxLength bounds tighten rather than
    // overwrite: the effective maximum is the smallest upper bound across all of them.
    public static int? StricterUpperBound(int? existing, int? value)
    {
        if (value is not int candidate)
        {
            return existing;
        }

        return existing is int current ? Math.Min(current, candidate) : candidate;
    }

    private static void ReadExclusiveFlags(
        AttributeData attribute,
        ref bool minimumExclusive,
        ref bool maximumExclusive)
    {
        foreach (var named in attribute.NamedArguments)
        {
            if (string.Equals(named.Key, "MinimumIsExclusive", StringComparison.Ordinal))
            {
                minimumExclusive = named.Value.Value is true;
            }

            if (string.Equals(named.Key, "MaximumIsExclusive", StringComparison.Ordinal))
            {
                maximumExclusive = named.Value.Value is true;
            }
        }
    }

    private static string? FormatNumericConstant(TypedConstant argument)
    {
        // double.PositiveInfinity / NaN are compile-time constants, so [Range(0, double.PositiveInfinity)]
        // is legal and would format as "Infinity"/"NaN" - not valid JSON. Skip non-finite bounds, and
        // validate every formatted token so an invalid JSON number is never written verbatim.
        string? formatted = argument.Value switch
        {
            int value => value.ToString(Invariant),
            double value => FormatFiniteDouble(value),
            _ => null,
        };

        return formatted;
    }

    private static string? FormatFiniteDouble(double value)
    {
        if (double.IsNaN(value))
        {
            return null;
        }

        if (double.IsInfinity(value))
        {
            return null;
        }

        return value.ToString("R", Invariant);
    }

    private static string? NormalizeNumericLiteral(string? literal, ITypeSymbol? boundType)
    {
        if (literal is null)
        {
            return null;
        }

        var trimmed = literal.Trim();

        // Re-format the bound exactly as RangeAttribute parses it for the declared operand type, so the
        // schema mirrors runtime rounding and ranges. RangeAttribute parses "+1", ".5", "1.", "01" with the
        // invariant culture; unparseable or non-finite bounds (e.g. "abc", "NaN") are dropped so an invalid
        // JSON number is never emitted.
        switch (boundType!.SpecialType)
        {
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
                // long covers signed values and unsigned values up to long.MaxValue; ulong covers the rest of
                // the UInt64 range (e.g. "18446744073709551615").
                if (long.TryParse(trimmed, NumberStyles.Integer | NumberStyles.AllowLeadingSign, Invariant, out var signed))
                {
                    return signed.ToString(Invariant);
                }

                if (ulong.TryParse(trimmed, NumberStyles.Integer, Invariant, out var unsigned))
                {
                    return unsigned.ToString(Invariant);
                }

                return null;

            case SpecialType.System_Single:
                if (!float.TryParse(trimmed, NumberStyles.Float, Invariant, out var single))
                {
                    return null;
                }

                if (float.IsNaN(single))
                {
                    return null;
                }

                if (float.IsInfinity(single))
                {
                    return null;
                }

                return ValidJsonNumberOrNull(single.ToString("R", Invariant));

            case SpecialType.System_Double:
                if (!double.TryParse(trimmed, NumberStyles.Float, Invariant, out var dbl))
                {
                    return null;
                }

                if (double.IsNaN(dbl))
                {
                    return null;
                }

                if (double.IsInfinity(dbl))
                {
                    return null;
                }

                return ValidJsonNumberOrNull(dbl.ToString("R", Invariant));

            case SpecialType.System_Decimal:
                // decimal preserves the authored scale (so "100.0" stays "100.0").
                if (!decimal.TryParse(trimmed, NumberStyles.Float, Invariant, out var dec))
                {
                    return null;
                }

                return ValidJsonNumberOrNull(dec.ToString(Invariant));

            default:
                // Unknown operand type: only emit a value already shaped as a valid JSON number.
                return ValidJsonNumberOrNull(trimmed);
        }
    }

    private static string? ValidJsonNumberOrNull(string value)
    {
        if (IsJsonNumber(value))
        {
            return value;
        }

        return null;
    }

    /// <summary>Validates a string against the RFC 8259 JSON number grammar.</summary>
    private static bool IsJsonNumber(string value)
    {
        var index = 0;
        var length = value.Length;
        if (length == 0)
        {
            return false;
        }

        if (value[index] == '-')
        {
            index++;
        }

        // int = "0" | ( digit1-9 *digit ) — no leading "+" and no leading zeros.
        if (index >= length)
        {
            return false;
        }

        if (value[index] == '0')
        {
            index++;
        }
        else if (IsDigit1To9(value[index]))
        {
            index++;
            while (index < length)
            {
                if (!IsDigit(value[index]))
                {
                    break;
                }

                index++;
            }
        }
        else
        {
            return false;
        }

        // frac = "." 1*digit
        if (index < length)
        {
            if (value[index] == '.')
            {
                index++;
                var fractionDigits = 0;
                while (index < length)
                {
                    if (!IsDigit(value[index]))
                    {
                        break;
                    }

                    index++;
                    fractionDigits++;
                }

                if (fractionDigits == 0)
                {
                    return false;
                }
            }
        }

        // exp = ("e" | "E") ["+" | "-"] 1*digit
        if (index < length)
        {
            var exponent = value[index];
            if (exponent == 'e')
            {
                index++;
                if (!TryReadExponentRest(value, ref index, length))
                {
                    return false;
                }
            }
            else if (exponent == 'E')
            {
                index++;
                if (!TryReadExponentRest(value, ref index, length))
                {
                    return false;
                }
            }
        }

        return index == length;
    }

    private static bool TryReadExponentRest(string value, ref int index, int length)
    {
        if (index < length)
        {
            var sign = value[index];
            if (sign == '+')
            {
                index++;
            }
            else if (sign == '-')
            {
                index++;
            }
        }

        var exponentDigits = 0;
        while (index < length)
        {
            if (!IsDigit(value[index]))
            {
                break;
            }

            index++;
            exponentDigits++;
        }

        return exponentDigits > 0;
    }

    private static bool IsDigit1To9(char value)
    {
        if (value < '1')
        {
            return false;
        }

        return value <= '9';
    }

    private static bool IsDigit(char value)
    {
        if (value < '0')
        {
            return false;
        }

        return value <= '9';
    }
}

internal readonly struct RangeOperands
{
    public RangeOperands(
        object? minimum,
        object? maximum,
        ITypeSymbol? operandType,
        bool minimumExclusive,
        bool maximumExclusive,
        bool isTypeStringOverload,
        bool parseLimitsInInvariantCulture)
    {
        Minimum = minimum;
        Maximum = maximum;
        OperandType = operandType;
        MinimumExclusive = minimumExclusive;
        MaximumExclusive = maximumExclusive;
        IsTypeStringOverload = isTypeStringOverload;
        ParseLimitsInInvariantCulture = parseLimitsInInvariantCulture;
    }

    public object? Minimum { get; }
    public object? Maximum { get; }
    public ITypeSymbol? OperandType { get; }
    public bool MinimumExclusive { get; }
    public bool MaximumExclusive { get; }
    public bool IsTypeStringOverload { get; }
    public bool ParseLimitsInInvariantCulture { get; }
}
