using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ConfigContraband;

internal sealed class OptionsTypeMetadata
{
    private readonly bool _hasAnyDataAnnotations;
    private readonly bool _bindsNonPublicProperties;

    private OptionsTypeMetadata(
        INamedTypeSymbol type,
        ImmutableArray<BindableProperty> bindableProperties,
        bool implementsValidatableObject,
        bool hasAnyDataAnnotations,
        bool bindsNonPublicProperties)
    {
        TypeName = type.Name;
        TypeKey = type.ToDisplayString();
        BindableProperties = bindableProperties;
        ImplementsValidatableObject = implementsValidatableObject;
        _hasAnyDataAnnotations = hasAnyDataAnnotations;
        _bindsNonPublicProperties = bindsNonPublicProperties;
    }

    public string TypeName { get; }
    public string TypeKey { get; }
    public ImmutableArray<BindableProperty> BindableProperties { get; }
    public bool ImplementsValidatableObject { get; }

    public static OptionsTypeMetadata Create(INamedTypeSymbol type, bool bindsNonPublicProperties = false)
    {
        var properties = ImmutableArray.CreateBuilder<BindableProperty>();

        foreach (var member in GetBindableProperties(type, bindsNonPublicProperties))
        {
            properties.Add(new BindableProperty(
                member.Property,
                GetConfigurationNames(member.Property, member.IsConstructorBound).ToImmutableArray(),
                member.IsConstructorBound,
                member.ConstructorParameterCanUseDefault,
                HasValidationAttribute(member.Property)));
        }

        return new OptionsTypeMetadata(
            type,
            properties.ToImmutable(),
            ImplementsInterface(type, "System.ComponentModel.DataAnnotations.IValidatableObject"),
            ContainsValidationAttributes(type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default), bindsNonPublicProperties),
            bindsNonPublicProperties);
    }

    public bool HasAnyDataAnnotations()
    {
        return _hasAnyDataAnnotations;
    }

    public bool TryGetConfigurationProperty(string key, out BindableProperty bindableProperty)
    {
        foreach (var property in BindableProperties)
        {
            foreach (var name in property.ConfigurationNames)
            {
                if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                {
                    bindableProperty = property;
                    return true;
                }
            }
        }

        bindableProperty = null!;
        return false;
    }

    public bool TryGetSettableConstructorBoundAlias(
        string key,
        ConfigurationNode section,
        out BindableProperty bindableProperty)
    {
        foreach (var property in BindableProperties)
        {
            if (!property.IsConstructorBound ||
                !CanBindPropertyAfterConstruction(property.Symbol, _bindsNonPublicProperties) ||
                (!property.ConstructorParameterCanUseDefault && !section.TryGetProperty(property.Symbol.Name, out _)) ||
                !HasConfigurationAlias(property.Symbol, key))
            {
                continue;
            }

            bindableProperty = property;
            return true;
        }

        bindableProperty = null!;
        return false;
    }

    public bool TryCreateNestedMetadata(BindableProperty property, out OptionsTypeMetadata metadata)
    {
        if (IsPotentialNestedObject(property.Symbol.Type) &&
            property.Symbol.Type is INamedTypeSymbol namedType)
        {
            metadata = Create(namedType, _bindsNonPublicProperties);
            return true;
        }

        metadata = null!;
        return false;
    }

    public bool TryCreateCollectionElementMetadata(BindableProperty property, out OptionsTypeMetadata metadata)
    {
        if (TryGetCollectionElementType(property.Symbol.Type, out var elementType) &&
            IsPotentialNestedObject(elementType) &&
            elementType is INamedTypeSymbol namedType)
        {
            metadata = Create(namedType, _bindsNonPublicProperties);
            return true;
        }

        metadata = null!;
        return false;
    }

    public bool TryCreateDictionaryValueMetadata(BindableProperty property, out OptionsTypeMetadata metadata)
    {
        if (TryGetDictionaryValueType(property.Symbol.Type, out var valueType) &&
            IsPotentialNestedObject(valueType) &&
            valueType is INamedTypeSymbol namedType)
        {
            metadata = Create(namedType, _bindsNonPublicProperties);
            return true;
        }

        metadata = null!;
        return false;
    }

    public bool TryCreateDictionaryValueCollectionElementMetadata(BindableProperty property, out OptionsTypeMetadata metadata)
    {
        if (TryGetDictionaryValueType(property.Symbol.Type, out var valueType) &&
            TryGetCollectionElementType(valueType, out var elementType) &&
            IsPotentialNestedObject(elementType) &&
            elementType is INamedTypeSymbol namedType)
        {
            metadata = Create(namedType, _bindsNonPublicProperties);
            return true;
        }

        metadata = null!;
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
        AddNestedValidationCandidates(builder, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
        return builder.ToImmutable();
    }

    private void AddNestedValidationCandidates(
        ImmutableArray<NestedValidationCandidate>.Builder builder,
        HashSet<ITypeSymbol> visited)
    {
        foreach (var property in BindableProperties)
        {
            if (TryGetCollectionElementType(property.Symbol.Type, out var elementType))
            {
                if (IsPotentialNestedObject(elementType) &&
                    ContainsValidationAttributes(elementType, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default), _bindsNonPublicProperties) &&
                    !HasAttribute(property.Symbol, "Microsoft.Extensions.Options.ValidateEnumeratedItemsAttribute"))
                {
                    builder.Add(new NestedValidationCandidate(property, "ValidateEnumeratedItems", isCollection: true));
                }

                AddNestedValidationCandidates(elementType, builder, visited, _bindsNonPublicProperties);
                continue;
            }

            if (IsPotentialNestedObject(property.Symbol.Type) &&
                ContainsValidationAttributes(property.Symbol.Type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default), _bindsNonPublicProperties) &&
                !HasAttribute(property.Symbol, "Microsoft.Extensions.Options.ValidateObjectMembersAttribute"))
            {
                builder.Add(new NestedValidationCandidate(property, "ValidateObjectMembers", isCollection: false));
            }

            AddNestedValidationCandidates(property.Symbol.Type, builder, visited, _bindsNonPublicProperties);
        }
    }

    private static void AddNestedValidationCandidates(
        ITypeSymbol type,
        ImmutableArray<NestedValidationCandidate>.Builder builder,
        HashSet<ITypeSymbol> visited,
        bool bindsNonPublicProperties)
    {
        if (!IsPotentialNestedObject(type) ||
            type is not INamedTypeSymbol namedType ||
            !visited.Add(namedType))
        {
            return;
        }

        Create(namedType, bindsNonPublicProperties).AddNestedValidationCandidates(builder, visited);
    }

    private static bool IsBindable(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        bool bindsNonPublicProperties,
        out bool isConstructorBound,
        out bool constructorParameterCanUseDefault)
    {
        isConstructorBound = false;
        constructorParameterCanUseDefault = false;
        if (property.IsStatic ||
            property.DeclaredAccessibility != Accessibility.Public ||
            property.GetMethod is null ||
            property.Parameters.Length != 0)
        {
            return false;
        }

        var constructorBound = TryGetConstructorBoundProperty(
            property,
            rootType,
            out var constructorParameterCanUseDefaultValue);
        if (property.SetMethod is not null &&
            (property.SetMethod.DeclaredAccessibility == Accessibility.Public ||
             bindsNonPublicProperties))
        {
            isConstructorBound = constructorBound;
            constructorParameterCanUseDefault = constructorParameterCanUseDefaultValue;
            return true;
        }

        if (constructorBound)
        {
            isConstructorBound = true;
            constructorParameterCanUseDefault = constructorParameterCanUseDefaultValue;
            return true;
        }

        if (HasPropertyInitializer(property) &&
            (IsPotentialNestedObject(property.Type) || IsMutableCollectionType(property.Type)))
        {
            return true;
        }

        return false;
    }

    private static IEnumerable<BindablePropertyCandidate> GetBindableProperties(INamedTypeSymbol type, bool bindsNonPublicProperties)
    {
        foreach (var property in GetProperties(type))
        {
            if (IsBindable(
                    property,
                    type,
                    bindsNonPublicProperties,
                    out var isConstructorBound,
                    out var constructorParameterCanUseDefault))
            {
                yield return new BindablePropertyCandidate(
                    property,
                    isConstructorBound,
                    constructorParameterCanUseDefault);
            }
        }
    }

    private static bool TryGetConstructorBoundProperty(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        out bool constructorParameterCanUseDefault)
    {
        constructorParameterCanUseDefault = false;
        if (rootType.InstanceConstructors.Any(static constructor =>
                constructor.DeclaredAccessibility == Accessibility.Public &&
                constructor.Parameters.Length == 0))
        {
            return false;
        }

        var isConstructorBound = false;
        var allMatchingConstructorParametersCanUseDefault = true;
        foreach (var constructor in rootType.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility != Accessibility.Public ||
                constructor.Parameters.Length == 0 ||
                !constructor.Parameters.All(parameter => TryFindMatchingConstructorProperty(rootType, parameter, out _)))
            {
                continue;
            }

            foreach (var parameter in constructor.Parameters)
            {
                if (IsConstructorParameterForProperty(parameter, property))
                {
                    isConstructorBound = true;
                    allMatchingConstructorParametersCanUseDefault &=
                        CanUseDefaultConstructorParameterValue(parameter);
                }
            }
        }

        constructorParameterCanUseDefault = isConstructorBound && allMatchingConstructorParametersCanUseDefault;
        return isConstructorBound;
    }

    private static bool TryFindMatchingConstructorProperty(
        INamedTypeSymbol type,
        IParameterSymbol parameter,
        out IPropertySymbol property)
    {
        foreach (var candidate in GetProperties(type))
        {
            if (IsConstructorParameterForProperty(parameter, candidate))
            {
                property = candidate;
                return true;
            }
        }

        property = null!;
        return false;
    }

    private static bool IsConstructorParameterForProperty(IParameterSymbol parameter, IPropertySymbol property)
    {
        return !property.IsStatic &&
               property.DeclaredAccessibility == Accessibility.Public &&
               property.GetMethod is not null &&
               property.Parameters.Length == 0 &&
               string.Equals(parameter.Name, property.Name, StringComparison.OrdinalIgnoreCase) &&
               SymbolEqualityComparer.Default.Equals(parameter.Type, property.Type);
    }

    private static bool CanUseDefaultConstructorParameterValue(IParameterSymbol parameter)
    {
        return parameter.IsOptional || parameter.HasExplicitDefaultValue;
    }

    private static IEnumerable<string> GetConfigurationNames(IPropertySymbol property, bool isConstructorBound)
    {
        if (isConstructorBound)
        {
            yield return property.Name;
            yield break;
        }

        var hasAlias = false;
        foreach (var attribute in property.GetAttributes())
        {
            if (TryGetConfigurationAlias(attribute, out var alias))
            {
                hasAlias = true;
                yield return alias;
            }
        }

        if (!hasAlias)
        {
            yield return property.Name;
        }
    }

    private static bool HasConfigurationAlias(IPropertySymbol property, string key)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (TryGetConfigurationAlias(attribute, out var alias) &&
                string.Equals(alias, key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetConfigurationAlias(AttributeData attribute, out string alias)
    {
        if (attribute.AttributeClass?.ToDisplayString() == "Microsoft.Extensions.Configuration.ConfigurationKeyNameAttribute" &&
            attribute.ConstructorArguments.Length == 1 &&
            attribute.ConstructorArguments[0].Value is string value &&
            !string.IsNullOrWhiteSpace(value))
        {
            alias = value;
            return true;
        }

        alias = null!;
        return false;
    }

    private static bool CanBindPropertyAfterConstruction(IPropertySymbol property, bool bindsNonPublicProperties)
    {
        return property.SetMethod is not null &&
               (property.SetMethod.DeclaredAccessibility == Accessibility.Public ||
                bindsNonPublicProperties);
    }

    private static bool HasValidationAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(attribute => InheritsFrom(attribute.AttributeClass, "System.ComponentModel.DataAnnotations.ValidationAttribute"));
    }

    private static bool ContainsValidationAttributes(
        ITypeSymbol type,
        HashSet<ITypeSymbol> visited,
        bool bindsNonPublicProperties)
    {
        if (!visited.Add(type))
        {
            return false;
        }

        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        if (ImplementsInterface(namedType, "System.ComponentModel.DataAnnotations.IValidatableObject"))
        {
            return true;
        }

        foreach (var candidate in GetBindableProperties(namedType, bindsNonPublicProperties))
        {
            var property = candidate.Property;
            if (HasValidationAttribute(property))
            {
                return true;
            }

            if (TryGetCollectionElementType(property.Type, out var elementType))
            {
                if (IsPotentialNestedObject(elementType) &&
                    ContainsValidationAttributes(elementType, visited, bindsNonPublicProperties))
                {
                    return true;
                }

                continue;
            }

            if (IsPotentialNestedObject(property.Type) &&
                ContainsValidationAttributes(property.Type, visited, bindsNonPublicProperties))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<IPropertySymbol> GetProperties(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                yield return property;
            }
        }
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

    private static bool ImplementsInterface(INamedTypeSymbol type, string metadataName)
    {
        return type.AllInterfaces.Any(iface =>
            string.Equals(iface.ToDisplayString(), metadataName, StringComparison.Ordinal));
    }

    private static bool IsPotentialNestedObject(ITypeSymbol type)
    {
        return type.TypeKind == TypeKind.Class &&
               type.SpecialType != SpecialType.System_String &&
               !IsSystemNamespace(type.ContainingNamespace);
    }

    private static bool HasPropertyInitializer(IPropertySymbol property)
    {
        return property.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax>()
            .Any(declaration => declaration.Initializer is not null);
    }

    private static bool IsMutableCollectionType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        foreach (var iface in namedType.AllInterfaces.Concat(new[] { namedType }))
        {
            if (!iface.IsGenericType)
            {
                continue;
            }

            var originalDefinition = iface.OriginalDefinition.ToDisplayString();
            if (originalDefinition == "System.Collections.Generic.ICollection<T>" ||
                originalDefinition == "System.Collections.Generic.IDictionary<TKey, TValue>")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSystemNamespace(INamespaceSymbol containingNamespace)
    {
        var namespaceName = containingNamespace.ToDisplayString();
        return string.Equals(namespaceName, "System", StringComparison.Ordinal) ||
               namespaceName.StartsWith("System.", StringComparison.Ordinal);
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

    private static bool TryGetDictionaryValueType(ITypeSymbol type, out ITypeSymbol valueType)
    {
        if (type is INamedTypeSymbol namedType)
        {
            foreach (var iface in namedType.AllInterfaces.Concat(new[] { namedType }))
            {
                if (!iface.IsGenericType)
                {
                    continue;
                }

                var originalDefinition = iface.OriginalDefinition.ToDisplayString();
                if (originalDefinition == "System.Collections.Generic.IDictionary<TKey, TValue>" ||
                    originalDefinition == "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
                {
                    valueType = iface.TypeArguments[1];
                    return true;
                }
            }
        }

        valueType = null!;
        return false;
    }

    private readonly struct BindablePropertyCandidate
    {
        public BindablePropertyCandidate(
            IPropertySymbol property,
            bool isConstructorBound,
            bool constructorParameterCanUseDefault)
        {
            Property = property;
            IsConstructorBound = isConstructorBound;
            ConstructorParameterCanUseDefault = constructorParameterCanUseDefault;
        }

        public IPropertySymbol Property { get; }
        public bool IsConstructorBound { get; }
        public bool ConstructorParameterCanUseDefault { get; }
    }
}

internal sealed class BindableProperty
{
    public BindableProperty(
        IPropertySymbol symbol,
        ImmutableArray<string> configurationNames,
        bool isConstructorBound,
        bool constructorParameterCanUseDefault,
        bool hasValidationAttribute)
    {
        Symbol = symbol;
        ConfigurationNames = configurationNames;
        IsConstructorBound = isConstructorBound;
        ConstructorParameterCanUseDefault = constructorParameterCanUseDefault;
        HasValidationAttribute = hasValidationAttribute;
    }

    public IPropertySymbol Symbol { get; }
    public ImmutableArray<string> ConfigurationNames { get; }
    public bool IsConstructorBound { get; }
    public bool ConstructorParameterCanUseDefault { get; }
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
