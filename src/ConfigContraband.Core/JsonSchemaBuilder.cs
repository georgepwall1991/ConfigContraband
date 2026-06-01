using System.Collections.Generic;
using System.Linq;
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
    public static JsonNode BuildObjectSchema(
        INamedTypeSymbol type,
        Compilation compilation,
        bool strict = false,
        bool bindsNonPublicProperties = false)
    {
        var context = new SchemaBuildContext(compilation, strict, bindsNonPublicProperties);
        return BuildObjectSchema(type, context, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));
    }

    private static JsonNode BuildObjectSchema(
        INamedTypeSymbol type,
        SchemaBuildContext context,
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

        var metadata = OptionsTypeMetadata.Create(type, context.BindsNonPublicProperties, context.Compilation);
        var properties = JsonNode.Object();
        var required = JsonNode.Array();
        var hasRequired = false;

        foreach (var property in metadata.BindableProperties)
        {
            var key = property.ConfigurationNames.FirstOrDefault() ?? property.Symbol.Name;
            properties.Add(key, BuildValueSchema(property.Symbol.Type, context, visited));

            if (property.IsRequired)
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
        if (context.Strict)
        {
            schema.Add("additionalProperties", JsonNode.Bool(false));
        }

        return schema;
    }

    private static JsonNode BuildValueSchema(
        ITypeSymbol type,
        SchemaBuildContext context,
        HashSet<INamedTypeSymbol> visited)
    {
        if (OptionsTypeMetadata.TryGetDictionaryValueType(type, out var valueType))
        {
            return JsonNode.Object()
                .Add("type", JsonNode.Str("object"))
                .Add("additionalProperties", BuildValueSchema(valueType, context, visited));
        }

        if (OptionsTypeMetadata.TryGetCollectionElementType(type, out var elementType))
        {
            return JsonNode.Object()
                .Add("type", JsonNode.Str("array"))
                .Add("items", BuildValueSchema(elementType, context, visited));
        }

        if (OptionsTypeMetadata.IsPotentialNestedObject(type) && type is INamedTypeSymbol namedType)
        {
            return BuildObjectSchema(namedType, context, visited);
        }

        return BuildScalarSchema(type);
    }

    private static JsonNode BuildScalarSchema(ITypeSymbol type)
    {
        var underlying = UnwrapNullable(type);

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

            return JsonNode.Object()
                .Add("type", JsonNode.Str("string"))
                .Add("enum", values);
        }

        var schema = JsonNode.Object();
        var jsonType = MapScalarType(underlying);
        if (jsonType is not null)
        {
            schema.Add("type", JsonNode.Str(jsonType));
        }

        return schema;
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

    private sealed class SchemaBuildContext
    {
        public SchemaBuildContext(Compilation compilation, bool strict, bool bindsNonPublicProperties)
        {
            Compilation = compilation;
            Strict = strict;
            BindsNonPublicProperties = bindsNonPublicProperties;
        }

        public Compilation Compilation { get; }

        public bool Strict { get; }

        public bool BindsNonPublicProperties { get; }
    }
}
