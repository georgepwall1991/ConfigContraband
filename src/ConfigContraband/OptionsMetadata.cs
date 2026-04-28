using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ConfigContraband;

internal sealed class OptionsTypeMetadata
{
    private OptionsTypeMetadata(ImmutableArray<BindableProperty> bindableProperties)
    {
        BindableProperties = bindableProperties;
    }

    public ImmutableArray<BindableProperty> BindableProperties { get; }

    public static OptionsTypeMetadata Create(INamedTypeSymbol type)
    {
        var properties = ImmutableArray.CreateBuilder<BindableProperty>();

        foreach (var member in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (!IsBindable(member))
            {
                continue;
            }

            properties.Add(new BindableProperty(
                member,
                GetConfigurationNames(member).ToImmutableArray(),
                HasValidationAttribute(member)));
        }

        return new OptionsTypeMetadata(properties.ToImmutable());
    }

    public bool HasAnyDataAnnotations()
    {
        return BindableProperties.Any(property => property.HasValidationAttribute);
    }

    public bool MatchesConfigurationKey(string key)
    {
        foreach (var property in BindableProperties)
        {
            foreach (var name in property.ConfigurationNames)
            {
                if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public ImmutableArray<string> GetConfigurationNames()
    {
        return BindableProperties
            .SelectMany(property => property.ConfigurationNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    public ImmutableArray<NestedValidationCandidate> GetNestedValidationCandidates()
    {
        var builder = ImmutableArray.CreateBuilder<NestedValidationCandidate>();
        foreach (var property in BindableProperties)
        {
            if (TryGetCollectionElementType(property.Symbol.Type, out var elementType))
            {
                if (ContainsValidationAttributes(elementType, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)) &&
                    !HasAttribute(property.Symbol, "Microsoft.Extensions.Options.ValidateEnumeratedItemsAttribute"))
                {
                    builder.Add(new NestedValidationCandidate(property, "ValidateEnumeratedItems", isCollection: true));
                }

                continue;
            }

            if (IsPotentialNestedObject(property.Symbol.Type) &&
                ContainsValidationAttributes(property.Symbol.Type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)) &&
                !HasAttribute(property.Symbol, "Microsoft.Extensions.Options.ValidateObjectMembersAttribute"))
            {
                builder.Add(new NestedValidationCandidate(property, "ValidateObjectMembers", isCollection: false));
            }
        }

        return builder.ToImmutable();
    }

    private static bool IsBindable(IPropertySymbol property)
    {
        return !property.IsStatic &&
               property.DeclaredAccessibility == Accessibility.Public &&
               property.GetMethod is not null &&
               property.SetMethod is not null &&
               property.SetMethod.DeclaredAccessibility == Accessibility.Public &&
               property.Parameters.Length == 0;
    }

    private static IEnumerable<string> GetConfigurationNames(IPropertySymbol property)
    {
        yield return property.Name;

        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == "Microsoft.Extensions.Configuration.ConfigurationKeyNameAttribute" &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is string alias &&
                !string.IsNullOrWhiteSpace(alias))
            {
                yield return alias;
            }
        }
    }

    private static bool HasValidationAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(attribute => InheritsFrom(attribute.AttributeClass, "System.ComponentModel.DataAnnotations.ValidationAttribute"));
    }

    private static bool ContainsValidationAttributes(ITypeSymbol type, HashSet<ITypeSymbol> visited)
    {
        if (!visited.Add(type))
        {
            return false;
        }

        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        foreach (var property in namedType.GetMembers().OfType<IPropertySymbol>())
        {
            if (!IsBindable(property))
            {
                continue;
            }

            if (HasValidationAttribute(property))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName)
    {
        return symbol.GetAttributes().Any(attribute =>
            string.Equals(attribute.AttributeClass?.ToDisplayString(), metadataName, StringComparison.Ordinal));
    }

    private static bool InheritsFrom(INamedTypeSymbol? type, string metadataName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.ToDisplayString(), metadataName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPotentialNestedObject(ITypeSymbol type)
    {
        return type.TypeKind == TypeKind.Class &&
               type.SpecialType != SpecialType.System_String &&
               !type.ContainingNamespace.ToDisplayString().StartsWith("System", StringComparison.Ordinal);
    }

    private static bool TryGetCollectionElementType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        if (type is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            return true;
        }

        if (type.SpecialType == SpecialType.System_String)
        {
            elementType = null!;
            return false;
        }

        if (type is INamedTypeSymbol namedType)
        {
            foreach (var iface in namedType.AllInterfaces.Concat(new[] { namedType }))
            {
                if (iface.IsGenericType &&
                    iface.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
                {
                    elementType = iface.TypeArguments[0];
                    return true;
                }
            }
        }

        elementType = null!;
        return false;
    }
}

internal sealed class BindableProperty
{
    public BindableProperty(IPropertySymbol symbol, ImmutableArray<string> configurationNames, bool hasValidationAttribute)
    {
        Symbol = symbol;
        ConfigurationNames = configurationNames;
        HasValidationAttribute = hasValidationAttribute;
    }

    public IPropertySymbol Symbol { get; }
    public ImmutableArray<string> ConfigurationNames { get; }
    public bool HasValidationAttribute { get; }
}

internal sealed class NestedValidationCandidate
{
    public NestedValidationCandidate(BindableProperty property, string attributeName, bool isCollection)
    {
        Property = property;
        AttributeName = attributeName;
        IsCollection = isCollection;
    }

    public BindableProperty Property { get; }
    public string AttributeName { get; }
    public bool IsCollection { get; }
}
