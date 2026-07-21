using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConfigContraband;

internal sealed partial class OptionsTypeMetadata
{
    private static bool RecursiveDefaultStillFailsValidation(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        bool bindsNonPublicProperties,
        Compilation? compilation,
        bool? recursiveValidationEnabledOverride = null)
    {
        // Recursive validation walks the default instance, so a nested required member without
        // its own satisfying default keeps the parent key required.
        if (!(recursiveValidationEnabledOverride ?? IsRecursiveValidationEnabled(property)))
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

        // No initializer (and the caller has proven no constructor writes the member). A
        // non-nullable value type still has a non-null default(T) instance that DataAnnotations
        // recursively validates — structs are sealed, so it is a clean, unmutated instance of
        // the declared type — so classify it as Modelled. A reference type or Nullable<T> stays
        // null by default, which validation skips.
        return property.Type.IsValueType && !IsNullableValueType(property.Type)
            ? RecursiveDefaultKind.Modelled
            : RecursiveDefaultKind.None;
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
}
