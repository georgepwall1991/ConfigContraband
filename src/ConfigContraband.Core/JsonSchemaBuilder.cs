using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ConfigContraband;

/// <summary>
/// Translates the bindable-property graph that ConfigContraband already models for diagnostics
/// (<see cref="OptionsTypeMetadata"/>) into a JSON Schema (draft-07) object node. This is the inverse
/// of the analyzer: instead of validating <c>appsettings.json</c> against the options contract, it
/// emits the contract so editors can offer completion, type checking, and required-key hints.
/// </summary>
internal static class JsonSchemaBuilder
{
    /// <summary>
    /// Builds the JSON Schema node describing how <paramref name="type"/> binds from configuration.
    /// </summary>
    public static JsonNode BuildObjectSchema(SchemaSection section, Compilation compilation)
    {
        return BuildObjectSchema(
            section.Type,
            compilation,
            section.Strict,
            section.BindsNonPublicProperties,
            section.ValidatesDataAnnotations);
    }

    public static JsonNode BuildObjectSchema(
        INamedTypeSymbol type,
        Compilation compilation,
        bool strict = false,
        bool bindsNonPublicProperties = false,
        bool validatesDataAnnotations = false)
    {
        var context = new SchemaBuildContext(compilation, bindsNonPublicProperties);
        return BuildObjectSchema(type, context, strict, validatesDataAnnotations, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));
    }

    private static JsonNode BuildObjectSchema(
        INamedTypeSymbol type,
        SchemaBuildContext context,
        bool strict,
        bool validates,
        HashSet<INamedTypeSymbol> visited)
    {
        var schema = JsonNode.Object();
        schema.Add("type", JsonNode.Str("object"));

        // Stop walking cyclic option graphs (e.g. a property typed as its own declaring class).
        // SymbolEqualityComparer.Default ignores nullable annotations, so NodeOptions and NodeOptions?
        // count as the same type for cycle detection.
        if (!visited.Add(type))
        {
            return schema;
        }

        // A type-level [Description]/[DisplayName] or XML <summary> documents the section as a whole. A
        // property that binds this type can override it with its own doc (see AnnotateProperty).
        var typeDescription = ResolveDescription(type);
        if (typeDescription is not null)
        {
            schema.Add("description", JsonNode.Str(typeDescription));
        }

        var metadata = OptionsTypeMetadata.Create(type, context.BindsNonPublicProperties, context.Compilation);
        var properties = JsonNode.Object();
        var required = JsonNode.Array();
        var hasRequired = false;

        foreach (var property in metadata.BindableProperties)
        {
            var key = property.ConfigurationNames.FirstOrDefault() ?? property.Symbol.Name;

            // A property declared as a base type but initialized with a derived type binds derived-only
            // keys at runtime (the analyzer tracks this via polymorphic-initializer metadata). Keep its
            // object open so strict mode does not reject those valid keys.
            var childStrict = strict && !property.HasPotentialPolymorphicInitializer;

            // Validation only walks into a child object/collection when the property opts in with a
            // recursive validation attribute, mirroring CFG002/CFG005.
            var childValidates = validates && property.IsRecursiveValidationEnabled;
            var valueSchema = BuildValueSchema(property.Symbol.Type, context, childStrict, childValidates, visited);
            if (valueSchema is JsonObject valueObject)
            {
                // `validates` (this object's flag), not `childValidates`: a property's own [Range]/length/
                // pattern is enforced when THIS type's DataAnnotations validation runs, mirroring [Required].
                AnnotateProperty(valueObject, property.Symbol, validates);
            }

            properties.Add(key, valueSchema);

            // [Required] is only enforced when DataAnnotations validation actually runs (CFG002).
            if (validates && property.IsRequired)
            {
                required.Add(JsonNode.Str(key));
                hasRequired = true;
            }
        }

        visited.Remove(type);

        if (properties.HasMembers)
        {
            schema.Add("properties", properties);
        }

        if (hasRequired)
        {
            schema.Add("required", required);
        }

        // Mirror the runtime binder: strict bindings (ErrorOnUnknownConfiguration) reject unknown keys,
        // so the schema forbids extras; loose bindings stay open so flexible configuration is still valid.
        if (strict)
        {
            schema.Add("additionalProperties", JsonNode.Bool(false));
        }

        return schema;
    }

    private static JsonNode BuildValueSchema(
        ITypeSymbol type,
        SchemaBuildContext context,
        bool strict,
        bool validates,
        HashSet<INamedTypeSymbol> visited)
    {
        if (OptionsTypeMetadata.TryGetDictionaryValueType(type, out var valueType))
        {
            // Options validation does not recurse into dictionary values (CFG005), so required-key
            // enforcement does not carry into them.
            return JsonNode.Object()
                .Add("type", JsonNode.Str("object"))
                .Add("additionalProperties", BuildValueSchema(valueType, context, strict, false, visited));
        }

        if (OptionsTypeMetadata.TryGetCollectionElementType(type, out var elementType))
        {
            return JsonNode.Object()
                .Add("type", JsonNode.Str("array"))
                .Add("items", BuildValueSchema(elementType, context, strict, validates, visited));
        }

        // The analyzer recurses into struct-typed nested options (CFG005/CFG006), but the
        // schema deliberately stays permissive for value types (an unknown struct may be
        // TypeConverter-bound from a scalar), so only classes emit a nested object schema.
        if (OptionsTypeMetadata.IsPotentialNestedObject(type) && type is INamedTypeSymbol { TypeKind: TypeKind.Class } namedType)
        {
            return BuildObjectSchema(namedType, context, strict, validates, visited);
        }

        return BuildScalarSchema(type);
    }

    private static JsonNode BuildScalarSchema(ITypeSymbol type)
    {
        var nullable = type is INamedTypeSymbol nullableType &&
                       nullableType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                       nullableType.TypeArguments.Length == 1;
        var underlying = nullable ? ((INamedTypeSymbol)type).TypeArguments[0] : type;

        if (underlying.TypeKind == TypeKind.Enum && underlying is INamedTypeSymbol enumType)
        {
            var values = JsonNode.Array();
            foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
            {
                if (member.HasConstantValue)
                {
                    values.Add(JsonNode.Str(member.Name));
                }
            }

            // A nullable enum accepts an explicit null, which must satisfy the enum constraint too.
            if (nullable)
            {
                values.Add(JsonNode.Null());
            }

            // The binder also accepts enum names case-insensitively, but JSON Schema enum is
            // case-sensitive. Emitting the canonical member names gives the best completion experience;
            // non-canonical casing (e.g. "trace") is the rare case and is accepted as flagged.
            return JsonNode.Object()
                .Add("type", ScalarType("string", nullable))
                .Add("enum", values);
        }

        var schema = JsonNode.Object();
        var jsonType = MapScalarType(underlying);
        if (jsonType is not null)
        {
            schema.Add("type", ScalarType(jsonType, nullable));
        }

        return schema;
    }

    private static JsonNode ScalarType(string jsonType, bool nullable)
    {
        // A Nullable<T> option accepts an explicit JSON null in addition to its underlying type.
        return nullable
            ? JsonNode.Array().Add(JsonNode.Str(jsonType)).Add(JsonNode.Str("null"))
            : JsonNode.Str(jsonType);
    }

    private static string? MapScalarType(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_String:
            case SpecialType.System_Char:
                return "string";
            case SpecialType.System_Boolean:
                return "boolean";
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
                return "integer";
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
                return "number";
        }

        // Common BCL types the configuration binder parses from string values.
        switch (type.ToDisplayString())
        {
            case "System.TimeSpan":
            case "System.DateTime":
            case "System.DateTimeOffset":
            case "System.DateOnly":
            case "System.TimeOnly":
            case "System.Guid":
            case "System.Uri":
            case "System.Version":
                return "string";
        }

        // Unknown scalar shape: stay permissive so the schema never produces a false validation error.
        return null;
    }

    /// <summary>
    /// Decorates a property's value node with a <c>description</c> (always, non-validating) and, when the
    /// registration runs DataAnnotations validation, the JSON Schema validation keywords that mirror the
    /// property's DataAnnotations. Every emitted keyword is 1:1 with a constraint the runtime actually
    /// enforces, so the schema never rejects configuration the binder would accept.
    /// </summary>
    private static void AnnotateProperty(JsonObject schema, IPropertySymbol property, bool validates)
    {
        var description = ResolveDescription(property);
        if (description is not null)
        {
            // Place the description right after "type" for readable, deterministic output. On a nested
            // object node this overrides the type-level description with the property's more specific doc.
            schema.InsertAfter("type", "description", JsonNode.Str(description));
        }

        if (validates)
        {
            ApplyConstraints(schema, property);
        }
    }

    private static void ApplyConstraints(JsonObject schema, IPropertySymbol property)
    {
        var kind = ClassifyScalar(property.Type);
        if (kind == ScalarKind.Other)
        {
            // Constraints only apply to scalar string/number nodes; collections and objects are left
            // unconstrained so the schema never produces a false validation error.
            return;
        }

        string? minimum = null;
        string? maximum = null;
        var minimumExclusive = false;
        var maximumExclusive = false;
        int? maxLength = null;

        foreach (var attribute in property.GetAttributes())
        {
            switch (attribute.AttributeClass?.ToDisplayString())
            {
                case "System.ComponentModel.DataAnnotations.RangeAttribute" when kind == ScalarKind.Number:
                    ReadRange(attribute, ref minimum, ref maximum, ref minimumExclusive, ref maximumExclusive);
                    break;
                case "System.ComponentModel.DataAnnotations.MaxLengthAttribute" when kind == ScalarKind.String:
                    maxLength = StricterUpperBound(maxLength, ReadIntValue(attribute));
                    break;
                case "System.ComponentModel.DataAnnotations.StringLengthAttribute" when kind == ScalarKind.String:
                    maxLength = StricterUpperBound(maxLength, ReadStringLengthMaximum(attribute));
                    break;

                    // Deliberately NOT mapped to JSON Schema:
                    // - [MinLength]/StringLength.MinimumLength -> `minLength`: DataAnnotations counts UTF-16
                    //   code units, but JSON Schema counts Unicode code points, so a value with non-BMP
                    //   characters (e.g. "😀", .NET length 2 / 1 code point) could satisfy [MinLength(2)] yet
                    //   be rejected by `minLength: 2`. `maxLength` is safe because code points <= UTF-16 units.
                    // - [RegularExpression] -> `pattern`: .NET regex and ECMA-262 (the dialect JSON Schema
                    //   `pattern` uses) are different languages. Shorthands like \d match different character
                    //   sets, and several .NET constructs are invalid in ECMA, so a translated pattern could
                    //   reject a value the runtime accepts. Faithful translation is not soundly achievable.
                    // - [EmailAddress]/[Url] -> `format`: these attributes are lenient regex checks while
                    //   `format: email`/`uri` follow strict RFC grammars, so a format-enforcing validator could
                    //   reject a value (e.g. "http://foo bar") the runtime accepts.
                    // Emitting nothing for these keeps the zero-false-positive guarantee.
            }
        }

        // Fixed canonical order keeps output deterministic regardless of attribute declaration order.
        if (minimum is not null)
        {
            // Exclusive bounds mirror RangeAttribute.MinimumIsExclusive/MaximumIsExclusive so the schema
            // rejects the boundary value exactly when ValidateDataAnnotations() does.
            schema.Set(minimumExclusive ? "exclusiveMinimum" : "minimum", JsonNode.Number(minimum));
        }

        if (maximum is not null)
        {
            schema.Set(maximumExclusive ? "exclusiveMaximum" : "maximum", JsonNode.Number(maximum));
        }

        if (maxLength is int maxLengthValue)
        {
            schema.Set("maxLength", JsonNode.Number(maxLengthValue.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private enum ScalarKind
    {
        Other,
        Number,
        String,
    }

    private static ScalarKind ClassifyScalar(ITypeSymbol type)
    {
        var underlying = type;
        if (type is INamedTypeSymbol named &&
            named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            named.TypeArguments.Length == 1)
        {
            underlying = named.TypeArguments[0];
        }

        switch (underlying.SpecialType)
        {
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
                return ScalarKind.Number;
            case SpecialType.System_String:
            case SpecialType.System_Char:
                return ScalarKind.String;
            default:
                return ScalarKind.Other;
        }
    }

    private static void ReadRange(
        AttributeData attribute,
        ref string? minimum,
        ref string? maximum,
        ref bool minimumExclusive,
        ref bool maximumExclusive)
    {
        var arguments = attribute.ConstructorArguments;
        if (arguments.Length == 2)
        {
            // Range(int, int) or Range(double, double).
            minimum = FormatNumericConstant(arguments[0]) ?? minimum;
            maximum = FormatNumericConstant(arguments[1]) ?? maximum;
        }
        else if (arguments.Length == 3)
        {
            // Range(Type, string, string): the operand type plus numeric strings. Bounds are parsed with the
            // invariant culture, NOT the current culture. RangeAttribute defaults to the current culture, but
            // a build-time schema generator cannot know the app's runtime culture, and parsing with the build
            // machine's culture would make the committed schema non-deterministic (breaking `--check` across
            // machines). Invariant parsing is deterministic; a culture-specific bound that does not parse is
            // dropped (the safe under-enforcing direction), matching the recommended ParseLimitsInInvariantCulture.
            var boundType = arguments[0].Value as ITypeSymbol;
            minimum = NormalizeNumericLiteral(arguments[1].Value as string, boundType) ?? minimum;
            maximum = NormalizeNumericLiteral(arguments[2].Value as string, boundType) ?? maximum;
        }

        foreach (var named in attribute.NamedArguments)
        {
            if (string.Equals(named.Key, "MinimumIsExclusive", StringComparison.Ordinal) && named.Value.Value is bool minExclusive)
            {
                minimumExclusive = minExclusive;
            }
            else if (string.Equals(named.Key, "MaximumIsExclusive", StringComparison.Ordinal) && named.Value.Value is bool maxExclusive)
            {
                maximumExclusive = maxExclusive;
            }
        }
    }

    private static string? FormatNumericConstant(TypedConstant argument)
    {
        if (argument.Kind != TypedConstantKind.Primitive)
        {
            return null;
        }

        // double.PositiveInfinity / NaN are compile-time constants, so [Range(0, double.PositiveInfinity)]
        // is legal and would format as "Infinity"/"NaN" - not valid JSON. Skip non-finite bounds, and
        // validate every formatted token so an invalid JSON number is never written verbatim.
        string? formatted = argument.Value switch
        {
            int value => value.ToString(CultureInfo.InvariantCulture),
            long value => value.ToString(CultureInfo.InvariantCulture),
            double value when !double.IsNaN(value) && !double.IsInfinity(value) => value.ToString("R", CultureInfo.InvariantCulture),
            float value when !float.IsNaN(value) && !float.IsInfinity(value) => value.ToString("R", CultureInfo.InvariantCulture),
            decimal value => value.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };

        return formatted is not null && IsJsonNumber(formatted) ? formatted : null;
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
        switch (boundType?.SpecialType)
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
                if (long.TryParse(trimmed, NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var signed))
                {
                    return signed.ToString(CultureInfo.InvariantCulture);
                }

                return ulong.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsigned)
                    ? unsigned.ToString(CultureInfo.InvariantCulture)
                    : null;

            case SpecialType.System_Single:
                return float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var single) &&
                       !float.IsNaN(single) && !float.IsInfinity(single)
                    ? ValidJsonNumberOrNull(single.ToString("R", CultureInfo.InvariantCulture))
                    : null;

            case SpecialType.System_Double:
                return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var dbl) &&
                       !double.IsNaN(dbl) && !double.IsInfinity(dbl)
                    ? ValidJsonNumberOrNull(dbl.ToString("R", CultureInfo.InvariantCulture))
                    : null;

            case SpecialType.System_Decimal:
                // decimal preserves the authored scale (so "100.0" stays "100.0").
                return decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var dec)
                    ? ValidJsonNumberOrNull(dec.ToString(CultureInfo.InvariantCulture))
                    : null;

            default:
                // Unknown operand type: only emit a value already shaped as a valid JSON number.
                return ValidJsonNumberOrNull(trimmed);
        }
    }

    private static string? ValidJsonNumberOrNull(string value) => IsJsonNumber(value) ? value : null;

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
        else if (value[index] >= '1' && value[index] <= '9')
        {
            while (index < length && value[index] >= '0' && value[index] <= '9')
            {
                index++;
            }
        }
        else
        {
            return false;
        }

        // frac = "." 1*digit
        if (index < length && value[index] == '.')
        {
            index++;
            var fractionDigits = 0;
            while (index < length && value[index] >= '0' && value[index] <= '9')
            {
                index++;
                fractionDigits++;
            }

            if (fractionDigits == 0)
            {
                return false;
            }
        }

        // exp = ("e" | "E") ["+" | "-"] 1*digit
        if (index < length && (value[index] == 'e' || value[index] == 'E'))
        {
            index++;
            if (index < length && (value[index] == '+' || value[index] == '-'))
            {
                index++;
            }

            var exponentDigits = 0;
            while (index < length && value[index] >= '0' && value[index] <= '9')
            {
                index++;
                exponentDigits++;
            }

            if (exponentDigits == 0)
            {
                return false;
            }
        }

        return index == length;
    }

    private static int? ReadIntValue(AttributeData attribute)
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

    // StringLength.MinimumLength is intentionally ignored (see the minLength note in ApplyConstraints); only
    // the maximum length, which maps safely to JSON Schema `maxLength`, is read here.
    private static int? ReadStringLengthMaximum(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length >= 1 && attribute.ConstructorArguments[0].Value is int max && max >= 0)
        {
            return max;
        }

        return null;
    }

    // DataAnnotations runs every length validator, so combined maxLength bounds tighten rather than
    // overwrite: the effective maximum is the smallest upper bound across all of them.
    private static int? StricterUpperBound(int? existing, int? value)
    {
        if (value is not int candidate)
        {
            return existing;
        }

        return existing is int current ? Math.Min(current, candidate) : candidate;
    }

    /// <summary>
    /// Resolves human-readable documentation for a property or type: an explicit
    /// <c>[Description]</c> (preferred) or <c>[DisplayName]</c> attribute, otherwise the XML
    /// <c>&lt;summary&gt;</c> doc comment. Returns null when nothing usable is present.
    /// </summary>
    private static string? ResolveDescription(ISymbol symbol)
    {
        var attributeText = GetAttributeStringArgument(symbol, "System.ComponentModel.DescriptionAttribute")
            ?? GetAttributeStringArgument(symbol, "System.ComponentModel.DisplayNameAttribute");
        if (attributeText is not null)
        {
            var normalized = NormalizeWhitespace(attributeText);
            return normalized.Length > 0 ? normalized : null;
        }

        return ExtractSummary(symbol.GetDocumentationCommentXml());
    }

    private static string? GetAttributeStringArgument(ISymbol symbol, string attributeFullName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (string.Equals(attribute.AttributeClass?.ToDisplayString(), attributeFullName, StringComparison.Ordinal) &&
                attribute.ConstructorArguments.Length >= 1 &&
                attribute.ConstructorArguments[0].Value is string text)
            {
                return text;
            }
        }

        return null;
    }

    private static string? ExtractSummary(string? documentationXml)
    {
        if (string.IsNullOrEmpty(documentationXml))
        {
            return null;
        }

        var inner = ExtractElementInnerXml(documentationXml!, "summary");
        if (inner is null)
        {
            return null;
        }

        var text = NormalizeWhitespace(StripXmlTagsAndDecode(inner));
        return text.Length > 0 ? text : null;
    }

    /// <summary>
    /// Returns the inner content of the first <c>&lt;<paramref name="element"/>&gt;</c> element in a
    /// documentation-comment XML fragment, or null when it is absent or self-closing. Deliberately a small
    /// hand-rolled scan rather than an XML parser so Core stays dependency-free and analyzer-API safe.
    /// </summary>
    private static string? ExtractElementInnerXml(string xml, string element)
    {
        var openTag = "<" + element;
        var start = xml.IndexOf(openTag, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var afterName = start + openTag.Length;
        if (afterName < xml.Length)
        {
            var next = xml[afterName];
            if (next != '>' && next != '/' && next != ' ' && next != '\t' && next != '\r' && next != '\n')
            {
                // A different element that merely starts with the same text; bail conservatively.
                return null;
            }
        }

        var openEnd = xml.IndexOf('>', afterName);
        if (openEnd < 0 || xml[openEnd - 1] == '/')
        {
            return null;
        }

        var closeTag = "</" + element + ">";
        var closeStart = xml.IndexOf(closeTag, openEnd + 1, StringComparison.Ordinal);
        if (closeStart < 0)
        {
            return null;
        }

        return xml.Substring(openEnd + 1, closeStart - (openEnd + 1));
    }

    private static string StripXmlTagsAndDecode(string xml)
    {
        var builder = new StringBuilder(xml.Length);
        var depth = 0;
        foreach (var ch in xml)
        {
            if (ch == '<')
            {
                depth++;
            }
            else if (ch == '>')
            {
                if (depth > 0)
                {
                    depth--;
                }
            }
            else if (depth == 0)
            {
                builder.Append(ch);
            }
        }

        return DecodeXmlEntities(builder.ToString());
    }

    private static string DecodeXmlEntities(string value)
    {
        if (value.IndexOf('&') < 0)
        {
            return value;
        }

        // &amp; is decoded last so an encoded entity like "&amp;lt;" does not collapse into "<".
        return value
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&apos;", "'")
            .Replace("&amp;", "&");
    }

    private static string NormalizeWhitespace(string value)
    {
        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts);
    }

    private sealed class SchemaBuildContext
    {
        public SchemaBuildContext(Compilation compilation, bool bindsNonPublicProperties)
        {
            Compilation = compilation;
            BindsNonPublicProperties = bindsNonPublicProperties;
        }

        public Compilation Compilation { get; }

        public bool BindsNonPublicProperties { get; }
    }
}
