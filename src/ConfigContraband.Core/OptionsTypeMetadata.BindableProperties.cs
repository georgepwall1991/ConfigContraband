using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConfigContraband;

internal sealed partial class OptionsTypeMetadata
{
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
        return TryGetDictionaryTypeArguments(type, out _, out valueType);
    }

    /// <summary>
    /// Same as <see cref="TryGetDictionaryValueType"/>, but also requires the dictionary's key
    /// type to be one the real ConfigurationBinder actually binds (string, enum, or integral).
    /// BindDictionary/BindDictionaryInterface silently return without binding anything for any
    /// other key type (Guid, double, bool, TimeSpan, custom struct, ...), so no CFG006/CFG007
    /// recursion or reporting should ever occur under such a dictionary's values - the runtime
    /// never evaluates them. Use this (not <see cref="TryGetDictionaryValueType"/>) at every
    /// analyzer/metadata decision point that decides whether to recurse into or report unknown
    /// keys under dictionary values; the schema builder intentionally keeps using the unfiltered
    /// <see cref="TryGetDictionaryValueType"/> so its output stays unaffected by this key-support
    /// boundary.
    /// </summary>
    public static bool TryGetSupportedDictionaryValueType(ITypeSymbol type, out ITypeSymbol valueType)
    {
        if (TryGetDictionaryTypeArguments(type, out var keyType, out valueType) &&
            IsSupportedRuntimeDictionaryKeyType(keyType))
        {
            return true;
        }

        valueType = null!;
        return false;
    }

    internal static bool IsSupportedRuntimeDictionaryKeyType(ITypeSymbol keyType)
    {
        if (keyType.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        return keyType.SpecialType is
            SpecialType.System_String or
            SpecialType.System_SByte or
            SpecialType.System_Byte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64;
    }

    private static bool TryGetDictionaryTypeArguments(ITypeSymbol type, out ITypeSymbol keyType, out ITypeSymbol valueType)
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
                    keyType = iface.TypeArguments[0];
                    valueType = iface.TypeArguments[1];
                    return true;
                }
            }
        }

        keyType = null!;
        valueType = null!;
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
               postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
        {
            expression = postfix.Operand;
        }

        return expression;
    }

    private static bool IsInitializerDefinitelyNullOrDefault(ExpressionSyntax initializer)
    {
        initializer = StripInitializerWrappers(initializer);
        if (initializer is CastExpressionSyntax cast)
        {
            return IsInitializerDefinitelyNullOrDefault(cast.Expression);
        }

        return initializer.IsKind(SyntaxKind.NullLiteralExpression) ||
               initializer.IsKind(SyntaxKind.DefaultLiteralExpression) ||
               initializer is DefaultExpressionSyntax;
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
            // A get-only nested member is only bindable when the binder can mutate the
            // existing instance in place. That holds for reference types (a class nested
            // object, or a mutable collection), but not for a value-type struct: the binder
            // binds a copy it cannot write back through the read-only property, so a get-only
            // struct keeps its default and must not be treated as a bindable nested object.
            ((IsPotentialNestedObject(property.Type) && property.Type.IsReferenceType) ||
             IsMutableCollectionType(property.Type)))
        {
            return true;
        }

        return false;
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

        property = GetRuntimeBindingProperty(property);
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
        property = GetRuntimeBindingProperty(property);
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
        property = GetRuntimeBindingProperty(property);
        foreach (var attribute in property.GetAttributes())
        {
            if (TryGetConfigurationAlias(attribute, out _))
            {
                return true;
            }
        }

        return false;
    }

    internal static IPropertySymbol GetRuntimeBindingProperty(IPropertySymbol property)
    {
        // ConfigurationBinder.GetAllProperties keeps only the base-most declaration of a
        // virtual property, using the setter's base definition to identify overrides.
        while (property.SetMethod?.OverriddenMethod is not null &&
               property.OverriddenProperty is { } overriddenProperty)
        {
            property = overriddenProperty;
        }

        return property;
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
