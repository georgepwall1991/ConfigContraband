using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ConfigContraband;

/// <summary>
/// Decides whether a bound appsettings scalar provably fails a framework DataAnnotations constraint
/// that <c>ValidateDataAnnotations()</c> would evaluate. Biased to the safe side: culture-dependent
/// Range bounds, custom <c>IsValid</c> overrides, and unconvertible values return no failure so CFG010
/// never reports a value the runtime validator would accept.
/// </summary>
internal static class DataAnnotationsConstraints
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private const NumberStyles IntegerStyles = NumberStyles.Integer;
    private const NumberStyles FloatStyles = NumberStyles.Float;

    public static bool TryGetProvableFailure(
        IPropertySymbol property,
        ITypeSymbol targetType,
        ScalarKind kind,
        string? rawValue,
        out string attributeDisplayName)
    {
        attributeDisplayName = null!;
        if (property is null ||
            targetType is null ||
            rawValue is null ||
            kind is ScalarKind.None or ScalarKind.Null)
        {
            return false;
        }

        var attributes = GetEffectiveConstraintAttributes(property);
        if (attributes.Count == 0)
        {
            return false;
        }

        if (!TryConvertPropertyValue(targetType, rawValue, out var converted, out var convertedType))
        {
            return false;
        }

        foreach (var attribute in attributes)
        {
            var attributeName = GetFrameworkConstraintName(attribute.AttributeClass);
            if (attributeName is null || OverridesIsValid(attribute.AttributeClass))
            {
                continue;
            }

            var failed = attributeName switch
            {
                ValidationAttributeLimits.RangeAttributeName =>
                    FailsRange(attribute, converted),
                ValidationAttributeLimits.MaxLengthAttributeName =>
                    FailsMaxLength(attribute, converted),
                ValidationAttributeLimits.MinLengthAttributeName =>
                    FailsMinLength(attribute, converted),
                ValidationAttributeLimits.StringLengthAttributeName =>
                    FailsStringLength(attribute, converted),
                ValidationAttributeLimits.LengthAttributeName =>
                    FailsLength(attribute, converted),
                ValidationAttributeLimits.AllowedValuesAttributeName =>
                    FailsAllowedValues(attribute, converted, convertedType),
                ValidationAttributeLimits.DeniedValuesAttributeName =>
                    FailsDeniedValues(attribute, converted, convertedType),
                _ => false,
            };

            if (failed)
            {
                attributeDisplayName = DisplayName(attributeName);
                return true;
            }
        }

        return false;
    }

    private static List<AttributeData> GetEffectiveConstraintAttributes(IPropertySymbol property)
    {
        var declarations = new List<IPropertySymbol>();
        for (var current = property; current is not null; current = current.OverriddenProperty)
        {
            declarations.Add(current);
        }

        var declaredAttributes = new List<AttributeData>();
        for (var index = declarations.Count - 1; index >= 0; index--)
        {
            declaredAttributes.AddRange(declarations[index].GetAttributes());
        }

        if (declaredAttributes.Any(attribute => OptionsTypeMetadata.OverridesAttributeTypeId(attribute.AttributeClass)))
        {
            return new List<AttributeData>();
        }

        var effective = new Dictionary<string, AttributeData>(StringComparer.Ordinal);
        foreach (var attribute in declaredAttributes)
        {
            var name = GetFrameworkConstraintName(attribute.AttributeClass);
            if (name is not null)
            {
                effective[name] = attribute;
            }
        }

        return effective.Values.ToList();
    }

    private static string? GetFrameworkConstraintName(INamedTypeSymbol? attributeClass)
    {
        for (var current = attributeClass; current is not null; current = current.BaseType)
        {
            var name = current.ToDisplayString();
            if (name is
                ValidationAttributeLimits.RangeAttributeName or
                ValidationAttributeLimits.MaxLengthAttributeName or
                ValidationAttributeLimits.MinLengthAttributeName or
                ValidationAttributeLimits.StringLengthAttributeName or
                ValidationAttributeLimits.LengthAttributeName or
                ValidationAttributeLimits.AllowedValuesAttributeName or
                ValidationAttributeLimits.DeniedValuesAttributeName)
            {
                return name;
            }
        }

        return null;
    }

    private static bool FailsRange(AttributeData attribute, object converted)
    {
        if (!ValidationAttributeLimits.TryReadRangeOperands(attribute, out var range) ||
            range.OperandType is null ||
            range.Minimum is null ||
            range.Maximum is null)
        {
            return false;
        }

        if (range.IsTypeStringOverload && !range.ParseLimitsInInvariantCulture)
        {
            return false;
        }

        var operandClr = GetClrType(range.OperandType);
        if (operandClr is null)
        {
            return false;
        }

        try
        {
            var value = (IComparable)Convert.ChangeType(converted, operandClr, Invariant);
            var minimum = (IComparable)Convert.ChangeType(range.Minimum, operandClr, Invariant);
            var maximum = (IComparable)Convert.ChangeType(range.Maximum, operandClr, Invariant);
            var minComparison = value.CompareTo(minimum);
            var maxComparison = value.CompareTo(maximum);
            var belowMin = range.MinimumExclusive ? minComparison <= 0 : minComparison < 0;
            var aboveMax = range.MaximumExclusive ? maxComparison >= 0 : maxComparison > 0;
            return belowMin || aboveMax;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            // RangeAttribute treats a value that cannot convert to the operand type as invalid.
            return true;
        }
    }

    private static bool FailsMaxLength(AttributeData attribute, object converted)
    {
        var max = ValidationAttributeLimits.ReadIntValue(attribute);
        return max is int maximum && converted is string text && text.Length > maximum;
    }

    private static bool FailsMinLength(AttributeData attribute, object converted)
    {
        var min = ValidationAttributeLimits.ReadIntValue(attribute);
        return min is int minimum && converted is string text && text.Length < minimum;
    }

    private static bool FailsStringLength(AttributeData attribute, object converted)
    {
        if (converted is not string text)
        {
            return false;
        }

        var max = ValidationAttributeLimits.ReadStringLengthMaximum(attribute);
        if (max is int maximum && text.Length > maximum)
        {
            return true;
        }

        var min = ValidationAttributeLimits.ReadStringLengthMinimum(attribute);
        return text.Length < min;
    }

    private static bool FailsLength(AttributeData attribute, object converted)
    {
        if (converted is not string text ||
            !ValidationAttributeLimits.TryReadLength(attribute, out var minimum, out var maximum))
        {
            return false;
        }

        return text.Length < minimum || text.Length > maximum;
    }

    private static bool FailsAllowedValues(AttributeData attribute, object converted, ITypeSymbol convertedType)
    {
        foreach (var allowed in GetParamsValues(attribute))
        {
            if (ValuesEqual(converted, convertedType, allowed))
            {
                return false;
            }
        }

        return GetParamsValues(attribute).Count > 0;
    }

    private static bool FailsDeniedValues(AttributeData attribute, object converted, ITypeSymbol convertedType)
    {
        foreach (var denied in GetParamsValues(attribute))
        {
            if (ValuesEqual(converted, convertedType, denied))
            {
                return true;
            }
        }

        return false;
    }

    private static List<TypedConstant> GetParamsValues(AttributeData attribute)
    {
        var values = new List<TypedConstant>();
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (argument.Kind == TypedConstantKind.Array)
            {
                values.AddRange(argument.Values);
            }
            else
            {
                values.Add(argument);
            }
        }

        return values;
    }

    private static bool ValuesEqual(object converted, ITypeSymbol convertedType, TypedConstant allowed)
    {
        return allowed.Type is not null &&
               SymbolEqualityComparer.Default.Equals(UnwrapNullable(allowed.Type), UnwrapNullable(convertedType)) &&
               Equals(converted, allowed.Value);
    }

    private static bool TryConvertPropertyValue(
        ITypeSymbol targetType,
        string rawValue,
        out object converted,
        out ITypeSymbol convertedType)
    {
        converted = null!;
        var isNullable = targetType is INamedTypeSymbol nullable &&
            nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        convertedType = UnwrapNullable(targetType);
        if (isNullable && rawValue.Length == 0)
        {
            return false;
        }

        if (convertedType.TypeKind == TypeKind.Enum && convertedType is INamedTypeSymbol enumType)
        {
            return TryConvertEnum(enumType, rawValue, out converted);
        }

        switch (convertedType.SpecialType)
        {
            case SpecialType.System_String:
                converted = rawValue;
                return true;
            case SpecialType.System_Boolean:
                if (bool.TryParse(rawValue.Trim(), out var boolean))
                {
                    converted = boolean;
                    return true;
                }

                return false;
            case SpecialType.System_SByte:
                return TryParseInteger(
                    rawValue,
                    s => sbyte.TryParse(s, IntegerStyles, Invariant, out var value) ? value : null,
                    s => sbyte.TryParse(s, NumberStyles.HexNumber, Invariant, out var hex) ? hex : null,
                    out converted);
            case SpecialType.System_Byte:
                return TryParseInteger(
                    rawValue,
                    s => byte.TryParse(s, IntegerStyles, Invariant, out var value) ? value : null,
                    s => byte.TryParse(s, NumberStyles.HexNumber, Invariant, out var hex) ? hex : null,
                    out converted);
            case SpecialType.System_Int16:
                return TryParseInteger(
                    rawValue,
                    s => short.TryParse(s, IntegerStyles, Invariant, out var value) ? value : null,
                    s => short.TryParse(s, NumberStyles.HexNumber, Invariant, out var hex) ? hex : null,
                    out converted);
            case SpecialType.System_UInt16:
                return TryParseInteger(
                    rawValue,
                    s => ushort.TryParse(s, IntegerStyles, Invariant, out var value) ? value : null,
                    s => ushort.TryParse(s, NumberStyles.HexNumber, Invariant, out var hex) ? hex : null,
                    out converted);
            case SpecialType.System_Int32:
                return TryParseInteger(
                    rawValue,
                    s => int.TryParse(s, IntegerStyles, Invariant, out var value) ? value : null,
                    s => int.TryParse(s, NumberStyles.HexNumber, Invariant, out var hex) ? hex : null,
                    out converted);
            case SpecialType.System_UInt32:
                return TryParseInteger(
                    rawValue,
                    s => uint.TryParse(s, IntegerStyles, Invariant, out var value) ? value : null,
                    s => uint.TryParse(s, NumberStyles.HexNumber, Invariant, out var hex) ? hex : null,
                    out converted);
            case SpecialType.System_Int64:
                return TryParseInteger(
                    rawValue,
                    s => long.TryParse(s, IntegerStyles, Invariant, out var value) ? value : null,
                    s => long.TryParse(s, NumberStyles.HexNumber, Invariant, out var hex) ? hex : null,
                    out converted);
            case SpecialType.System_UInt64:
                return TryParseInteger(
                    rawValue,
                    s => ulong.TryParse(s, IntegerStyles, Invariant, out var value) ? value : null,
                    s => ulong.TryParse(s, NumberStyles.HexNumber, Invariant, out var hex) ? hex : null,
                    out converted);
            case SpecialType.System_Single:
                if (float.TryParse(rawValue, FloatStyles, Invariant, out var single))
                {
                    converted = single;
                    return true;
                }

                return false;
            case SpecialType.System_Double:
                if (double.TryParse(rawValue, FloatStyles, Invariant, out var dbl))
                {
                    converted = dbl;
                    return true;
                }

                return false;
            case SpecialType.System_Decimal:
                if (decimal.TryParse(rawValue, FloatStyles, Invariant, out var dec))
                {
                    converted = dec;
                    return true;
                }

                return false;
        }

        return false;
    }

    private static bool TryParseInteger(
        string rawValue,
        Func<string, object?> tryDecimal,
        Func<string, object?> tryHex,
        out object converted)
    {
        converted = null!;
        var trimmed = rawValue.Trim();
        var parsed = tryDecimal(trimmed);
        if (parsed is not null)
        {
            converted = parsed;
            return true;
        }

        var hex = StripHexPrefix(trimmed);
        if (hex is null)
        {
            return false;
        }

        parsed = tryHex(hex);
        if (parsed is not null)
        {
            converted = parsed;
            return true;
        }

        return false;
    }

    private static string? StripHexPrefix(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && value.Length > 2)
        {
            return value.Substring(2);
        }

        if (value.StartsWith("&h", StringComparison.OrdinalIgnoreCase) && value.Length > 2)
        {
            return value.Substring(2);
        }

        if (value.StartsWith("#", StringComparison.Ordinal) && value.Length > 1)
        {
            return value.Substring(1);
        }

        return null;
    }

    private static bool TryConvertEnum(INamedTypeSymbol enumType, string rawValue, out object converted)
    {
        converted = null!;
        var members = enumType.GetMembers().OfType<IFieldSymbol>().Where(field => field.IsConst).ToArray();
        foreach (var token in rawValue.Split(','))
        {
            var trimmed = token.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            var member = members.FirstOrDefault(field =>
                string.Equals(field.Name, trimmed, StringComparison.OrdinalIgnoreCase));
            if (member?.ConstantValue is not null)
            {
                converted = member.ConstantValue;
                continue;
            }

            if (!TryParseEnumNumeric(enumType, trimmed, out converted))
            {
                return false;
            }
        }

        return converted is not null;
    }

    private static bool TryParseEnumNumeric(INamedTypeSymbol enumType, string value, out object converted)
    {
        converted = null!;
        var ok = enumType.EnumUnderlyingType?.SpecialType switch
        {
            SpecialType.System_SByte => sbyte.TryParse(value, IntegerStyles, Invariant, out var sbyteValue) && Assign(sbyteValue, out converted),
            SpecialType.System_Byte => byte.TryParse(value, IntegerStyles, Invariant, out var byteValue) && Assign(byteValue, out converted),
            SpecialType.System_Int16 => short.TryParse(value, IntegerStyles, Invariant, out var shortValue) && Assign(shortValue, out converted),
            SpecialType.System_UInt16 => ushort.TryParse(value, IntegerStyles, Invariant, out var ushortValue) && Assign(ushortValue, out converted),
            SpecialType.System_Int32 => int.TryParse(value, IntegerStyles, Invariant, out var intValue) && Assign(intValue, out converted),
            SpecialType.System_UInt32 => uint.TryParse(value, IntegerStyles, Invariant, out var uintValue) && Assign(uintValue, out converted),
            SpecialType.System_Int64 => long.TryParse(value, IntegerStyles, Invariant, out var longValue) && Assign(longValue, out converted),
            SpecialType.System_UInt64 => ulong.TryParse(value, IntegerStyles, Invariant, out var ulongValue) && Assign(ulongValue, out converted),
            _ => false,
        };
        return ok;
    }

    private static bool Assign(object value, out object converted)
    {
        converted = value;
        return true;
    }

    private static Type? GetClrType(ITypeSymbol type)
    {
        type = UnwrapNullable(type);
        return type.SpecialType switch
        {
            SpecialType.System_SByte => typeof(sbyte),
            SpecialType.System_Byte => typeof(byte),
            SpecialType.System_Int16 => typeof(short),
            SpecialType.System_UInt16 => typeof(ushort),
            SpecialType.System_Int32 => typeof(int),
            SpecialType.System_UInt32 => typeof(uint),
            SpecialType.System_Int64 => typeof(long),
            SpecialType.System_UInt64 => typeof(ulong),
            SpecialType.System_Single => typeof(float),
            SpecialType.System_Double => typeof(double),
            SpecialType.System_Decimal => typeof(decimal),
            SpecialType.System_String => typeof(string),
            SpecialType.System_Boolean => typeof(bool),
            SpecialType.System_Char => typeof(char),
            _ => null,
        };
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named &&
            named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            named.TypeArguments.Length == 1)
        {
            return named.TypeArguments[0];
        }

        return type;
    }

    private static bool OverridesIsValid(INamedTypeSymbol? attributeClass)
    {
        if (attributeClass is null)
        {
            return false;
        }

        var frameworkNames = new HashSet<string>(StringComparer.Ordinal)
        {
            ValidationAttributeLimits.RangeAttributeName,
            ValidationAttributeLimits.MaxLengthAttributeName,
            ValidationAttributeLimits.MinLengthAttributeName,
            ValidationAttributeLimits.StringLengthAttributeName,
            ValidationAttributeLimits.LengthAttributeName,
            ValidationAttributeLimits.AllowedValuesAttributeName,
            ValidationAttributeLimits.DeniedValuesAttributeName,
        };

        for (var current = attributeClass; current is not null; current = current.BaseType)
        {
            var name = current.ToDisplayString();
            if (frameworkNames.Contains(name))
            {
                return false;
            }

            if (current.GetMembers("IsValid").OfType<IMethodSymbol>().Any(method => method.IsOverride))
            {
                return true;
            }
        }

        return false;
    }

    private static string DisplayName(string attributeFullName)
    {
        var simple = attributeFullName.Split('.').Last();
        return simple.EndsWith("Attribute", StringComparison.Ordinal)
            ? simple.Substring(0, simple.Length - "Attribute".Length)
            : simple;
    }
}
