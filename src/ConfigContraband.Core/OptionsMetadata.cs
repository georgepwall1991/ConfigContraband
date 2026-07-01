using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConfigContraband;

internal sealed class OptionsTypeMetadata
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
        // members, and unprovable defaults would make declared-type findings speculative.
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
        if (TryGetDictionaryValueType(property.Symbol.Type, out var valueType) &&
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
        if (TryGetDictionaryValueType(property.Symbol.Type, out var valueType) &&
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

    private static bool IsBindable(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        bool bindsNonPublicProperties,
        Compilation? compilation,
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

        if ((HasNonNullPropertyInitializer(property) ||
             HasNonNullConstructorAssignment(property, rootType, compilation)) &&
            (IsPotentialNestedObject(property.Type) || IsMutableCollectionType(property.Type)))
        {
            return true;
        }

        return false;
    }

    private static IEnumerable<BindablePropertyCandidate> GetBindableProperties(
        INamedTypeSymbol type,
        bool bindsNonPublicProperties,
        Compilation? compilation)
    {
        foreach (var property in GetProperties(type))
        {
            if (IsBindable(
                    property,
                    type,
                    bindsNonPublicProperties,
                    compilation,
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
        var bindableConstructors = rootType.InstanceConstructors
            .Where(static constructor =>
                constructor.DeclaredAccessibility == Accessibility.Public &&
                constructor.Parameters.Length > 0)
            .ToArray();
        if (bindableConstructors.Length != 1)
        {
            return false;
        }

        foreach (var constructor in bindableConstructors)
        {
            if (!constructor.Parameters.All(parameter => TryFindMatchingConstructorProperty(rootType, parameter, out _)))
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

    private static bool IsNullableValueType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { IsGenericType: true } namedType &&
               namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }

    private static bool CanRuntimeCreateObjectShapedValue(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false } namedType &&
               namedType.InstanceConstructors.Any(static constructor =>
                   constructor.DeclaredAccessibility == Accessibility.Public &&
                   constructor.Parameters.Length == 0);
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

    private static bool HasConfigurationAlias(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (TryGetConfigurationAlias(attribute, out _))
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

    private static bool IsRequired(ISymbol symbol)
    {
        if (symbol.GetAttributes().Any(attribute =>
                string.Equals(attribute.AttributeClass?.ToDisplayString(), "System.ComponentModel.DataAnnotations.RequiredAttribute", StringComparison.Ordinal)))
        {
            return symbol is not IPropertySymbol property ||
                   !property.Type.IsValueType ||
                   IsNullableValueType(property.Type);
        }

        return false;
    }

    private static bool HasRequiredSatisfyingDefault(
        BindablePropertyCandidate member,
        INamedTypeSymbol rootType,
        Compilation? compilation)
    {
        var property = member.Property;

        // RequiredAttribute reads the getter, and a custom getter (including C# field-backed
        // semi-auto getters) can return something other than the initializer or the
        // constructor-assigned value.
        if (!IsAutoImplementedAccessor(property.GetMethod))
        {
            return false;
        }

        var allowEmptyStrings = RequiredAllowsEmptyStrings(property);

        if (member.IsConstructorBound &&
            HasSatisfyingConstructorParameterDefault(property, rootType, allowEmptyStrings))
        {
            return true;
        }

        // A constructor-bound property can also keep a satisfying initializer when no declared
        // constructor overwrites it, so the initializer proof below applies to both shapes.
        var hasSatisfyingInitializer = property.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<PropertyDeclarationSyntax>()
            .Any(declaration => declaration.Initializer?.Value is { } value &&
                                InitializerDefinitelySatisfiesRequired(value, property.Type, allowEmptyStrings, compilation));

        // Constructors run after property initializers, so a declared constructor that writes the
        // property (or does anything unprovable) can erase the satisfying default before validation.
        return hasSatisfyingInitializer && NoDeclaredConstructorCanOverwriteProperty(rootType, property);
    }

    private static bool RecursiveDefaultStillFailsValidation(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        bool bindsNonPublicProperties,
        Compilation? compilation)
    {
        // Recursive validation walks the default instance, so a nested required member without
        // its own satisfying default keeps the parent key required.
        if (!IsRecursiveValidationEnabled(property))
        {
            return false;
        }

        switch (ClassifyEffectiveRecursiveDefault(rootType, property, compilation))
        {
            case RecursiveDefaultKind.None:
                // Runtime validation skips null members, and empty collection defaults have no
                // items to validate.
                return false;
            case RecursiveDefaultKind.Unprovable:
                return true;
        }

        if (TryGetCollectionElementType(property.Type, out _))
        {
            // A modelled collection default is a clean empty creation, so nothing is validated.
            return false;
        }

        return NestedGraphHasUnsatisfiedRequired(
            property.Type,
            bindsNonPublicProperties,
            compilation,
            new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
    }

    private static bool NestedGraphHasUnsatisfiedRequired(
        ITypeSymbol type,
        bool bindsNonPublicProperties,
        Compilation? compilation,
        HashSet<ITypeSymbol> visited)
    {
        if (!visited.Add(type) ||
            type is not INamedTypeSymbol namedType ||
            !IsPotentialNestedObject(namedType))
        {
            return false;
        }

        // Runtime recursive validation evaluates every DataAnnotations rule on the default
        // instance, not just [Required]; other attributes and IValidatableObject cannot be
        // proven statically, so they keep the ancestor required.
        if (HasTypeLevelValidationInChain(namedType))
        {
            return true;
        }

        var bindableCandidates = new Dictionary<IPropertySymbol, BindablePropertyCandidate>(SymbolEqualityComparer.Default);
        foreach (var candidate in GetBindableProperties(namedType, bindsNonPublicProperties, compilation))
        {
            bindableCandidates[candidate.Property] = candidate;
        }

        // Validator.TryValidateObject(validateAllProperties: true) evaluates every public-getter
        // property, including non-bindable get-only or private-set members.
        foreach (var property in GetValidationVisibleProperties(namedType))
        {
            if (HasNonRequiredValidationAttribute(property))
            {
                return true;
            }

            if (!bindableCandidates.TryGetValue(property, out var candidate))
            {
                candidate = new BindablePropertyCandidate(property, isConstructorBound: false, constructorParameterCanUseDefault: false);
            }

            if (IsRequired(property) &&
                !HasRequiredSatisfyingDefault(candidate, namedType, compilation))
            {
                return true;
            }

            if (IsRecursiveValidationEnabled(property))
            {
                // Runtime recursive validation walks the actual default instance even when the
                // child is not itself required: an unprovable default fails the whole proof,
                // while null members and empty collection defaults are skipped by validation.
                var defaultKind = ClassifyEffectiveRecursiveDefault(namedType, property, compilation);
                if (defaultKind == RecursiveDefaultKind.Unprovable)
                {
                    return true;
                }

                if (defaultKind == RecursiveDefaultKind.Modelled &&
                    !TryGetCollectionElementType(property.Type, out _) &&
                    NestedGraphHasUnsatisfiedRequired(property.Type, bindsNonPublicProperties, compilation, visited))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasTypeLevelValidationInChain(INamedTypeSymbol type)
    {
        if (ImplementsInterface(type, "System.ComponentModel.DataAnnotations.IValidatableObject"))
        {
            return true;
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (HasValidationAttribute(current))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<IPropertySymbol> GetValidationVisibleProperties(INamedTypeSymbol type)
    {
        foreach (var property in GetProperties(type))
        {
            if (!property.IsStatic &&
                property.Parameters.Length == 0 &&
                property.DeclaredAccessibility == Accessibility.Public &&
                property.GetMethod is { DeclaredAccessibility: Accessibility.Public })
            {
                yield return property;
            }
        }
    }

    private enum RecursiveDefaultKind
    {
        // The runtime default is null or an empty collection, which validation skips.
        None,
        // The runtime default is a clean, unmutated instance of the declared type.
        Modelled,
        // The runtime default cannot be predicted from the declaration.
        Unprovable
    }

    private static RecursiveDefaultKind ClassifyRecursiveDefault(IPropertySymbol property, Compilation? compilation)
    {
        // Validation reads the getter; a custom getter hides the real default.
        if (!IsAutoImplementedAccessor(property.GetMethod))
        {
            return RecursiveDefaultKind.Unprovable;
        }

        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not PropertyDeclarationSyntax declaration ||
                declaration.Initializer?.Value is not { } initializerValue)
            {
                continue;
            }

            var stripped = StripInitializerWrappers(initializerValue);
            if (IsInitializerDefinitelyNullOrDefault(stripped))
            {
                return RecursiveDefaultKind.None;
            }

            if (stripped is CollectionExpressionSyntax collectionExpression)
            {
                return collectionExpression.Elements.Count == 0
                    ? RecursiveDefaultKind.None
                    : RecursiveDefaultKind.Unprovable;
            }

            return IsCleanDeclaredTypeCreation(stripped, property.Type, compilation)
                ? RecursiveDefaultKind.Modelled
                : RecursiveDefaultKind.Unprovable;
        }

        // No initializer (and the caller has proven no constructor writes the member), so the
        // runtime value stays null and validation skips it.
        return RecursiveDefaultKind.None;
    }

    private static bool IsCleanDeclaredTypeCreation(
        ExpressionSyntax expression,
        ITypeSymbol declaredType,
        Compilation? compilation)
    {
        return expression switch
        {
            // A polymorphic default is validated as the created type, not the declared one.
            ObjectCreationExpressionSyntax creation =>
                (creation.Initializer is null || creation.Initializer.Expressions.Count == 0) &&
                (creation.ArgumentList is null || creation.ArgumentList.Arguments.Count == 0) &&
                IsInitializerDefinitelyDeclaredType(expression, declaredType, compilation),
            ImplicitObjectCreationExpressionSyntax implicitCreation =>
                (implicitCreation.Initializer is null || implicitCreation.Initializer.Expressions.Count == 0) &&
                implicitCreation.ArgumentList.Arguments.Count == 0,
            _ => false
        };
    }

    private static bool HasNonRequiredValidationAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(attribute =>
            InheritsFrom(attribute.AttributeClass, "System.ComponentModel.DataAnnotations.ValidationAttribute") &&
            !string.Equals(attribute.AttributeClass?.ToDisplayString(), "System.ComponentModel.DataAnnotations.RequiredAttribute", StringComparison.Ordinal));
    }

    private static bool NoDeclaredConstructorCanOverwriteProperty(INamedTypeSymbol rootType, IPropertySymbol property)
    {
        return TryResolveRuntimeConstructorEffect(rootType, property, compilation: null, out var assignedKind) &&
               assignedKind is null;
    }

    private static RecursiveDefaultKind ClassifyEffectiveRecursiveDefault(
        INamedTypeSymbol owningType,
        IPropertySymbol property,
        Compilation? compilation)
    {
        if (!TryResolveRuntimeConstructorEffect(owningType, property, compilation, out var assignedKind))
        {
            return RecursiveDefaultKind.Unprovable;
        }

        if (assignedKind is not null)
        {
            // The most-derived definite constructor write is the runtime default — provided the
            // getter actually returns the stored value.
            return IsAutoImplementedAccessor(property.GetMethod)
                ? assignedKind.Value
                : RecursiveDefaultKind.Unprovable;
        }

        return ClassifyRecursiveDefault(property, compilation);
    }

    private static bool TryResolveRuntimeConstructorEffect(
        INamedTypeSymbol rootType,
        IPropertySymbol property,
        Compilation? compilation,
        out RecursiveDefaultKind? assignedKind)
    {
        assignedKind = null;

        // Only the constructor chain the binder actually executes matters: the runtime-selected
        // constructor on the root type, then each implicitly chained accessible parameterless
        // base constructor. Unused public overloads and private factory constructors never run.
        var constructor = SelectRuntimeBindingConstructor(rootType);
        while (true)
        {
            if (constructor is null)
            {
                return false;
            }

            ConstructorDeclarationSyntax? declaration = null;
            var hasNonConstructorDeclaration = false;
            foreach (var reference in constructor.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is ConstructorDeclarationSyntax constructorSyntax)
                {
                    declaration = constructorSyntax;
                }
                else
                {
                    hasNonConstructorDeclaration = true;
                }
            }

            if (declaration is null)
            {
                // Implicit default constructors and class/record primary constructors cannot
                // write an existing property, but a primary constructor with explicit base
                // arguments selects a base overload this walk cannot resolve, and a syntaxless
                // constructor from a referenced assembly cannot be proven harmless.
                if (hasNonConstructorDeclaration)
                {
                    if (PrimaryConstructorHasExplicitBaseArguments(constructor))
                    {
                        return false;
                    }
                }
                else if (!constructor.IsImplicitlyDeclared)
                {
                    return false;
                }
            }
            else
            {
                // Explicit chains with arguments can target overloads this walk cannot resolve;
                // a zero-argument `: base()` resolves to the same constructor the implicit chain
                // selects, and a zero-argument `: this()` is followed below.
                if (declaration.Initializer is { } chainInitializer &&
                    chainInitializer.ArgumentList.Arguments.Count > 0)
                {
                    return false;
                }

                if (!TryClassifyConstructorPropertyWrite(declaration, constructor, property, compilation, out var writeKind))
                {
                    return false;
                }

                if (writeKind is not null)
                {
                    // Base constructors run before this write, so the chain above is irrelevant,
                    // and every more-derived constructor was already proven non-writing.
                    assignedKind = writeKind;
                    return true;
                }

                if (declaration.Initializer?.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ThisConstructorInitializer) == true)
                {
                    constructor = SelectParameterlessConstructor(constructor.ContainingType);
                    continue;
                }
            }

            var baseType = constructor.ContainingType.BaseType;
            if (baseType is null || baseType.SpecialType == SpecialType.System_Object)
            {
                return true;
            }

            constructor = SelectImplicitlyChainedConstructor(baseType);
        }
    }

    private static IMethodSymbol? SelectParameterlessConstructor(INamedTypeSymbol type)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.Parameters.Length == 0)
            {
                return constructor;
            }
        }

        return null;
    }

    private static IMethodSymbol? SelectRuntimeBindingConstructor(INamedTypeSymbol type)
    {
        IMethodSymbol? parameterless = null;
        IMethodSymbol? singleParameterized = null;
        var parameterizedCount = 0;
        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            if (constructor.Parameters.Length == 0)
            {
                parameterless = constructor;
            }
            else
            {
                parameterizedCount++;
                singleParameterized = constructor;
            }
        }

        if (parameterless is not null)
        {
            return parameterless;
        }

        return parameterizedCount == 1 ? singleParameterized : null;
    }

    private static IMethodSymbol? SelectImplicitlyChainedConstructor(INamedTypeSymbol baseType)
    {
        foreach (var constructor in baseType.InstanceConstructors)
        {
            if (constructor.Parameters.Length == 0 &&
                constructor.DeclaredAccessibility != Accessibility.Private)
            {
                return constructor;
            }
        }

        return null;
    }

    private static bool PrimaryConstructorHasExplicitBaseArguments(IMethodSymbol constructor)
    {
        foreach (var reference in constructor.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is TypeDeclarationSyntax typeDeclaration &&
                typeDeclaration.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>().Any() == true)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryClassifyConstructorPropertyWrite(
        ConstructorDeclarationSyntax declaration,
        IMethodSymbol constructor,
        IPropertySymbol property,
        Compilation? compilation,
        out RecursiveDefaultKind? writeKind)
    {
        writeKind = null;

        // Name hiding or shadowing makes the syntax match unreliable.
        if (PropertyNameIsHidden(constructor.ContainingType, property) ||
            ConstructorShadowsPropertyName(declaration, property))
        {
            return false;
        }

        if (declaration.ExpressionBody is { } expressionBody)
        {
            if (!IsSimpleParameterOrLiteralAssignment(expressionBody.Expression, constructor))
            {
                return false;
            }

            if (expressionBody.Expression is AssignmentExpressionSyntax expressionAssignment &&
                IsPropertyAssignmentTarget(expressionAssignment.Left, property))
            {
                writeKind = ClassifyAssignedDefaultValue(expressionAssignment.Right, property.Type, compilation);
            }

            return true;
        }

        if (declaration.Body is null)
        {
            return false;
        }

        foreach (var statement in declaration.Body.Statements)
        {
            if (statement is not ExpressionStatementSyntax expressionStatement ||
                !IsSimpleParameterOrLiteralAssignment(expressionStatement.Expression, constructor))
            {
                return false;
            }

            if (expressionStatement.Expression is AssignmentExpressionSyntax assignment &&
                IsPropertyAssignmentTarget(assignment.Left, property))
            {
                // Statements are sequential and side-effect-free, so the last write wins.
                writeKind = ClassifyAssignedDefaultValue(assignment.Right, property.Type, compilation);
            }
        }

        return true;
    }

    private static RecursiveDefaultKind ClassifyAssignedDefaultValue(
        ExpressionSyntax value,
        ITypeSymbol declaredType,
        Compilation? compilation)
    {
        var stripped = StripInitializerWrappers(value);
        if (IsInitializerDefinitelyNullOrDefault(stripped))
        {
            return RecursiveDefaultKind.None;
        }

        if (stripped is CollectionExpressionSyntax collectionExpression)
        {
            return collectionExpression.Elements.Count == 0
                ? RecursiveDefaultKind.None
                : RecursiveDefaultKind.Unprovable;
        }

        return IsCleanDeclaredTypeCreation(stripped, declaredType, compilation)
            ? RecursiveDefaultKind.Modelled
            : RecursiveDefaultKind.Unprovable;
    }

    private static bool RequiredAllowsEmptyStrings(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), "System.ComponentModel.DataAnnotations.RequiredAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var argument in attribute.NamedArguments)
            {
                if (string.Equals(argument.Key, "AllowEmptyStrings", StringComparison.Ordinal) &&
                    argument.Value.Value is bool allowEmptyStrings)
                {
                    return allowEmptyStrings;
                }
            }
        }

        return false;
    }

    private static bool HasSatisfyingConstructorParameterDefault(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        bool allowEmptyStrings)
    {
        var bindableConstructors = rootType.InstanceConstructors
            .Where(static constructor =>
                constructor.DeclaredAccessibility == Accessibility.Public &&
                constructor.Parameters.Length > 0)
            .ToArray();
        if (bindableConstructors.Length != 1)
        {
            return false;
        }

        foreach (var parameter in bindableConstructors[0].Parameters)
        {
            if (IsConstructorParameterForProperty(parameter, property))
            {
                return parameter.HasExplicitDefaultValue &&
                       DefaultValueSatisfiesRequired(parameter.ExplicitDefaultValue, allowEmptyStrings) &&
                       ConstructorParameterDefinitelyReachesProperty(bindableConstructors[0], parameter, property);
            }
        }

        return false;
    }

    private static bool ConstructorParameterDefinitelyReachesProperty(
        IMethodSymbol constructor,
        IParameterSymbol parameter,
        IPropertySymbol property)
    {
        // Positional record parameters initialize their synthesized property directly.
        foreach (var propertyReference in property.DeclaringSyntaxReferences)
        {
            if (propertyReference.GetSyntax() is ParameterSyntax parameterSyntax &&
                parameter.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() == parameterSyntax))
            {
                return true;
            }
        }

        // Name hiding makes the syntax match unreliable: an assignment to a hiding member never
        // reaches the hidden required property.
        if (PropertyNameIsHidden(constructor.ContainingType, property))
        {
            return false;
        }

        foreach (var reference in constructor.DeclaringSyntaxReferences)
        {
            // Constructor initializers run before the body, so a `: base(...)` or `: this(...)`
            // chain cannot clear a value the body assigns afterwards.
            if (reference.GetSyntax() is not ConstructorDeclarationSyntax declaration ||
                ConstructorShadowsPropertyName(declaration, property))
            {
                continue;
            }

            if (declaration.ExpressionBody?.Expression is { } expressionBody)
            {
                // The same side-effect-free target rule as block bodies applies: a custom setter
                // could mutate the value instead of storing the parameter.
                if (IsSimpleParameterOrLiteralAssignment(expressionBody, constructor) &&
                    IsDirectParameterToPropertyAssignment(expressionBody, parameter, property))
                {
                    return true;
                }

                continue;
            }

            if (declaration.Body is null)
            {
                continue;
            }

            // The proof only holds when the body contains nothing but simple parameter-or-literal
            // assignments: helper calls, compound expressions, or control flow could mutate the
            // property after the parameter assignment runs.
            var assignsParameter = false;
            var bodyIsOnlySimpleAssignments = true;
            foreach (var statement in declaration.Body.Statements)
            {
                if (statement is not ExpressionStatementSyntax expressionStatement ||
                    !IsSimpleParameterOrLiteralAssignment(expressionStatement.Expression, constructor))
                {
                    bodyIsOnlySimpleAssignments = false;
                    break;
                }

                if (IsDirectParameterToPropertyAssignment(expressionStatement.Expression, parameter, property))
                {
                    assignsParameter = true;
                }
            }

            if (!bodyIsOnlySimpleAssignments || !assignsParameter)
            {
                continue;
            }

            // Any other write to the property could overwrite the parameter value, so every
            // property write must be the direct parameter assignment.
            var allPropertyWritesUseParameter = declaration.Body
                .DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Where(assignment => IsPropertyAssignmentTarget(assignment.Left, property))
                .All(assignment => IsDirectParameterToPropertyAssignment(assignment, parameter, property));

            if (allPropertyWritesUseParameter)
            {
                return true;
            }
        }

        return false;
    }

    private static bool PropertyNameIsHidden(INamedTypeSymbol type, IPropertySymbol property)
    {
        var membersWithName = 0;
        for (var current = type; current is not null; current = current.BaseType)
        {
            membersWithName += current.GetMembers(property.Name).Length;
        }

        return membersWithName > 1;
    }

    private static bool ConstructorShadowsPropertyName(ConstructorDeclarationSyntax declaration, IPropertySymbol property)
    {
        // A parameter or local named exactly like the property would capture the assignment,
        // leaving the property untouched.
        if (declaration.ParameterList.Parameters.Any(parameter =>
                string.Equals(parameter.Identifier.ValueText, property.Name, StringComparison.Ordinal)))
        {
            return true;
        }

        return declaration.Body is not null &&
               declaration.Body
                   .DescendantNodes()
                   .OfType<VariableDeclaratorSyntax>()
                   .Any(declarator => string.Equals(declarator.Identifier.ValueText, property.Name, StringComparison.Ordinal));
    }

    private static bool IsSimpleParameterOrLiteralAssignment(ExpressionSyntax expression, IMethodSymbol constructor)
    {
        if (expression is not AssignmentExpressionSyntax assignment ||
            !assignment.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleAssignmentExpression))
        {
            return false;
        }

        var left = StripInitializerWrappers(assignment.Left);
        var targetName = left switch
        {
            IdentifierNameSyntax identifierTarget => identifierTarget.Identifier.ValueText,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } memberAccess =>
                memberAccess.Name.Identifier.ValueText,
            _ => null
        };

        // An unqualified identifier that matches a constructor parameter writes the parameter,
        // not a member, even when a same-named field or property exists.
        if (targetName is null ||
            (left is IdentifierNameSyntax &&
             constructor.Parameters.Any(parameter =>
                 string.Equals(parameter.Name, targetName, StringComparison.Ordinal))))
        {
            return false;
        }

        // A custom setter on the assigned member could mutate other properties, so the target
        // must be a field or an auto-implemented property.
        if (!IsSideEffectFreeAssignmentTarget(constructor.ContainingType, targetName))
        {
            return false;
        }

        var right = StripInitializerWrappers(assignment.Right);
        return right is LiteralExpressionSyntax ||
               IsArgumentFreeCreation(right) ||
               (right is IdentifierNameSyntax identifier &&
                constructor.Parameters.Any(parameter =>
                    string.Equals(parameter.Name, identifier.Identifier.ValueText, StringComparison.Ordinal)));
    }

    private static bool IsArgumentFreeCreation(ExpressionSyntax expression)
    {
        // A creation with no arguments and no initializer cannot reference the enclosing
        // instance, so it cannot mutate other properties.
        return expression switch
        {
            ObjectCreationExpressionSyntax creation =>
                (creation.Initializer is null || creation.Initializer.Expressions.Count == 0) &&
                (creation.ArgumentList is null || creation.ArgumentList.Arguments.Count == 0),
            ImplicitObjectCreationExpressionSyntax implicitCreation =>
                (implicitCreation.Initializer is null || implicitCreation.Initializer.Expressions.Count == 0) &&
                implicitCreation.ArgumentList.Arguments.Count == 0,
            CollectionExpressionSyntax collectionExpression => collectionExpression.Elements.Count == 0,
            _ => false
        };
    }

    private static bool IsSideEffectFreeAssignmentTarget(INamedTypeSymbol type, string memberName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(memberName))
            {
                return member switch
                {
                    IFieldSymbol => true,
                    // A get-only auto-property assigned in a constructor writes the backing field
                    // directly; a settable property needs an auto-implemented setter. Overridable
                    // members can dispatch to a derived accessor with side effects.
                    IPropertySymbol property =>
                        !property.IsVirtual && !property.IsAbstract && !property.IsOverride &&
                        (property.SetMethod is null
                            ? IsAutoImplementedAccessor(property.GetMethod)
                            : IsAutoImplementedAccessor(property.SetMethod)),
                    _ => false
                };
            }
        }

        return false;
    }

    private static bool IsAutoImplementedAccessor(IMethodSymbol? accessorMethod)
    {
        if (accessorMethod is null)
        {
            return false;
        }

        if (accessorMethod.IsImplicitlyDeclared)
        {
            return true;
        }

        foreach (var reference in accessorMethod.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not AccessorDeclarationSyntax accessor ||
                accessor.Body is not null ||
                accessor.ExpressionBody is not null)
            {
                return false;
            }
        }

        return accessorMethod.DeclaringSyntaxReferences.Length > 0;
    }

    private static bool IsDirectParameterToPropertyAssignment(
        ExpressionSyntax expression,
        IParameterSymbol parameter,
        IPropertySymbol property)
    {
        return expression is AssignmentExpressionSyntax assignment &&
               assignment.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleAssignmentExpression) &&
               IsPropertyAssignmentTarget(assignment.Left, property) &&
               StripInitializerWrappers(assignment.Right) is IdentifierNameSyntax identifier &&
               string.Equals(identifier.Identifier.ValueText, parameter.Name, StringComparison.Ordinal);
    }

    private static bool IsPropertyAssignmentTarget(ExpressionSyntax expression, IPropertySymbol property)
    {
        return StripInitializerWrappers(expression) switch
        {
            IdentifierNameSyntax identifier =>
                string.Equals(identifier.Identifier.ValueText, property.Name, StringComparison.Ordinal),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } memberAccess =>
                string.Equals(memberAccess.Name.Identifier.ValueText, property.Name, StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool DefaultValueSatisfiesRequired(object? value, bool allowEmptyStrings)
    {
        if (value is null)
        {
            return false;
        }

        if (value is string text)
        {
            return allowEmptyStrings || text.Trim().Length > 0;
        }

        return true;
    }

    private static bool InitializerDefinitelySatisfiesRequired(
        ExpressionSyntax initializer,
        ITypeSymbol propertyType,
        bool allowEmptyStrings,
        Compilation? compilation)
    {
        initializer = StripInitializerWrappers(initializer);

        // Compile-time constants (literals, const fields, nameof, constant folding) keep their
        // value when the key is missing, so judge them by the constant itself — unless a
        // user-defined conversion decides the stored value instead of the source constant.
        if (compilation is not null)
        {
            var semanticModel = compilation.GetSemanticModel(initializer.SyntaxTree);
            var constantValue = semanticModel.GetConstantValue(initializer);
            if (constantValue.HasValue)
            {
                if (Microsoft.CodeAnalysis.CSharp.CSharpExtensions.ClassifyConversion(semanticModel, initializer, propertyType).IsUserDefined)
                {
                    return false;
                }

                return DefaultValueSatisfiesRequired(constantValue.Value, allowEmptyStrings);
            }
        }

        if (initializer is CastExpressionSyntax cast)
        {
            return InitializerDefinitelySatisfiesRequired(cast.Expression, propertyType, allowEmptyStrings, compilation);
        }

        // Signed numeric defaults like -1 parse as a unary expression over a numeric literal.
        if (initializer is PrefixUnaryExpressionSyntax prefixUnary &&
            (prefixUnary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.UnaryMinusExpression) ||
             prefixUnary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.UnaryPlusExpression)) &&
            StripInitializerWrappers(prefixUnary.Operand) is LiteralExpressionSyntax numericOperand &&
            numericOperand.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NumericLiteralExpression))
        {
            return true;
        }

        if (initializer is LiteralExpressionSyntax literal)
        {
            if (literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NullLiteralExpression) ||
                literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.DefaultLiteralExpression))
            {
                return false;
            }

            if (literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression))
            {
                return DefaultValueSatisfiesRequired(literal.Token.ValueText, allowEmptyStrings);
            }

            // Numeric, boolean, and character literals are non-null, non-string runtime values,
            // which RequiredAttribute always accepts.
            return true;
        }

        if (initializer is not (ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax))
        {
            return false;
        }

        // A constructed string is non-null but can be empty or whitespace, which only
        // AllowEmptyStrings accepts.
        if (propertyType.SpecialType == SpecialType.System_String)
        {
            return allowEmptyStrings;
        }

        // Target-typed new() constructs the property type itself — the underlying value type for
        // nullable value properties per the C# spec — so it always produces a non-null, non-string
        // value here (string properties are excluded above and string has no parameterless constructor).
        if (initializer is ImplicitObjectCreationExpressionSyntax)
        {
            return true;
        }

        // Explicit creations need the semantic constructed type: a type alias can hide Nullable<T>,
        // whose parameterless construction boxes to null, and a constructed string assigned to an
        // object-typed property can still be empty or whitespace.
        if (compilation is null)
        {
            return false;
        }

        var creationSemanticModel = compilation.GetSemanticModel(initializer.SyntaxTree);

        // A user-defined conversion decides the stored value, not the constructed source object.
        if (Microsoft.CodeAnalysis.CSharp.CSharpExtensions.ClassifyConversion(creationSemanticModel, initializer, propertyType).IsUserDefined)
        {
            return false;
        }

        var constructedType = creationSemanticModel.GetTypeInfo(initializer).Type;
        if (constructedType is null)
        {
            return false;
        }

        if (constructedType.SpecialType == SpecialType.System_String)
        {
            return allowEmptyStrings;
        }

        if (IsNullableValueType(constructedType))
        {
            // Only Nullable<T> construction with a value carries HasValue == true.
            return ((ObjectCreationExpressionSyntax)initializer).ArgumentList?.Arguments.Count > 0;
        }

        return true;
    }

    private static bool ContainsValidationAttributes(
        ITypeSymbol type,
        HashSet<ITypeSymbol> visited,
        bool bindsNonPublicProperties,
        Compilation? compilation)
    {
        if (!visited.Add(type))
        {
            return false;
        }

        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        // Reuse the inheritance-aware chain walk CFG002 relies on for the same shape: a
        // type-level ValidationAttribute declared only on a base class is still evaluated by
        // Validator.TryValidateObject (AttributeUsageAttribute.Inherited defaults to true),
        // so checking only the exact type's own attributes would miss it.
        if (HasTypeLevelValidationInChain(namedType))
        {
            return true;
        }

        foreach (var candidate in GetBindableProperties(namedType, bindsNonPublicProperties, compilation))
        {
            var property = candidate.Property;
            if (HasValidationAttribute(property))
            {
                return true;
            }

            if (TryGetCollectionElementType(property.Type, out var elementType))
            {
                if (IsPotentialNestedObject(elementType) &&
                    ContainsValidationAttributes(elementType, visited, bindsNonPublicProperties, compilation))
                {
                    return true;
                }

                continue;
            }

            if (IsPotentialNestedObject(property.Type) &&
                ContainsValidationAttributes(property.Type, visited, bindsNonPublicProperties, compilation))
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

    internal static bool IsPotentialNestedObject(ITypeSymbol type)
    {
        return type.TypeKind == TypeKind.Class &&
               type.SpecialType != SpecialType.System_String &&
               !IsSystemNamespace(type.ContainingNamespace);
    }

    private static bool HasNonNullPropertyInitializer(IPropertySymbol property)
    {
        return property.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<PropertyDeclarationSyntax>()
            .Any(declaration => declaration.Initializer?.Value is { } value &&
                                !IsInitializerDefinitelyNullOrDefault(value));
    }

    private static bool HasPotentialPolymorphicInitializer(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        Compilation? compilation)
    {
        if (property.Type.TypeKind != TypeKind.Class ||
            property.Type.SpecialType == SpecialType.System_String ||
            property.Type is INamedTypeSymbol { IsSealed: true })
        {
            return false;
        }

        foreach (var declaration in property.DeclaringSyntaxReferences
                     .Select(reference => reference.GetSyntax())
                     .OfType<PropertyDeclarationSyntax>())
        {
            if (declaration.Initializer?.Value is null)
            {
                continue;
            }

            if (!IsInitializerDefinitelyDeclaredType(declaration.Initializer.Value, property.Type, compilation))
            {
                return true;
            }
        }

        return HasPotentialPolymorphicConstructorAssignment(property, rootType, compilation);
    }

    private static ImmutableHashSet<string> GetPotentialPolymorphicDictionaryValueInitializerKeys(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        Compilation? compilation)
    {
        var keys = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        if (!TryGetDictionaryValueType(property.Type, out _))
        {
            return keys.ToImmutable();
        }

        foreach (var declaration in property.DeclaringSyntaxReferences
                     .Select(reference => reference.GetSyntax())
                     .OfType<PropertyDeclarationSyntax>())
        {
            if (declaration.Initializer?.Value is not null)
            {
                AddPotentialPolymorphicDictionaryValueInitializerKeys(
                    keys,
                    declaration.Initializer.Value,
                    property.Type,
                    ImmutableArray<string>.Empty,
                    compilation);
            }
        }

        AddPotentialPolymorphicDictionaryValueConstructorAssignmentKeys(
            keys,
            property,
            rootType,
            property.Type,
            compilation);

        return keys.ToImmutable();
    }

    private static bool CanHavePolymorphicRuntimeValue(ITypeSymbol type)
    {
        return type.TypeKind == TypeKind.Class &&
               type.SpecialType != SpecialType.System_String &&
               type is not INamedTypeSymbol { IsSealed: true };
    }

    private static bool CanHavePolymorphicDictionaryValue(ITypeSymbol dictionaryType)
    {
        return TryGetDictionaryValueType(dictionaryType, out var valueType) &&
               !TryGetDictionaryValueType(valueType, out _) &&
               !TryGetCollectionElementType(valueType, out _) &&
               CanHavePolymorphicRuntimeValue(valueType);
    }

    private static bool IsOpaqueDictionaryInitializer(ExpressionSyntax expression, Compilation? compilation)
    {
        expression = StripInitializerWrappers(expression);
        return expression switch
        {
            ObjectCreationExpressionSyntax { Initializer: null, ArgumentList.Arguments.Count: 0 } => false,
            ImplicitObjectCreationExpressionSyntax { Initializer: null, ArgumentList.Arguments.Count: 0 } => false,
            CollectionExpressionSyntax { Elements.Count: 0 } => false,
            ObjectCreationExpressionSyntax { Initializer: null } objectCreation =>
                !IsEmptyDictionaryConstructorCall(objectCreation, compilation),
            ImplicitObjectCreationExpressionSyntax { Initializer: null } implicitObjectCreation =>
                !IsEmptyDictionaryConstructorCall(implicitObjectCreation, compilation),
            _ => true
        };
    }

    private static bool IsEmptyDictionaryConstructorCall(
        ObjectCreationExpressionSyntax objectCreation,
        Compilation? compilation)
    {
        return IsEmptyDictionaryConstructorCall(
            objectCreation,
            objectCreation.ArgumentList?.Arguments.Count ?? 0,
            compilation);
    }

    private static bool IsEmptyDictionaryConstructorCall(
        ImplicitObjectCreationExpressionSyntax objectCreation,
        Compilation? compilation)
    {
        return IsEmptyDictionaryConstructorCall(
            objectCreation,
            objectCreation.ArgumentList?.Arguments.Count ?? 0,
            compilation);
    }

    private static bool IsEmptyDictionaryConstructorCall(
        ExpressionSyntax objectCreation,
        int argumentCount,
        Compilation? compilation)
    {
        if (argumentCount == 0)
        {
            return true;
        }

        if (compilation is null)
        {
            return false;
        }

        var semanticModel = compilation.GetSemanticModel(objectCreation.SyntaxTree);
        return semanticModel.GetSymbolInfo(objectCreation).Symbol is IMethodSymbol constructor &&
               constructor.Parameters.Length == argumentCount &&
               constructor.Parameters.All(static parameter =>
                   parameter.Type.SpecialType == SpecialType.System_Int32 ||
                   IsEqualityComparerType(parameter.Type));
    }

    private static bool IsEqualityComparerType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType &&
               namedType.IsGenericType &&
               string.Equals(
                   namedType.OriginalDefinition.ToDisplayString(),
                   "System.Collections.Generic.IEqualityComparer<T>",
                   StringComparison.Ordinal);
    }

    private static void AddPotentialPolymorphicDictionaryValueConstructorAssignmentKeys(
        ImmutableHashSet<string>.Builder keys,
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        ITypeSymbol dictionaryType,
        Compilation? compilation)
    {
        foreach (var constructor in GetRuntimeConstructorDeclarations(rootType, property, compilation))
        {
            if (constructor.ExpressionBody?.Expression is AssignmentExpressionSyntax expressionBodyAssignment &&
                IsAssignmentToProperty(expressionBodyAssignment, property, compilation))
            {
                AddPotentialPolymorphicDictionaryValueInitializerKeys(
                    keys,
                    expressionBodyAssignment.Right,
                    dictionaryType,
                    ImmutableArray<string>.Empty,
                    compilation);
            }

            if (constructor.ExpressionBody?.Expression is AssignmentExpressionSyntax expressionBodyElementAssignment)
            {
                AddPotentialPolymorphicDictionaryElementAssignmentKey(
                    keys,
                    expressionBodyElementAssignment,
                    property,
                    rootType,
                    dictionaryType,
                    compilation);
            }

            if (constructor.ExpressionBody?.Expression is InvocationExpressionSyntax expressionBodyInvocation)
            {
                AddPotentialPolymorphicDictionaryAddInvocationKey(
                    keys,
                    expressionBodyInvocation,
                    property,
                    rootType,
                    dictionaryType,
                    compilation);
            }

            if (constructor.Body is null)
            {
                continue;
            }

            foreach (var assignment in constructor.Body
                         .DescendantNodes(ShouldDescendIntoConstructorInitializerNode)
                         .OfType<AssignmentExpressionSyntax>())
            {
                if (IsAssignmentToProperty(assignment, property, compilation))
                {
                    AddPotentialPolymorphicDictionaryValueInitializerKeys(
                        keys,
                        assignment.Right,
                        dictionaryType,
                        ImmutableArray<string>.Empty,
                        compilation);
                }

                AddPotentialPolymorphicDictionaryElementAssignmentKey(
                    keys,
                    assignment,
                    property,
                    rootType,
                    dictionaryType,
                    compilation);
            }

            foreach (var invocation in constructor.Body
                         .DescendantNodes(ShouldDescendIntoConstructorInitializerNode)
                         .OfType<InvocationExpressionSyntax>())
            {
                AddPotentialPolymorphicDictionaryAddInvocationKey(
                    keys,
                    invocation,
                    property,
                    rootType,
                    dictionaryType,
                    compilation);
            }
        }
    }

    private static bool CanRuntimeSelectRootConstructor(IMethodSymbol constructor)
    {
        if (constructor.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        if (constructor.Parameters.Length == 0)
        {
            return true;
        }

        var containingType = constructor.ContainingType;
        if (containingType.InstanceConstructors.Any(static candidate =>
                candidate.DeclaredAccessibility == Accessibility.Public &&
                candidate.Parameters.Length == 0))
        {
            return false;
        }

        var publicParameterizedConstructors = containingType.InstanceConstructors
            .Where(static candidate =>
                candidate.DeclaredAccessibility == Accessibility.Public &&
                candidate.Parameters.Length > 0)
            .ToArray();

        return publicParameterizedConstructors.Length == 1 &&
               SymbolEqualityComparer.Default.Equals(publicParameterizedConstructors[0], constructor) &&
               constructor.Parameters.All(parameter => TryFindMatchingConstructorProperty(containingType, parameter, out _));
    }

    private static void AddPotentialPolymorphicDictionaryElementAssignmentKey(
        ImmutableHashSet<string>.Builder keys,
        AssignmentExpressionSyntax assignment,
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        ITypeSymbol dictionaryType,
        Compilation? compilation)
    {
        var keyPath = ImmutableArray<string>.Empty;
        if (!assignment.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleAssignmentExpression) ||
            assignment.Left is not ElementAccessExpressionSyntax elementAccess ||
            !TryGetDictionaryElementAccessPath(elementAccess, property, compilation, out keyPath) ||
            assignment.Right.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NullLiteralExpression))
        {
            return;
        }

        AddPotentialPolymorphicDictionaryValueInitializerKeys(
            keys,
            assignment.Right,
            GetDictionaryValueTypeForPath(dictionaryType, keyPath),
            keyPath,
            compilation,
            DictionaryPathCaseInsensitiveSegments(property, rootType, dictionaryType, keyPath, compilation));
    }

    private static void AddPotentialPolymorphicDictionaryAddInvocationKey(
        ImmutableHashSet<string>.Builder keys,
        InvocationExpressionSyntax invocation,
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        ITypeSymbol dictionaryType,
        Compilation? compilation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name: IdentifierNameSyntax name } memberAccess ||
            !string.Equals(name.Identifier.ValueText, "Add", StringComparison.Ordinal) ||
            invocation.ArgumentList.Arguments.Count < 2 ||
            invocation.ArgumentList.Arguments[1].Expression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NullLiteralExpression) ||
            !TryGetDictionaryTargetPath(memberAccess.Expression, property, compilation, out var targetPath))
        {
            return;
        }

        var entryPath = AddDictionaryPathSegment(targetPath, invocation.ArgumentList.Arguments[0].Expression, compilation);
        AddPotentialPolymorphicDictionaryValueInitializerKeys(
            keys,
            invocation.ArgumentList.Arguments[1].Expression,
            GetDictionaryValueTypeForPath(dictionaryType, entryPath),
            entryPath,
            compilation,
            DictionaryPathCaseInsensitiveSegments(property, rootType, dictionaryType, entryPath, compilation));
    }

    private static void AddPotentialPolymorphicDictionaryValueInitializerKeys(
        ImmutableHashSet<string>.Builder keys,
        ExpressionSyntax expression,
        ITypeSymbol dictionaryType,
        ImmutableArray<string> keyPath,
        Compilation? compilation,
        ImmutableArray<bool> caseInsensitivePath = default)
    {
        if (!TryGetDictionaryValueType(dictionaryType, out var valueType))
        {
            if (CanHavePolymorphicRuntimeValue(dictionaryType) &&
                !IsInitializerDefinitelyDeclaredType(expression, dictionaryType, compilation))
            {
                AddPotentialPolymorphicDictionaryPath(keys, keyPath, caseInsensitivePath);
            }

            return;
        }

        var initializer = expression switch
        {
            ObjectCreationExpressionSyntax objectCreation => objectCreation.Initializer,
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation => implicitObjectCreation.Initializer,
            _ => null
        };

        if (initializer is null)
        {
            if (IsOpaqueDictionaryInitializer(expression, compilation) &&
                CanHavePolymorphicDictionaryValue(dictionaryType))
            {
                AddPotentialPolymorphicDictionaryPath(keys, keyPath, caseInsensitivePath);
            }

            return;
        }

        var dictionaryUsesCaseInsensitiveKeys = UsesCaseInsensitiveStringComparer(expression, compilation);
        foreach (var entry in initializer.Expressions)
        {
            if (!TryGetDictionaryInitializerEntry(entry, out var keyExpression, out var valueExpression))
            {
                continue;
            }

            if (valueExpression is null ||
                valueExpression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NullLiteralExpression))
            {
                continue;
            }

            var entryPath = AddDictionaryPathSegment(keyPath, keyExpression, compilation);
            var entryCaseInsensitivePath = AddDictionaryCaseInsensitivePathSegment(
                caseInsensitivePath,
                dictionaryUsesCaseInsensitiveKeys);
            if (TryGetDictionaryValueType(valueType, out _))
            {
                AddPotentialPolymorphicDictionaryValueInitializerKeys(
                    keys,
                    valueExpression,
                    valueType,
                    entryPath,
                    compilation,
                    entryCaseInsensitivePath);
                continue;
            }

            if (CanHavePolymorphicRuntimeValue(valueType) &&
                !IsInitializerDefinitelyDeclaredType(valueExpression, valueType, compilation))
            {
                AddPotentialPolymorphicDictionaryPath(keys, entryPath, entryCaseInsensitivePath);
            }
        }
    }

    private static ImmutableArray<bool> AddDictionaryCaseInsensitivePathSegment(
        ImmutableArray<bool> path,
        bool segmentUsesCaseInsensitiveKeys)
    {
        var builder = path.IsDefault
            ? ImmutableArray.CreateBuilder<bool>()
            : path.ToBuilder();
        builder.Add(segmentUsesCaseInsensitiveKeys);
        return builder.ToImmutable();
    }

    private static ImmutableArray<bool> DictionaryPathCaseInsensitiveSegments(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        ITypeSymbol dictionaryType,
        ImmutableArray<string> keyPath,
        Compilation? compilation)
    {
        if (keyPath.IsDefaultOrEmpty)
        {
            return ImmutableArray<bool>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<bool>(keyPath.Length);
        for (var i = 0; i < keyPath.Length; i++)
        {
            builder.Add(DictionaryPathUsesCaseInsensitiveStringComparer(
                property,
                rootType,
                dictionaryType,
                GetPathPrefix(keyPath, i),
                compilation));
        }

        return builder.ToImmutable();
    }

    private static bool DictionaryPathUsesCaseInsensitiveStringComparer(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        ITypeSymbol dictionaryType,
        ImmutableArray<string> dictionaryPath,
        Compilation? compilation)
    {
        foreach (var declaration in property.DeclaringSyntaxReferences
                     .Select(reference => reference.GetSyntax())
                     .OfType<PropertyDeclarationSyntax>())
        {
            if (declaration.Initializer?.Value is { } initializer &&
                DictionaryInitializerForPathUsesCaseInsensitiveStringComparer(
                    initializer,
                    dictionaryType,
                    dictionaryPath,
                    compilation))
            {
                return true;
            }
        }

        foreach (var constructor in GetRuntimeConstructorDeclarations(rootType, property, compilation))
        {
            if (constructor.ExpressionBody?.Expression is AssignmentExpressionSyntax expressionBodyAssignment &&
                IsAssignmentToProperty(expressionBodyAssignment, property, compilation) &&
                DictionaryInitializerForPathUsesCaseInsensitiveStringComparer(
                    expressionBodyAssignment.Right,
                    dictionaryType,
                    dictionaryPath,
                    compilation))
            {
                return true;
            }

            if (constructor.Body is null)
            {
                continue;
            }

            foreach (var assignment in GetDefinitelyExecutedConstructorAssignments(constructor))
            {
                if (IsAssignmentToProperty(assignment, property, compilation) &&
                    DictionaryInitializerForPathUsesCaseInsensitiveStringComparer(
                        assignment.Right,
                        dictionaryType,
                        dictionaryPath,
                        compilation))
                {
                    return true;
                }

                if (!dictionaryPath.IsDefaultOrEmpty &&
                    assignment.Left is ElementAccessExpressionSyntax elementAccess &&
                    TryGetDictionaryElementAccessPath(elementAccess, property, compilation, out var assignedPath) &&
                    DictionaryPathsEqualForRuntime(
                        assignedPath,
                        dictionaryPath,
                        property,
                        rootType,
                        dictionaryType,
                        compilation) &&
                    UsesCaseInsensitiveStringComparer(assignment.Right, compilation))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool DictionaryPathsEqualForRuntime(
        ImmutableArray<string> left,
        ImmutableArray<string> right,
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        ITypeSymbol dictionaryType,
        Compilation? compilation)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            var parentPath = GetPathPrefix(right, i);
            var ignoreCase = DictionaryPathUsesCaseInsensitiveStringComparer(
                property,
                rootType,
                dictionaryType,
                parentPath,
                compilation);
            if (!DictionaryKeysEqual(left[i], right[i], ignoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static ImmutableArray<string> GetPathPrefix(ImmutableArray<string> path, int length)
    {
        if (length <= 0)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>(length);
        for (var i = 0; i < length; i++)
        {
            builder.Add(path[i]);
        }

        return builder.ToImmutable();
    }

    private static bool DictionaryInitializerForPathUsesCaseInsensitiveStringComparer(
        ExpressionSyntax expression,
        ITypeSymbol dictionaryType,
        ImmutableArray<string> dictionaryPath,
        Compilation? compilation)
    {
        if (dictionaryPath.IsDefaultOrEmpty)
        {
            return UsesCaseInsensitiveStringComparer(expression, compilation);
        }

        if (!TryGetDictionaryValueType(dictionaryType, out var valueType))
        {
            return false;
        }

        var initializer = expression switch
        {
            ObjectCreationExpressionSyntax objectCreation => objectCreation.Initializer,
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation => implicitObjectCreation.Initializer,
            _ => null
        };

        if (initializer is null)
        {
            return false;
        }

        var dictionaryUsesCaseInsensitiveKeys = UsesCaseInsensitiveStringComparer(expression, compilation);
        foreach (var entry in initializer.Expressions)
        {
            if (!TryGetDictionaryInitializerEntry(entry, out var keyExpression, out var valueExpression) ||
                valueExpression is null ||
                !TryGetConstantString(keyExpression, compilation, out var key) ||
                !DictionaryKeysEqual(key, dictionaryPath[0], dictionaryUsesCaseInsensitiveKeys))
            {
                continue;
            }

            return DictionaryInitializerForPathUsesCaseInsensitiveStringComparer(
                valueExpression,
                valueType,
                RemoveFirstPathSegment(dictionaryPath),
                compilation);
        }

        return false;
    }

    private static bool DictionaryKeysEqual(string left, string right, bool ignoreCase)
    {
        return string.Equals(
            left,
            right,
            ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static ImmutableArray<string> RemoveFirstPathSegment(ImmutableArray<string> path)
    {
        if (path.Length <= 1)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>(path.Length - 1);
        for (var i = 1; i < path.Length; i++)
        {
            builder.Add(path[i]);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<string> RemoveLastPathSegment(ImmutableArray<string> path)
    {
        if (path.Length <= 1)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>(path.Length - 1);
        for (var i = 0; i < path.Length - 1; i++)
        {
            builder.Add(path[i]);
        }

        return builder.ToImmutable();
    }

    private static bool TryGetDictionaryInitializerEntry(
        ExpressionSyntax expression,
        out ExpressionSyntax? keyExpression,
        out ExpressionSyntax? valueExpression)
    {
        keyExpression = null;
        valueExpression = null;

        if (expression is AssignmentExpressionSyntax assignment)
        {
            keyExpression = GetDictionaryInitializerAssignmentKeyExpression(assignment.Left);
            valueExpression = assignment.Right;
            return true;
        }

        if (expression is InitializerExpressionSyntax initializer &&
            initializer.Expressions.Count >= 2)
        {
            keyExpression = initializer.Expressions[0];
            valueExpression = initializer.Expressions[1];
            return true;
        }

        if (expression is ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: >= 2 } objectCreation)
        {
            keyExpression = objectCreation.ArgumentList.Arguments[0].Expression;
            valueExpression = objectCreation.ArgumentList.Arguments[1].Expression;
            return true;
        }

        if (expression is ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: >= 2 } implicitObjectCreation)
        {
            keyExpression = implicitObjectCreation.ArgumentList.Arguments[0].Expression;
            valueExpression = implicitObjectCreation.ArgumentList.Arguments[1].Expression;
            return true;
        }

        return false;
    }

    private static ExpressionSyntax? GetDictionaryInitializerAssignmentKeyExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            ImplicitElementAccessSyntax implicitElementAccess =>
                GetDictionaryElementKeyExpression(implicitElementAccess),
            ElementAccessExpressionSyntax elementAccess =>
                GetDictionaryElementKeyExpression(elementAccess),
            _ => null
        };
    }

    private static ExpressionSyntax? GetDictionaryElementKeyExpression(ElementAccessExpressionSyntax elementAccess)
    {
        return elementAccess.ArgumentList.Arguments.Count > 0
            ? elementAccess.ArgumentList.Arguments[0].Expression
            : null;
    }

    private static ExpressionSyntax? GetDictionaryElementKeyExpression(ImplicitElementAccessSyntax elementAccess)
    {
        return elementAccess.ArgumentList.Arguments.Count > 0
            ? elementAccess.ArgumentList.Arguments[0].Expression
            : null;
    }

    private static ImmutableArray<string> AddDictionaryPathSegment(
        ImmutableArray<string> path,
        ExpressionSyntax? keyExpression,
        Compilation? compilation)
    {
        var builder = path.ToBuilder();
        builder.Add(TryGetConstantString(keyExpression, compilation, out var key)
            ? key
            : BindableProperty.AnyPotentialPolymorphicDictionaryValueKey);
        return builder.ToImmutable();
    }

    private static bool UsesCaseInsensitiveStringComparer(
        ExpressionSyntax expression,
        Compilation? compilation)
    {
        expression = StripInitializerWrappers(expression);
        var arguments = expression switch
        {
            ObjectCreationExpressionSyntax objectCreation => objectCreation.ArgumentList?.Arguments,
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation => implicitObjectCreation.ArgumentList?.Arguments,
            _ => null
        };

        if (arguments is null)
        {
            return false;
        }

        foreach (var argument in arguments.Value)
        {
            if (IsCaseInsensitiveStringComparer(argument.Expression, compilation))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCaseInsensitiveStringComparer(ExpressionSyntax expression, Compilation? compilation)
    {
        return IsCaseInsensitiveStringComparer(
            expression,
            compilation,
            new HashSet<ISymbol>(SymbolEqualityComparer.Default));
    }

    private static bool IsCaseInsensitiveStringComparer(
        ExpressionSyntax expression,
        Compilation? compilation,
        HashSet<ISymbol> visitedSymbols)
    {
        if (compilation is not null)
        {
            var semanticModel = compilation.GetSemanticModel(expression.SyntaxTree);
            var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
            if (symbol is IPropertySymbol property &&
                string.Equals(property.ContainingType.ToDisplayString(), "System.StringComparer", StringComparison.Ordinal) &&
                property.Name.EndsWith("IgnoreCase", StringComparison.Ordinal))
            {
                return true;
            }

            if (symbol is not null &&
                visitedSymbols.Add(symbol) &&
                TryGetCaseInsensitiveStringComparerAliasInitializer(symbol, out var initializer))
            {
                return IsCaseInsensitiveStringComparer(initializer, compilation, visitedSymbols);
            }
        }

        return expression.ToString().Contains("StringComparer.", StringComparison.Ordinal) &&
               expression.ToString().Contains("IgnoreCase", StringComparison.Ordinal);
    }

    private static bool TryGetCaseInsensitiveStringComparerAliasInitializer(
        ISymbol symbol,
        out ExpressionSyntax initializer)
    {
        initializer = null!;
        if (symbol is IFieldSymbol field &&
            IsStringComparerAliasType(field.Type))
        {
            return TryGetVariableInitializer(field.DeclaringSyntaxReferences, out initializer);
        }

        if (symbol is ILocalSymbol local &&
            IsStringComparerAliasType(local.Type))
        {
            return TryGetVariableInitializer(local.DeclaringSyntaxReferences, out initializer);
        }

        if (symbol is IPropertySymbol property &&
            IsStringComparerAliasType(property.Type))
        {
            foreach (var declaration in property.DeclaringSyntaxReferences
                         .Select(reference => reference.GetSyntax())
                         .OfType<PropertyDeclarationSyntax>())
            {
                if (declaration.ExpressionBody?.Expression is { } expressionBody)
                {
                    initializer = expressionBody;
                    return true;
                }

                if (declaration.Initializer?.Value is { } value)
                {
                    initializer = value;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetVariableInitializer(
        ImmutableArray<SyntaxReference> syntaxReferences,
        out ExpressionSyntax initializer)
    {
        initializer = null!;
        foreach (var declaration in syntaxReferences
                     .Select(reference => reference.GetSyntax())
                     .OfType<VariableDeclaratorSyntax>())
        {
            if (declaration.Initializer?.Value is { } value)
            {
                initializer = value;
                return true;
            }
        }

        return false;
    }

    private static bool IsStringComparerAliasType(ITypeSymbol type)
    {
        if (string.Equals(type.ToDisplayString(), "System.StringComparer", StringComparison.Ordinal))
        {
            return true;
        }

        return type is INamedTypeSymbol namedType &&
               namedType.TypeArguments.Length == 1 &&
               namedType.TypeArguments[0].SpecialType == SpecialType.System_String &&
               IsEqualityComparerType(type);
    }

    private static void AddPotentialPolymorphicDictionaryPath(
        ImmutableHashSet<string>.Builder keys,
        ImmutableArray<string> path,
        ImmutableArray<bool> caseInsensitivePath = default)
    {
        if (path.IsDefaultOrEmpty ||
            path.Contains(BindableProperty.AnyPotentialPolymorphicDictionaryValueKey))
        {
            keys.Add(BindableProperty.AnyPotentialPolymorphicDictionaryValueKey);
            return;
        }

        var key = string.Join(":", path);
        keys.Add(key);
        if (!caseInsensitivePath.IsDefaultOrEmpty &&
            caseInsensitivePath.Any(static segment => segment))
        {
            keys.Add(BindableProperty.CaseInsensitivePotentialPolymorphicDictionaryValueKeyPrefix +
                     string.Concat(caseInsensitivePath.Select(static segment => segment ? "1" : "0")) +
                     ":" +
                     key);
        }
    }

    private static bool TryGetDictionaryElementAccessPath(
        ElementAccessExpressionSyntax elementAccess,
        IPropertySymbol property,
        Compilation? compilation,
        out ImmutableArray<string> path)
    {
        if (!TryGetDictionaryTargetPath(elementAccess.Expression, property, compilation, out var targetPath))
        {
            path = ImmutableArray<string>.Empty;
            return false;
        }

        path = AddDictionaryPathSegment(targetPath, GetDictionaryElementKeyExpression(elementAccess), compilation);
        return true;
    }

    private static bool TryGetDictionaryTargetPath(
        ExpressionSyntax expression,
        IPropertySymbol property,
        Compilation? compilation,
        out ImmutableArray<string> path)
    {
        if (IsPropertyAssignmentTarget(expression, property, compilation))
        {
            path = ImmutableArray<string>.Empty;
            return true;
        }

        if (expression is ElementAccessExpressionSyntax elementAccess)
        {
            return TryGetDictionaryElementAccessPath(elementAccess, property, compilation, out path);
        }

        path = ImmutableArray<string>.Empty;
        return false;
    }

    private static ITypeSymbol GetDictionaryValueTypeForPath(
        ITypeSymbol dictionaryType,
        ImmutableArray<string> keyPath)
    {
        var currentType = dictionaryType;
        for (var i = 0; i < keyPath.Length; i++)
        {
            if (!TryGetDictionaryValueType(currentType, out var valueType))
            {
                break;
            }

            currentType = valueType;
        }

        return currentType;
    }

    private static bool TryGetConstantString(
        ExpressionSyntax? expression,
        Compilation? compilation,
        out string key)
    {
        expression = expression is null ? null : StripNullableSuppressions(expression);
        if (expression is null)
        {
            key = null!;
            return false;
        }

        if (compilation is not null)
        {
            var semanticModel = compilation.GetSemanticModel(expression.SyntaxTree);
            var constant = semanticModel.GetConstantValue(expression);
            if (constant.HasValue &&
                constant.Value is string constantKey)
            {
                key = constantKey;
                return true;
            }
        }

        if (expression is LiteralExpressionSyntax literal &&
            literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression) &&
            literal.Token.Value is string literalKey)
        {
            key = literalKey;
            return true;
        }

        key = null!;
        return false;
    }

    private static bool HasNonNullRuntimeInitializer(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        Compilation? compilation)
    {
        return HasNonNullPropertyInitializer(property) ||
               HasNonNullConstructorAssignment(property, rootType, compilation);
    }

    private static bool HasNonNullConstructorAssignment(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        Compilation? compilation)
    {
        foreach (var constructor in GetRuntimeConstructorDeclarations(rootType, property, compilation))
        {
            if (constructor.ExpressionBody?.Expression is AssignmentExpressionSyntax expressionBodyAssignment &&
                IsAssignmentToProperty(expressionBodyAssignment, property, compilation) &&
                !IsInitializerDefinitelyNullOrDefault(expressionBodyAssignment.Right))
            {
                return true;
            }

            if (constructor.Body is null)
            {
                continue;
            }

            foreach (var assignment in GetDefinitelyExecutedConstructorAssignments(constructor))
            {
                if (IsAssignmentToProperty(assignment, property, compilation) &&
                    !IsInitializerDefinitelyNullOrDefault(assignment.Right))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<AssignmentExpressionSyntax> GetDefinitelyExecutedConstructorAssignments(
        ConstructorDeclarationSyntax constructor)
    {
        if (constructor.Body is null)
        {
            yield break;
        }

        var hasPriorConditionalExit = false;
        foreach (var statement in constructor.Body.Statements)
        {
            if (!hasPriorConditionalExit &&
                statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment })
            {
                yield return assignment;
            }

            if (ContainsConstructorExit(statement))
            {
                hasPriorConditionalExit = true;
            }
        }
    }

    private static bool ContainsConstructorExit(SyntaxNode node)
    {
        return node.DescendantNodesAndSelf(ShouldDescendIntoConstructorInitializerNode).Any(static descendant =>
            descendant.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ReturnStatement) ||
            descendant.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GotoStatement) ||
            descendant.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GotoCaseStatement) ||
            descendant.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GotoDefaultStatement) ||
            descendant.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ThrowStatement));
    }

    private static bool HasPotentialPolymorphicConstructorAssignment(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        Compilation? compilation)
    {
        foreach (var constructor in GetRuntimeConstructorDeclarations(rootType, property, compilation))
        {
            if (constructor.ExpressionBody?.Expression is AssignmentExpressionSyntax expressionBodyAssignment &&
                IsPotentialPolymorphicAssignmentToProperty(expressionBodyAssignment, property, compilation))
            {
                return true;
            }

            if (constructor.Body is null)
            {
                continue;
            }

            foreach (var assignment in constructor.Body
                         .DescendantNodes(ShouldDescendIntoConstructorInitializerNode)
                         .OfType<AssignmentExpressionSyntax>())
            {
                if (IsPotentialPolymorphicAssignmentToProperty(assignment, property, compilation))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ShouldDescendIntoConstructorInitializerNode(SyntaxNode node)
    {
        return node is not AnonymousFunctionExpressionSyntax and
               not LocalFunctionStatementSyntax;
    }

    private static IEnumerable<ConstructorDeclarationSyntax> GetRuntimeConstructorDeclarations(
        INamedTypeSymbol rootType,
        IPropertySymbol property,
        Compilation? compilation)
    {
        if (compilation is not null)
        {
            var constructors = ImmutableArray.CreateBuilder<ConstructorDeclarationSyntax>();
            var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var constructor in rootType.InstanceConstructors.Where(CanRuntimeSelectRootConstructor))
            {
                AddReachableConstructorDeclarations(constructor, constructors, visited, compilation);
            }

            foreach (var constructor in constructors)
            {
                yield return constructor;
            }

            yield break;
        }

        for (INamedTypeSymbol? current = rootType; current is not null; current = current.BaseType)
        {
            foreach (var declaration in current.DeclaringSyntaxReferences
                         .Select(reference => reference.GetSyntax())
                         .OfType<TypeDeclarationSyntax>())
            {
                foreach (var constructor in declaration.Members.OfType<ConstructorDeclarationSyntax>())
                {
                    yield return constructor;
                }
            }

            if (SymbolEqualityComparer.Default.Equals(current, property.ContainingType))
            {
                yield break;
            }
        }
    }

    private static void AddReachableConstructorDeclarations(
        IMethodSymbol constructor,
        ImmutableArray<ConstructorDeclarationSyntax>.Builder constructors,
        HashSet<IMethodSymbol> visited,
        Compilation compilation)
    {
        if (!visited.Add(constructor))
        {
            return;
        }

        var declaration = GetConstructorDeclaration(constructor);
        if (declaration is not null)
        {
            constructors.Add(declaration);
        }

        var chainedConstructor = declaration?.Initializer is { } initializer
            ? GetChainedConstructor(initializer, compilation)
            : GetImplicitBaseConstructor(constructor);

        if (chainedConstructor is not null)
        {
            AddReachableConstructorDeclarations(chainedConstructor, constructors, visited, compilation);
        }
    }

    private static ConstructorDeclarationSyntax? GetConstructorDeclaration(IMethodSymbol constructor)
    {
        return constructor.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault();
    }

    private static IMethodSymbol? GetChainedConstructor(
        ConstructorInitializerSyntax initializer,
        Compilation compilation)
    {
        var semanticModel = compilation.GetSemanticModel(initializer.SyntaxTree);
        return semanticModel.GetSymbolInfo(initializer).Symbol as IMethodSymbol;
    }

    private static IMethodSymbol? GetImplicitBaseConstructor(IMethodSymbol constructor)
    {
        if (constructor.ContainingType.BaseType is not { SpecialType: not SpecialType.System_Object } baseType)
        {
            return null;
        }

        return baseType.InstanceConstructors.FirstOrDefault(static candidate => candidate.Parameters.Length == 0);
    }

    private static bool IsPotentialPolymorphicAssignmentToProperty(
        AssignmentExpressionSyntax assignment,
        IPropertySymbol property,
        Compilation? compilation)
    {
        return IsAssignmentToProperty(assignment, property, compilation) &&
               !IsInitializerDefinitelyDeclaredType(assignment.Right, property.Type, compilation);
    }

    private static bool IsAssignmentToProperty(
        AssignmentExpressionSyntax assignment,
        IPropertySymbol property,
        Compilation? compilation)
    {
        return assignment.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleAssignmentExpression) &&
               IsPropertyAssignmentTarget(assignment.Left, property, compilation);
    }

    private static bool IsPropertyAssignmentTarget(
        ExpressionSyntax expression,
        IPropertySymbol property,
        Compilation? compilation)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Expression is not ThisExpressionSyntax &&
                memberAccess.Expression is not BaseExpressionSyntax)
            {
                return false;
            }

            if (compilation is null &&
                memberAccess.Name is IdentifierNameSyntax name)
            {
                return string.Equals(name.Identifier.ValueText, property.Name, StringComparison.Ordinal);
            }
        }

        if (compilation is null)
        {
            return false;
        }

        var semanticModel = compilation.GetSemanticModel(expression.SyntaxTree);
        return SymbolEqualityComparer.Default.Equals(
            semanticModel.GetSymbolInfo(expression).Symbol,
            property);
    }

    private static bool IsInitializerDefinitelyDeclaredType(
        ExpressionSyntax initializer,
        ITypeSymbol declaredType,
        Compilation? compilation)
    {
        initializer = StripInitializerWrappers(initializer);
        if (IsInitializerDefinitelyNullOrDefault(initializer))
        {
            return true;
        }

        return initializer switch
        {
            ImplicitObjectCreationExpressionSyntax => true,
            ObjectCreationExpressionSyntax objectCreation => IsTypeSyntaxDeclaredType(objectCreation.Type, declaredType, compilation),
            _ => false
        };
    }

    private static bool IsInitializerDefinitelyNullOrDefault(ExpressionSyntax initializer)
    {
        initializer = StripInitializerWrappers(initializer);
        if (initializer is CastExpressionSyntax cast)
        {
            return IsInitializerDefinitelyNullOrDefault(cast.Expression);
        }

        return initializer.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NullLiteralExpression) ||
               initializer.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.DefaultLiteralExpression) ||
               initializer is DefaultExpressionSyntax;
    }

    private static ExpressionSyntax StripInitializerWrappers(ExpressionSyntax expression)
    {
        while (true)
        {
            expression = StripNullableSuppressions(expression);
            if (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
                continue;
            }

            return expression;
        }
    }

    private static ExpressionSyntax StripNullableSuppressions(ExpressionSyntax expression)
    {
        while (expression is PostfixUnaryExpressionSyntax postfix &&
               postfix.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SuppressNullableWarningExpression))
        {
            expression = postfix.Operand;
        }

        return expression;
    }

    private static bool IsTypeSyntaxDeclaredType(
        TypeSyntax typeSyntax,
        ITypeSymbol declaredType,
        Compilation? compilation)
    {
        if (compilation is not null)
        {
            var semanticModel = compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            var type = semanticModel.GetTypeInfo(typeSyntax).Type;
            if (type is null && typeSyntax is NameSyntax nameSyntax)
            {
                type = semanticModel.GetAliasInfo(nameSyntax)?.Target as ITypeSymbol;
            }

            if (type is not null)
            {
                return IsSameType(type, declaredType);
            }

            return IsQualifiedTypeSyntaxDeclaredType(typeSyntax, declaredType);
        }

        return typeSyntax switch
        {
            IdentifierNameSyntax identifier => string.Equals(identifier.Identifier.ValueText, declaredType.Name, StringComparison.Ordinal),
            GenericNameSyntax generic => string.Equals(generic.Identifier.ValueText, declaredType.Name, StringComparison.Ordinal),
            QualifiedNameSyntax or AliasQualifiedNameSyntax => IsQualifiedTypeSyntaxDeclaredType(typeSyntax, declaredType),
            NullableTypeSyntax nullable => IsTypeSyntaxDeclaredType(nullable.ElementType, declaredType, compilation),
            _ => false
        };
    }

    private static bool IsQualifiedTypeSyntaxDeclaredType(TypeSyntax typeSyntax, ITypeSymbol declaredType)
    {
        var syntaxName = NormalizeTypeSyntaxName(typeSyntax.ToString());
        var displayName = NormalizeTypeSyntaxName(declaredType.ToDisplayString());
        var fullyQualifiedName = NormalizeTypeSyntaxName(declaredType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

        return string.Equals(syntaxName, displayName, StringComparison.Ordinal) ||
               string.Equals(syntaxName, fullyQualifiedName, StringComparison.Ordinal);
    }

    private static bool IsSameType(ITypeSymbol type, ITypeSymbol declaredType)
    {
        if (SymbolEqualityComparer.Default.Equals(type, declaredType))
        {
            return true;
        }

        return string.Equals(
            NormalizeTypeSyntaxName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
            NormalizeTypeSyntaxName(declaredType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
            StringComparison.Ordinal);
    }

    private static string NormalizeTypeSyntaxName(string name)
    {
        const string globalPrefix = "global::";
        return name.StartsWith(globalPrefix, StringComparison.Ordinal)
            ? name.Substring(globalPrefix.Length)
            : name;
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

    public static bool TryGetCollectionElementType(ITypeSymbol type, out ITypeSymbol elementType)
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

    public static bool TryGetDictionaryValueType(ITypeSymbol type, out ITypeSymbol valueType)
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
