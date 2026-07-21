using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ConfigContraband;

internal sealed partial class OptionsTypeMetadata
{
    private readonly bool _hasAnyDataAnnotations;
    private readonly bool _bindsNonPublicProperties;
    private readonly Compilation? _compilation;

    private OptionsTypeMetadata(
        INamedTypeSymbol type,
        ImmutableArray<BindableProperty> bindableProperties,
        bool implementsValidatableObject,
        bool hasAnyDataAnnotations,
        bool bindsNonPublicProperties,
        Compilation? compilation)
    {
        TypeSymbol = type;
        TypeName = type.Name;
        TypeKey = type.ToDisplayString();
        BindableProperties = bindableProperties;
        ImplementsValidatableObject = implementsValidatableObject;
        _hasAnyDataAnnotations = hasAnyDataAnnotations;
        _bindsNonPublicProperties = bindsNonPublicProperties;
        _compilation = compilation;
    }

    private INamedTypeSymbol TypeSymbol { get; }
    public string TypeName { get; }
    public string TypeKey { get; }
    public ImmutableArray<BindableProperty> BindableProperties { get; }
    public bool ImplementsValidatableObject { get; }
    public bool BindsNonPublicProperties => _bindsNonPublicProperties;

    public static OptionsTypeMetadata Create(
        INamedTypeSymbol type,
        bool bindsNonPublicProperties = false,
        Compilation? compilation = null)
    {
        var properties = ImmutableArray.CreateBuilder<BindableProperty>();

        // Type-level validation attributes (including inherited ones) and IValidatableObject run
        // against the whole options instance and can inspect any defaulted property, so they keep
        // every required key reported regardless of satisfying defaults.
        var typeHasUnprovableValidation = HasTypeLevelValidationInChain(type);

        foreach (var member in GetBindableProperties(type, bindsNonPublicProperties, compilation))
        {
            properties.Add(new BindableProperty(
                member.Property,
                GetConfigurationNames(member.Property, member.IsConstructorBound).ToImmutableArray(),
                member.IsConstructorBound,
                member.ConstructorParameterCanUseDefault,
                HasValidationAttribute(member.Property),
                IsRequired(member.Property) &&
                (typeHasUnprovableValidation ||
                 HasNonRequiredValidationAttribute(member.Property) ||
                 !HasRequiredSatisfyingDefault(member, type, compilation) ||
                 RecursiveDefaultStillFailsValidation(member.Property, type, bindsNonPublicProperties, compilation)),
                IsRecursiveValidationEnabled(member.Property),
                HasPotentialPolymorphicInitializer(member.Property, type, compilation),
                GetPotentialPolymorphicDictionaryValueInitializerKeys(member.Property, type, compilation)));
        }

        return new OptionsTypeMetadata(
            type,
            properties.ToImmutable(),
            ImplementsInterface(type, "System.ComponentModel.DataAnnotations.IValidatableObject"),
            ContainsValidationAttributes(type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default), bindsNonPublicProperties, compilation),
            bindsNonPublicProperties,
            compilation);
    }

    public bool HasAnyDataAnnotations()
    {
        return _hasAnyDataAnnotations;
    }

    internal bool IsRequiredForSchema(IReadOnlyList<BindableProperty> overrideGroup)
    {
        var runtimeProperty = overrideGroup[0];
        var requiredAttributes = GetEffectiveRequiredAttributes(overrideGroup);
        if (requiredAttributes.Count == 0 ||
            (runtimeProperty.Symbol.Type.IsValueType && !IsNullableValueType(runtimeProperty.Symbol.Type)))
        {
            return false;
        }

        var candidate = new BindablePropertyCandidate(
            runtimeProperty.Symbol,
            runtimeProperty.IsConstructorBound,
            runtimeProperty.ConstructorParameterCanUseDefault);
        var recursiveValidationEnabled = overrideGroup.Any(property => IsRecursiveValidationEnabled(property.Symbol));

        return HasTypeLevelValidationInChain(TypeSymbol) ||
               overrideGroup.Any(property => HasNonRequiredValidationAttribute(property.Symbol)) ||
               requiredAttributes.Any(attribute =>
                   !HasRequiredSatisfyingDefault(
                       candidate,
                       TypeSymbol,
                       _compilation,
                       RequiredAllowsEmptyStrings(attribute))) ||
               RecursiveDefaultStillFailsValidation(
                   runtimeProperty.Symbol,
                   TypeSymbol,
                   _bindsNonPublicProperties,
                   _compilation,
                   recursiveValidationEnabled);
    }

    private static IReadOnlyList<AttributeData> GetEffectiveRequiredAttributes(IReadOnlyList<BindableProperty> overrideGroup)
    {
        var allAttributesByDeclaration = overrideGroup
            .Select(property => property.Symbol.GetAttributes().ToArray())
            .ToArray();
        var requiredAttributesByDeclaration = allAttributesByDeclaration
            .Select(attributes => attributes
                .Where(attribute => IsRequiredAttribute(attribute.AttributeClass))
                .ToArray())
            .ToArray();

        if (allAttributesByDeclaration
            .SelectMany(static attributes => attributes)
            .Any(attribute => OverridesAttributeTypeId(attribute.AttributeClass)))
        {
            // Attribute.TypeId is virtual, and even a non-validation attribute can replace an
            // inherited RequiredAttribute. Source symbols cannot generally prove which attribute
            // a custom implementation replaces, so omit the schema requirement for this unusual
            // property. That safe-side false negative cannot reject runtime-valid configuration.
            return Array.Empty<AttributeData>();
        }

        // Bindable properties are ordered most-derived first. TypeDescriptor replaces an inherited
        // attribute only when a derived declaration has the same TypeId. When TypeId keeps its
        // framework default, concrete attribute type is that identity: build the set base-to-derived
        // so a redeclaration replaces its own type while distinct RequiredAttribute subclasses remain.
        var attributesByType = new Dictionary<INamedTypeSymbol, AttributeData>(SymbolEqualityComparer.Default);
        for (var index = requiredAttributesByDeclaration.Length - 1; index >= 0; index--)
        {
            foreach (var attribute in requiredAttributesByDeclaration[index])
            {
                if (attribute.AttributeClass is { } attributeClass)
                {
                    attributesByType[attributeClass] = attribute;
                }
            }
        }

        return attributesByType.Values.ToList();
    }

    internal static bool OverridesAttributeTypeId(INamedTypeSymbol? attributeClass)
    {
        for (var current = attributeClass; current is not null; current = current.BaseType)
        {
            if (current.GetMembers("TypeId").OfType<IPropertySymbol>().Any(property => property.IsOverride))
            {
                return true;
            }

            if (string.Equals(current.ToDisplayString(), "System.Attribute", StringComparison.Ordinal))
            {
                break;
            }
        }

        return false;
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

    public bool HasProvableNonNullRecursiveDefault(BindableProperty property)
    {
        // The missing-section recursion may only descend when the member's runtime default is
        // provably a non-null, unmutated instance of the declared type: validation skips null
        // members, and unprovable defaults would make declared-type findings speculative. A
        // non-nullable struct's implicit default(T) is treated as such an instance inside
        // ClassifyRecursiveDefault, while a struct whose initializer/constructor sets members
        // (e.g. new() { X = "ok" }) is classified Unprovable there and does not descend.
        return ClassifyEffectiveRecursiveDefault(TypeSymbol, property.Symbol, _compilation) == RecursiveDefaultKind.Modelled &&
               !TryGetCollectionElementType(property.Symbol.Type, out _);
    }

    public bool TryCreateNestedMetadata(BindableProperty property, out OptionsTypeMetadata metadata)
    {
        if (IsPotentialNestedObject(property.Symbol.Type) &&
            property.Symbol.Type is INamedTypeSymbol namedType)
        {
            metadata = Create(namedType, _bindsNonPublicProperties, _compilation);
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
            metadata = Create(namedType, _bindsNonPublicProperties, _compilation);
            return true;
        }

        metadata = null!;
        return false;
    }

    public bool TryCreateDictionaryValueMetadata(BindableProperty property, out OptionsTypeMetadata metadata)
    {
        if (TryGetSupportedDictionaryValueType(property.Symbol.Type, out var valueType) &&
            IsPotentialNestedObject(valueType) &&
            valueType is INamedTypeSymbol namedType)
        {
            metadata = Create(namedType, _bindsNonPublicProperties, _compilation);
            return true;
        }

        metadata = null!;
        return false;
    }

    public bool TryCreateDictionaryValueCollectionElementMetadata(BindableProperty property, out OptionsTypeMetadata metadata)
    {
        if (TryGetSupportedDictionaryValueType(property.Symbol.Type, out var valueType) &&
            TryGetCollectionElementType(valueType, out var elementType) &&
            IsPotentialNestedObject(elementType) &&
            elementType is INamedTypeSymbol namedType)
        {
            metadata = Create(namedType, _bindsNonPublicProperties, _compilation);
            return true;
        }

        metadata = null!;
        return false;
    }

    private static bool IsRecursiveValidationEnabled(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(attr =>
            attr.AttributeClass?.ToDisplayString() is
                "Microsoft.Extensions.Options.ValidateObjectMembersAttribute" or
                "Microsoft.Extensions.Options.ValidateEnumeratedItemsAttribute");
    }

    public ImmutableArray<string> GetConfigurationNames()
    {
        return BindableProperties
            .SelectMany(property => property.ConfigurationNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    public ImmutableArray<string> GetStrictBindingSuggestionNames()
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var property in BindableProperties)
        {
            if (!property.IsConstructorBound &&
                HasConfigurationAlias(property.Symbol))
            {
                continue;
            }

            builder.Add(property.Symbol.Name);
        }

        return builder.Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray();
    }

    public bool HasClrPropertyNamed(string key)
    {
        return TryGetClrProperty(TypeSymbol, key, out _);
    }

    public bool TryGetClrPropertyNamed(string key, out IPropertySymbol? property)
    {
        return TryGetClrProperty(TypeSymbol, key, out property);
    }

    public bool CanStrictBindObjectShapedClrOnlyProperty(IPropertySymbol property)
    {
        if (property.Parameters.Length != 0 ||
            property.GetMethod is null ||
            HasConfigurationAlias(property) ||
            TryGetDictionaryValueType(property.Type, out _) ||
            TryGetCollectionElementType(property.Type, out _) ||
            property.Type.TypeKind == TypeKind.Interface ||
            property.Type.SpecialType == SpecialType.System_Object)
        {
            return false;
        }

        if (property.DeclaredAccessibility != Accessibility.Public &&
            !_bindsNonPublicProperties)
        {
            return false;
        }

        return (property.Type.IsValueType && !IsNullableValueType(property.Type)) ||
               (CanBindPropertyAfterConstruction(property, _bindsNonPublicProperties) &&
                CanRuntimeCreateObjectShapedValue(property.Type)) ||
               (HasNonNullRuntimeInitializer(property, TypeSymbol, _compilation) &&
                !HasPotentialPolymorphicInitializer(property, TypeSymbol, _compilation));
    }

    public bool IsConfigurationAlias(BindableProperty property, string key)
    {
        return !string.Equals(property.Symbol.Name, key, StringComparison.OrdinalIgnoreCase) &&
               HasConfigurationAlias(property.Symbol, key);
    }

    public static ImmutableArray<string> GetClrPropertyNames(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var property in GetProperties(namedType))
        {
            builder.Add(property.Name);
        }

        AddSpecialClrPropertyNames(type, builder);

        return builder.Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray();
    }

    public static bool TryGetClrProperty(ITypeSymbol type, string key, out IPropertySymbol? property)
    {
        if (type is INamedTypeSymbol namedType)
        {
            foreach (var candidate in GetProperties(namedType))
            {
                if (string.Equals(candidate.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    property = candidate;
                    return true;
                }
            }
        }

        if (IsSpecialClrPropertyName(type, key))
        {
            property = null;
            return true;
        }

        property = null;
        return false;
    }

    private static void AddSpecialClrPropertyNames(
        ITypeSymbol type,
        ImmutableArray<string>.Builder builder)
    {
        if (type.SpecialType == SpecialType.System_String)
        {
            builder.Add("Chars");
        }
    }

    private static bool IsSpecialClrPropertyName(ITypeSymbol type, string key)
    {
        return type.SpecialType == SpecialType.System_String &&
               string.Equals(key, "Chars", StringComparison.OrdinalIgnoreCase);
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
                    ContainsValidationAttributes(elementType, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default), _bindsNonPublicProperties, _compilation) &&
                    !HasAttribute(property.Symbol, "Microsoft.Extensions.Options.ValidateEnumeratedItemsAttribute"))
                {
                    builder.Add(new NestedValidationCandidate(property, "ValidateEnumeratedItems", isCollection: true));
                }

                AddNestedValidationCandidates(elementType, builder, visited, _bindsNonPublicProperties, _compilation);
                continue;
            }

            if (IsPotentialNestedObject(property.Symbol.Type) &&
                ContainsValidationAttributes(property.Symbol.Type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default), _bindsNonPublicProperties, _compilation) &&
                !HasAttribute(property.Symbol, "Microsoft.Extensions.Options.ValidateObjectMembersAttribute"))
            {
                builder.Add(new NestedValidationCandidate(property, "ValidateObjectMembers", isCollection: false));
            }

            AddNestedValidationCandidates(property.Symbol.Type, builder, visited, _bindsNonPublicProperties, _compilation);
        }
    }

    private static void AddNestedValidationCandidates(
        ITypeSymbol type,
        ImmutableArray<NestedValidationCandidate>.Builder builder,
        HashSet<ITypeSymbol> visited,
        bool bindsNonPublicProperties,
        Compilation? compilation)
    {
        if (!IsPotentialNestedObject(type) ||
            type is not INamedTypeSymbol namedType ||
            !visited.Add(namedType))
        {
            return;
        }

        Create(namedType, bindsNonPublicProperties, compilation).AddNestedValidationCandidates(builder, visited);
    }

}

internal sealed class BindableProperty
{
    public BindableProperty(
        IPropertySymbol symbol,
        ImmutableArray<string> configurationNames,
        bool isConstructorBound,
        bool constructorParameterCanUseDefault,
        bool hasValidationAttribute,
        bool isRequired,
        bool isRecursiveValidationEnabled,
        bool hasPotentialPolymorphicInitializer,
        ImmutableHashSet<string> potentialPolymorphicDictionaryValueKeys)
    {
        Symbol = symbol;
        ConfigurationNames = configurationNames;
        IsConstructorBound = isConstructorBound;
        ConstructorParameterCanUseDefault = constructorParameterCanUseDefault;
        HasValidationAttribute = hasValidationAttribute;
        IsRequired = isRequired;
        IsRecursiveValidationEnabled = isRecursiveValidationEnabled;
        HasPotentialPolymorphicInitializer = hasPotentialPolymorphicInitializer;
        PotentialPolymorphicDictionaryValueKeys = potentialPolymorphicDictionaryValueKeys;
    }

    public IPropertySymbol Symbol { get; }
    public ImmutableArray<string> ConfigurationNames { get; }
    public bool IsConstructorBound { get; }
    public bool ConstructorParameterCanUseDefault { get; }
    public bool HasValidationAttribute { get; }
    public bool IsRequired { get; }
    public bool IsRecursiveValidationEnabled { get; }
    public bool HasPotentialPolymorphicInitializer { get; }
    public ImmutableHashSet<string> PotentialPolymorphicDictionaryValueKeys { get; }

    public const string AnyPotentialPolymorphicDictionaryValueKey = "\0*";
    public const string CaseInsensitivePotentialPolymorphicDictionaryValueKeyPrefix = "\0i:";

    public bool HasPotentialPolymorphicDictionaryValueInitializerForKey(string key)
    {
        return PotentialPolymorphicDictionaryValueKeys.Contains(AnyPotentialPolymorphicDictionaryValueKey) ||
               PotentialPolymorphicDictionaryValueKeys.Contains(key) ||
               ContainsCaseInsensitivePotentialPolymorphicDictionaryValueKey(ImmutableArray.Create(key));
    }

    public bool HasPotentialPolymorphicDictionaryValueInitializerForPath(ImmutableArray<string> path)
    {
        if (PotentialPolymorphicDictionaryValueKeys.Contains(AnyPotentialPolymorphicDictionaryValueKey))
        {
            return true;
        }

        if (path.IsDefaultOrEmpty)
        {
            return false;
        }

        var key = string.Join(":", path);
        return PotentialPolymorphicDictionaryValueKeys.Contains(key) ||
               ContainsCaseInsensitivePotentialPolymorphicDictionaryValueKey(path);
    }

    private bool ContainsCaseInsensitivePotentialPolymorphicDictionaryValueKey(ImmutableArray<string> path)
    {
        foreach (var candidate in PotentialPolymorphicDictionaryValueKeys)
        {
            if (!candidate.StartsWith(CaseInsensitivePotentialPolymorphicDictionaryValueKeyPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var candidateBody = candidate.Substring(CaseInsensitivePotentialPolymorphicDictionaryValueKeyPrefix.Length);
            var separatorIndex = candidateBody.IndexOf(':');
            if (separatorIndex < 0)
            {
                continue;
            }

            var caseInsensitiveSegments = candidateBody.Substring(0, separatorIndex);
            var candidateSegments = candidateBody.Substring(separatorIndex + 1).Split(':');
            if (caseInsensitiveSegments.Length != path.Length ||
                candidateSegments.Length != path.Length)
            {
                continue;
            }

            var matches = true;
            for (var i = 0; i < path.Length; i++)
            {
                if (!string.Equals(
                        candidateSegments[i],
                        path[i],
                        caseInsensitiveSegments[i] == '1' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }
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
