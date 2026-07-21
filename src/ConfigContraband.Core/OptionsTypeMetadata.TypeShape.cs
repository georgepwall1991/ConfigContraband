using System;
using System.Linq;
using Microsoft.CodeAnalysis;

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

}
