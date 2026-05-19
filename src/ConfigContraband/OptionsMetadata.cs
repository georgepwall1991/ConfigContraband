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

        foreach (var member in GetBindableProperties(type, bindsNonPublicProperties, compilation))
        {
            properties.Add(new BindableProperty(
                member.Property,
                GetConfigurationNames(member.Property, member.IsConstructorBound).ToImmutableArray(),
                member.IsConstructorBound,
                member.ConstructorParameterCanUseDefault,
                HasValidationAttribute(member.Property),
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

        if (ImplementsInterface(namedType, "System.ComponentModel.DataAnnotations.IValidatableObject"))
        {
            return true;
        }

        if (HasValidationAttribute(namedType))
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

    private static bool IsPotentialNestedObject(ITypeSymbol type)
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
            }
        }

        return false;
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
            IsStringComparerType(field.Type))
        {
            foreach (var declaration in field.DeclaringSyntaxReferences
                         .Select(reference => reference.GetSyntax())
                         .OfType<VariableDeclaratorSyntax>())
            {
                if (declaration.Initializer?.Value is { } value)
                {
                    initializer = value;
                    return true;
                }
            }
        }

        if (symbol is IPropertySymbol property &&
            IsStringComparerType(property.Type))
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

    private static bool IsStringComparerType(ITypeSymbol type)
    {
        return string.Equals(type.ToDisplayString(), "System.StringComparer", StringComparison.Ordinal);
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
        bool hasPotentialPolymorphicInitializer,
        ImmutableHashSet<string> potentialPolymorphicDictionaryValueKeys)
    {
        Symbol = symbol;
        ConfigurationNames = configurationNames;
        IsConstructorBound = isConstructorBound;
        ConstructorParameterCanUseDefault = constructorParameterCanUseDefault;
        HasValidationAttribute = hasValidationAttribute;
        HasPotentialPolymorphicInitializer = hasPotentialPolymorphicInitializer;
        PotentialPolymorphicDictionaryValueKeys = potentialPolymorphicDictionaryValueKeys;
    }

    public IPropertySymbol Symbol { get; }
    public ImmutableArray<string> ConfigurationNames { get; }
    public bool IsConstructorBound { get; }
    public bool ConstructorParameterCanUseDefault { get; }
    public bool HasValidationAttribute { get; }
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
