using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ConfigContraband;

internal sealed partial class OptionsTypeMetadata
{
    private static bool HasValidationAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(attribute => InheritsFrom(attribute.AttributeClass, "System.ComponentModel.DataAnnotations.ValidationAttribute"));
    }

    private static bool IsRequired(ISymbol symbol)
    {
        if (symbol.GetAttributes().Any(attribute => IsRequiredAttribute(attribute.AttributeClass)))
        {
            return symbol is not IPropertySymbol property ||
                   !property.Type.IsValueType ||
                   IsNullableValueType(property.Type);
        }

        return false;
    }

    private static bool IsRequiredAttribute(INamedTypeSymbol? attributeClass)
    {
        if (attributeClass is null)
        {
            return false;
        }

        // The runtime validator enforces RequiredAttribute and any subclass that inherits its
        // check, so match by inheritance rather than an exact type name. A subclass that overrides
        // IsValid may weaken the check (e.g. accept a missing value), so it can no longer be proven
        // required — stay conservative and treat only RequiredAttribute itself or a subclass that
        // does not override IsValid as required.
        var overridesIsValid = false;
        for (var current = attributeClass; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.ToDisplayString(), "System.ComponentModel.DataAnnotations.RequiredAttribute", StringComparison.Ordinal))
            {
                return !overridesIsValid;
            }

            if (current.GetMembers("IsValid").OfType<IMethodSymbol>().Any(method => method.IsOverride))
            {
                overridesIsValid = true;
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

    private static bool HasNonRequiredValidationAttribute(ISymbol symbol)
    {
        // Exclude only the RequiredAttribute forms IsRequired actually proves (RequiredAttribute
        // itself or a subclass that does not override IsValid): for those the required signal is
        // already handled by IsRequired plus the satisfying-default proof, so counting them here
        // too would keep the key required even when the compile-time default satisfies them. A
        // subclass that overrides IsValid is NOT proven required (IsRequiredAttribute returns
        // false), so it must still count as a validating attribute here — otherwise its property's
        // validation is ignored entirely and a nested-default failure is missed.
        return symbol.GetAttributes().Any(attribute =>
            InheritsFrom(attribute.AttributeClass, "System.ComponentModel.DataAnnotations.ValidationAttribute") &&
            !IsRequiredAttribute(attribute.AttributeClass));
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

    private static bool HasAttribute(ISymbol symbol, string metadataName)
    {
        return symbol.GetAttributes().Any(attribute =>
            string.Equals(attribute.AttributeClass?.ToDisplayString(), metadataName, StringComparison.Ordinal));
    }

}
