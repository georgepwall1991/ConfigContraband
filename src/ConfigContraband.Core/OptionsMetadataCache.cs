using System;
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace ConfigContraband;

/// <summary>
/// Per-compilation memoization for <see cref="OptionsTypeMetadata"/>. Building metadata walks the
/// whole options type graph (attributes, constructor proofs, syntax references), and a single
/// options registration can request the same <c>(type, bindsNonPublicProperties)</c> metadata several
/// times across the validation, nested-validation, and unknown-key passes. Metadata is an immutable
/// pure function of those inputs plus the compilation, so it is safe to share under concurrent
/// execution. The analyzer keeps one cache per <see cref="Compilation"/> through a
/// <c>ConditionalWeakTable</c>, so the cache is collected with its compilation and never leaks.
/// </summary>
internal sealed class OptionsMetadataCache
{
    private readonly ConcurrentDictionary<CacheKey, OptionsTypeMetadata> _entries = new();

    public bool TryGet(INamedTypeSymbol type, bool bindsNonPublicProperties, out OptionsTypeMetadata metadata)
    {
        return _entries.TryGetValue(new CacheKey(type, bindsNonPublicProperties), out metadata!);
    }

    public void Add(INamedTypeSymbol type, bool bindsNonPublicProperties, OptionsTypeMetadata metadata)
    {
        _entries.TryAdd(new CacheKey(type, bindsNonPublicProperties), metadata);
    }

    private readonly struct CacheKey : IEquatable<CacheKey>
    {
        public CacheKey(INamedTypeSymbol type, bool bindsNonPublicProperties)
        {
            Type = type;
            BindsNonPublicProperties = bindsNonPublicProperties;
        }

        public INamedTypeSymbol Type { get; }

        public bool BindsNonPublicProperties { get; }

        public bool Equals(CacheKey other)
        {
            return BindsNonPublicProperties == other.BindsNonPublicProperties &&
                   SymbolEqualityComparer.Default.Equals(Type, other.Type);
        }

        public override bool Equals(object? obj)
        {
            return obj is CacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (SymbolEqualityComparer.Default.GetHashCode(Type) * 397) ^ BindsNonPublicProperties.GetHashCode();
            }
        }
    }
}
