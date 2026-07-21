using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ConfigContraband;

public sealed partial class ConfigContrabandAnalyzer
{
    private static void AnalyzeUnknownKeys(
        Action<Diagnostic> reportDiagnostic,
        OptionsRegistration registration,
        ConfigurationSnapshot configuration,
        ConfigurationProviderSemantics providerSemantics,
        ConcurrentDictionary<string, byte> unknownKeysReported,
        Compilation compilation,
        bool strictUnknownConfigurationKeySuppressed)
    {
        if (registration.RequiresRuntimeSection &&
            configuration.GetSectionExistence(registration.SectionPath, providerSemantics) ==
                ConfigurationSectionExistence.Missing)
        {
            return;
        }

        var sections = configuration.FindSections(registration.SectionPath);
        if (sections.IsDefaultOrEmpty)
        {
            return;
        }

        var metadata = OptionsTypeMetadata.Create(
            registration.OptionsType,
            registration.BindsNonPublicProperties,
            compilation);

        AnalyzeRequiredKeysAcrossSections(
            reportDiagnostic,
            sections,
            metadata,
            registration.SectionPath ?? "",
            registration.BindLocation,
            compilation,
            registration.IsDataAnnotationsEnabled);

        foreach (var matchingSection in sections)
        {
            AnalyzeUnknownKeysInSection(
                reportDiagnostic,
                matchingSection,
                metadata,
                unknownKeysReported,
                registration.ErrorsOnUnknownConfiguration,
                strictUnknownConfigurationKeySuppressed,
                compilation);
        }
    }

    private static void AnalyzeRequiredKeysAcrossSections(
        Action<Diagnostic> reportDiagnostic,
        ImmutableArray<ConfigurationNode> sections,
        OptionsTypeMetadata metadata,
        string sectionPath,
        Location location,
        Compilation compilation,
        bool dataAnnotationsEnabled)
    {
        if (!dataAnnotationsEnabled)
        {
            return;
        }

        foreach (var property in metadata.BindableProperties)
        {
            var found = false;
            var nestedSectionsBuilder = ImmutableArray.CreateBuilder<ConfigurationNode>();
            string? matchedConfigName = null;

            foreach (var section in sections)
            {
                foreach (var configName in property.ConfigurationNames)
                {
                    if (section.TryGetProperty(configName, out var matchedProperty))
                    {
                        found = true;
                        matchedConfigName ??= matchedProperty.Key;
                        nestedSectionsBuilder.Add(matchedProperty.Value);
                    }
                }
            }

            if (property.IsRequired && !found)
            {
                var displayName = property.ConfigurationNames.Length > 0
                    ? property.ConfigurationNames[0]
                    : property.Symbol.Name;

                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MissingRequiredConfigurationKey,
                    location,
                    displayName,
                    sectionPath));
            }

            if (found && nestedSectionsBuilder.Count > 0)
            {
                var subPath = sectionPath + ":" + (matchedConfigName ?? property.Symbol.Name);
                if (metadata.TryCreateNestedMetadata(property, out var nestedMetadata))
                {
                    if (property.IsRecursiveValidationEnabled)
                    {
                        AnalyzeRequiredKeysAcrossSections(
                            reportDiagnostic,
                            nestedSectionsBuilder.ToImmutable(),
                            nestedMetadata,
                            subPath,
                            location,
                            compilation,
                            dataAnnotationsEnabled);
                    }
                }
                else if (metadata.TryCreateCollectionElementMetadata(property, out var elementMetadata))
                {
                    if (property.IsRecursiveValidationEnabled)
                    {
                        var elementEntries = new Dictionary<string, ImmutableArray<ConfigurationNode>.Builder>(StringComparer.Ordinal);
                        foreach (var section in nestedSectionsBuilder)
                        {
                            foreach (var entry in section.Properties)
                            {
                                if (int.TryParse(entry.Key, out _))
                                {
                                    if (!elementEntries.TryGetValue(entry.Key, out var builder))
                                    {
                                        builder = ImmutableArray.CreateBuilder<ConfigurationNode>();
                                        elementEntries[entry.Key] = builder;
                                    }
                                    builder.Add(entry.Value);
                                }
                            }
                        }

                        foreach (var entry in elementEntries)
                        {
                            AnalyzeRequiredKeysAcrossSections(
                                reportDiagnostic,
                                entry.Value.ToImmutable(),
                                elementMetadata,
                                subPath + ":" + entry.Key,
                                location,
                                compilation,
                                dataAnnotationsEnabled);
                        }
                    }
                }
            }
            else if (property.IsRecursiveValidationEnabled &&
                     metadata.HasProvableNonNullRecursiveDefault(property))
            {
                // Recurse into provably initialized objects even if the section is missing from
                // config; null members are skipped by runtime validation and unprovable defaults
                // would make declared-type findings speculative. Use the configured
                // ([ConfigurationKeyName]) name for the reported child path, since the section is
                // absent so there is no matched key to fall back on and the runtime binder keys
                // the child by its configured name, not the CLR property name.
                var childName = property.ConfigurationNames.Length > 0
                    ? property.ConfigurationNames[0]
                    : property.Symbol.Name;
                var subPath = sectionPath + ":" + childName;
                if (metadata.TryCreateNestedMetadata(property, out var nestedMetadata))
                {
                    AnalyzeRequiredKeysAcrossSections(
                        reportDiagnostic,
                        ImmutableArray<ConfigurationNode>.Empty,
                        nestedMetadata,
                        subPath,
                        location,
                        compilation,
                        dataAnnotationsEnabled);
                }
            }
        }
    }

    private static void AnalyzeUnknownKeysInSection(
        Action<Diagnostic> reportDiagnostic,
        ConfigurationNode section,
        OptionsTypeMetadata metadata,
        ConcurrentDictionary<string, byte> unknownKeysReported,
        bool errorsOnUnknownConfiguration,
        bool strictUnknownConfigurationKeySuppressed,
        Compilation compilation)
    {
        var knownNames = metadata.GetConfigurationNames();
        foreach (var property in section.Properties)
        {
            var propertyStrictUnknownConfigurationKeySuppressed =
                strictUnknownConfigurationKeySuppressed ||
                property.StrictUnknownConfigurationKeySuppressedByAnalyzerConfig;
            var reportStrictUnknownKeys = errorsOnUnknownConfiguration &&
                !propertyStrictUnknownConfigurationKeySuppressed;

            if (!metadata.TryGetConfigurationProperty(property.Key, out var bindableProperty))
            {
                if (metadata.TryGetSettableConstructorBoundAlias(property.Key, section, out var constructorAliasProperty))
                {
                    if (reportStrictUnknownKeys &&
                        metadata.IsConfigurationAlias(constructorAliasProperty, property.Key))
                    {
                        ReportUnknownConfigurationKey(
                            reportDiagnostic,
                            unknownKeysReported,
                            metadata.TypeKey,
                            DiagnosticDescriptors.UnknownConfigurationKeyWillThrow,
                            property.Location,
                            property.FullPath,
                            property.Key,
                            metadata.TypeName,
                            ImmutableArray<string>.Empty);
                    }

                    continue;
                }

                if (reportStrictUnknownKeys &&
                    !property.Value.Properties.IsDefaultOrEmpty &&
                    metadata.TryGetClrPropertyNamed(property.Key, out var clrProperty) &&
                    clrProperty is not null &&
                    metadata.CanStrictBindObjectShapedClrOnlyProperty(clrProperty))
                {
                    ReportStrictScalarChildKeys(
                        reportDiagnostic,
                        unknownKeysReported,
                        metadata.TypeKey + "|" + clrProperty.Name,
                        clrProperty.Type,
                        property.Value,
                        suppressKnownClrProperties: true,
                        strictUnknownConfigurationKeySuppressed);
                    continue;
                }

                var descriptor = reportStrictUnknownKeys &&
                    !metadata.HasClrPropertyNamed(property.Key)
                    ? DiagnosticDescriptors.UnknownConfigurationKeyWillThrow
                    : DiagnosticDescriptors.UnknownConfigurationKey;
                ReportUnknownConfigurationKey(
                    reportDiagnostic,
                    unknownKeysReported,
                    metadata.TypeKey,
                    descriptor,
                    property.Location,
                    property.FullPath,
                    property.Key,
                    metadata.TypeName,
                    reportStrictUnknownKeys
                        ? metadata.GetStrictBindingSuggestionNames()
                        : knownNames);

                continue;
            }

            if (reportStrictUnknownKeys &&
                metadata.IsConfigurationAlias(bindableProperty, property.Key))
            {
                ReportUnknownConfigurationKey(
                    reportDiagnostic,
                    unknownKeysReported,
                    metadata.TypeKey,
                    DiagnosticDescriptors.UnknownConfigurationKeyWillThrow,
                    property.Location,
                    property.FullPath,
                    property.Key,
                    metadata.TypeName,
                    ImmutableArray<string>.Empty);
                continue;
            }

            if (property.Value.Properties.IsDefaultOrEmpty)
            {
                AnalyzeScalarValueConversion(
                    reportDiagnostic,
                    unknownKeysReported,
                    bindableProperty,
                    property);
                continue;
            }

            if (metadata.TryCreateNestedMetadata(bindableProperty, out var nestedMetadata))
            {
                var nestedErrorsOnUnknownConfiguration = errorsOnUnknownConfiguration &&
                    !bindableProperty.HasPotentialPolymorphicInitializer;
                AnalyzeUnknownKeysInSection(
                    reportDiagnostic,
                    property.Value,
                    nestedMetadata,
                    unknownKeysReported,
                    nestedErrorsOnUnknownConfiguration,
                    strictUnknownConfigurationKeySuppressed,
                    compilation);
                continue;
            }

            if (metadata.TryCreateDictionaryValueMetadata(bindableProperty, out var dictionaryValueMetadata))
            {
                foreach (var entry in property.Value.Properties)
                {
                    if (!entry.Value.Properties.IsDefaultOrEmpty)
                    {
                        var dictionaryErrorsOnUnknownConfiguration = errorsOnUnknownConfiguration &&
                            !bindableProperty.HasPotentialPolymorphicDictionaryValueInitializerForKey(entry.Key);
                        AnalyzeUnknownKeysInSection(
                            reportDiagnostic,
                            entry.Value,
                            dictionaryValueMetadata,
                            unknownKeysReported,
                            dictionaryErrorsOnUnknownConfiguration,
                            strictUnknownConfigurationKeySuppressed,
                            compilation);
                    }
                }

                continue;
            }

            if (metadata.TryCreateDictionaryValueCollectionElementMetadata(bindableProperty, out var dictionaryValueElementMetadata))
            {
                foreach (var entry in property.Value.Properties)
                {
                    foreach (var item in entry.Value.Properties)
                    {
                        if (!item.Value.Properties.IsDefaultOrEmpty)
                        {
                            AnalyzeUnknownKeysInSection(
                                reportDiagnostic,
                                item.Value,
                                dictionaryValueElementMetadata,
                                unknownKeysReported,
                                errorsOnUnknownConfiguration,
                                strictUnknownConfigurationKeySuppressed,
                                compilation);
                        }
                    }
                }

                continue;
            }

            if (!metadata.TryCreateCollectionElementMetadata(bindableProperty, out var elementMetadata))
            {
                if (reportStrictUnknownKeys)
                {
                    var reportKeyPrefix = metadata.TypeKey + "|" + bindableProperty.Symbol.Name;

                    // A dictionary's own IEnumerable<KeyValuePair<TKey, TValue>> shape must never
                    // fall through to the generic collection/scalar branches below: an unsupported
                    // dictionary key type (see TryGetSupportedDictionaryValueType) makes this
                    // property opaque - the runtime binder never evaluates it either - rather than
                    // reclassifying it as a collection of KeyValuePair or a plain scalar.
                    var isDictionary = OptionsTypeMetadata.TryGetDictionaryValueType(bindableProperty.Symbol.Type, out _);
                    if (OptionsTypeMetadata.TryGetSupportedDictionaryValueType(bindableProperty.Symbol.Type, out var dictionaryValueType))
                    {
                        if (OptionsTypeMetadata.TryGetDictionaryValueType(dictionaryValueType, out _))
                        {
                            ReportStrictNestedDictionaryChildKeys(
                                reportDiagnostic,
                                unknownKeysReported,
                                reportKeyPrefix,
                                dictionaryValueType,
                                property.Value,
                                bindableProperty,
                                ImmutableArray<string>.Empty,
                                metadata.BindsNonPublicProperties,
                                strictUnknownConfigurationKeySuppressed,
                                compilation);
                            continue;
                        }

                        if (OptionsTypeMetadata.TryGetCollectionElementType(dictionaryValueType, out var dictionaryValueElementType) &&
                            IsStrictScalarValueType(dictionaryValueElementType) &&
                            !IsOpenRuntimeShape(dictionaryValueElementType))
                        {
                            ReportStrictScalarDictionaryValueCollectionChildKeys(
                                reportDiagnostic,
                                unknownKeysReported,
                                reportKeyPrefix,
                                dictionaryValueElementType,
                                property.Value,
                                strictUnknownConfigurationKeySuppressed);
                        }
                        else if (IsStrictScalarValueType(dictionaryValueType) &&
                                 !IsOpenRuntimeShape(dictionaryValueType))
                        {
                            ReportStrictScalarDictionaryValueChildKeys(
                                reportDiagnostic,
                                unknownKeysReported,
                                reportKeyPrefix,
                                dictionaryValueType,
                                property.Value,
                                strictUnknownConfigurationKeySuppressed);
                        }
                    }
                    else if (!isDictionary &&
                             OptionsTypeMetadata.TryGetCollectionElementType(bindableProperty.Symbol.Type, out var collectionElementType))
                    {
                        if (IsStrictScalarValueType(collectionElementType) &&
                            !IsOpenRuntimeShape(collectionElementType))
                        {
                            ReportStrictScalarCollectionChildKeys(
                                reportDiagnostic,
                                unknownKeysReported,
                                reportKeyPrefix,
                                collectionElementType,
                                property.Value,
                                strictUnknownConfigurationKeySuppressed);
                        }
                    }
                    else if (!isDictionary &&
                             !IsOpenRuntimeShape(bindableProperty.Symbol.Type))
                    {
                        ReportStrictScalarChildKeys(
                            reportDiagnostic,
                            unknownKeysReported,
                            reportKeyPrefix,
                            bindableProperty.Symbol.Type,
                            property.Value,
                            suppressKnownClrProperties: CanSuppressKnownBindableScalarClrProperties(metadata, bindableProperty),
                            strictUnknownConfigurationKeySuppressed);
                    }
                }

                continue;
            }

            foreach (var item in property.Value.Properties)
            {
                if (!item.Value.Properties.IsDefaultOrEmpty)
                {
                    AnalyzeUnknownKeysInSection(
                        reportDiagnostic,
                        item.Value,
                        elementMetadata,
                        unknownKeysReported,
                        errorsOnUnknownConfiguration,
                        strictUnknownConfigurationKeySuppressed,
                        compilation);
                }
            }
        }
    }

    private static bool IsStrictScalarValueType(ITypeSymbol type)
    {
        return !OptionsTypeMetadata.TryGetDictionaryValueType(type, out _) &&
               !OptionsTypeMetadata.TryGetCollectionElementType(type, out _);
    }

    private static void ReportStrictScalarChildKeys(
        Action<Diagnostic> reportDiagnostic,
        ConcurrentDictionary<string, byte> unknownKeysReported,
        string reportKeyPrefix,
        ITypeSymbol valueType,
        ConfigurationNode value,
        bool suppressKnownClrProperties,
        bool strictUnknownConfigurationKeySuppressed)
    {
        var effectiveValueType = UnwrapNullableValueType(valueType);
        var canSuppressKnownClrProperties = suppressKnownClrProperties &&
            CanSuppressKnownStrictScalarClrProperties(effectiveValueType);
        var knownNames = canSuppressKnownClrProperties
            ? OptionsTypeMetadata.GetClrPropertyNames(effectiveValueType)
            : ImmutableArray<string>.Empty;
        foreach (var child in value.Properties)
        {
            if (strictUnknownConfigurationKeySuppressed ||
                child.StrictUnknownConfigurationKeySuppressedByAnalyzerConfig)
            {
                continue;
            }

            if (canSuppressKnownClrProperties &&
                OptionsTypeMetadata.TryGetClrProperty(effectiveValueType, child.Key, out var childProperty))
            {
                if (childProperty is not null &&
                    childProperty.DeclaredAccessibility == Accessibility.Public &&
                    !childProperty.IsStatic &&
                    childProperty.Parameters.Length == 0 &&
                    !child.Value.Properties.IsDefaultOrEmpty)
                {
                    ReportStrictScalarChildKeys(
                        reportDiagnostic,
                        unknownKeysReported,
                        reportKeyPrefix,
                        childProperty.Type,
                        child.Value,
                        suppressKnownClrProperties: true,
                        strictUnknownConfigurationKeySuppressed);
                }

                continue;
            }

            ReportUnknownConfigurationKey(
                reportDiagnostic,
                unknownKeysReported,
                reportKeyPrefix,
                DiagnosticDescriptors.UnknownConfigurationKeyWillThrow,
                child.Location,
                child.FullPath,
                child.Key,
                effectiveValueType.Name,
                knownNames);
        }
    }

    private static void ReportStrictScalarCollectionChildKeys(
        Action<Diagnostic> reportDiagnostic,
        ConcurrentDictionary<string, byte> unknownKeysReported,
        string reportKeyPrefix,
        ITypeSymbol elementType,
        ConfigurationNode collectionValue,
        bool strictUnknownConfigurationKeySuppressed)
    {
        foreach (var item in collectionValue.Properties)
        {
            if (!item.Value.Properties.IsDefaultOrEmpty)
            {
                ReportStrictScalarChildKeys(
                    reportDiagnostic,
                    unknownKeysReported,
                    reportKeyPrefix,
                    elementType,
                    item.Value,
                    suppressKnownClrProperties: CanSuppressKnownStrictCollectionItemClrProperties(elementType),
                    strictUnknownConfigurationKeySuppressed);
            }
        }
    }

    private static void ReportStrictScalarDictionaryValueChildKeys(
        Action<Diagnostic> reportDiagnostic,
        ConcurrentDictionary<string, byte> unknownKeysReported,
        string reportKeyPrefix,
        ITypeSymbol valueType,
        ConfigurationNode dictionaryValue,
        bool strictUnknownConfigurationKeySuppressed)
    {
        foreach (var entry in dictionaryValue.Properties)
        {
            if (!entry.Value.Properties.IsDefaultOrEmpty)
            {
                ReportStrictScalarChildKeys(
                    reportDiagnostic,
                    unknownKeysReported,
                    reportKeyPrefix,
                    valueType,
                    entry.Value,
                    suppressKnownClrProperties: CanSuppressKnownStrictCollectionItemClrProperties(valueType),
                    strictUnknownConfigurationKeySuppressed);
            }
        }
    }

    private static void ReportStrictScalarDictionaryValueCollectionChildKeys(
        Action<Diagnostic> reportDiagnostic,
        ConcurrentDictionary<string, byte> unknownKeysReported,
        string reportKeyPrefix,
        ITypeSymbol elementType,
        ConfigurationNode dictionaryValue,
        bool strictUnknownConfigurationKeySuppressed)
    {
        foreach (var entry in dictionaryValue.Properties)
        {
            ReportStrictScalarCollectionChildKeys(
                reportDiagnostic,
                unknownKeysReported,
                reportKeyPrefix,
                elementType,
                entry.Value,
                strictUnknownConfigurationKeySuppressed);
        }
    }

    private static void ReportStrictNestedDictionaryChildKeys(
        Action<Diagnostic> reportDiagnostic,
        ConcurrentDictionary<string, byte> unknownKeysReported,
        string reportKeyPrefix,
        ITypeSymbol dictionaryType,
        ConfigurationNode dictionaryValue,
        BindableProperty bindableProperty,
        ImmutableArray<string> dictionaryPath,
        bool bindsNonPublicProperties,
        bool strictUnknownConfigurationKeySuppressed,
        Compilation compilation)
    {
        if (!OptionsTypeMetadata.TryGetSupportedDictionaryValueType(dictionaryType, out var valueType))
        {
            return;
        }

        foreach (var entry in dictionaryValue.Properties)
        {
            var entryPath = dictionaryPath.Add(entry.Key);
            if (entry.Value.Properties.IsDefaultOrEmpty)
            {
                continue;
            }

            if (OptionsTypeMetadata.TryGetDictionaryValueType(valueType, out _))
            {
                ReportStrictNestedDictionaryChildKeys(
                    reportDiagnostic,
                    unknownKeysReported,
                    reportKeyPrefix,
                    valueType,
                    entry.Value,
                    bindableProperty,
                    entryPath,
                    bindsNonPublicProperties,
                    strictUnknownConfigurationKeySuppressed,
                    compilation);
            }
            else if (OptionsTypeMetadata.TryGetCollectionElementType(valueType, out var elementType))
            {
                if (IsStrictScalarValueType(elementType) &&
                    !IsOpenRuntimeShape(elementType) &&
                    !IsUserDefinedReferenceObject(elementType))
                {
                    ReportStrictScalarDictionaryValueCollectionChildKeys(
                        reportDiagnostic,
                        unknownKeysReported,
                        reportKeyPrefix,
                        elementType,
                        entry.Value,
                        strictUnknownConfigurationKeySuppressed);
                }
                else if (elementType is INamedTypeSymbol namedElementType &&
                         IsUserDefinedReferenceObject(elementType))
                {
                    ReportStrictNestedDictionaryObjectCollectionChildKeys(
                        reportDiagnostic,
                        unknownKeysReported,
                        namedElementType,
                        entry.Value,
                        bindsNonPublicProperties,
                        strictUnknownConfigurationKeySuppressed,
                        compilation);
                }
            }
            else if (IsStrictScalarValueType(valueType) &&
                     !IsUserDefinedReferenceObject(valueType) &&
                     !IsOpenRuntimeShape(valueType))
            {
                ReportStrictScalarDictionaryValueChildKeys(
                    reportDiagnostic,
                    unknownKeysReported,
                    reportKeyPrefix,
                    valueType,
                    entry.Value,
                    strictUnknownConfigurationKeySuppressed);
            }
            else if (valueType is INamedTypeSymbol namedValueType &&
                     IsUserDefinedReferenceObject(valueType))
            {
                var valueMetadata = OptionsTypeMetadata.Create(namedValueType, bindsNonPublicProperties, compilation);
                foreach (var nestedEntry in entry.Value.Properties)
                {
                    if (!nestedEntry.Value.Properties.IsDefaultOrEmpty)
                    {
                        var nestedPath = entryPath.Add(nestedEntry.Key);
                        AnalyzeUnknownKeysInSection(
                            reportDiagnostic,
                            nestedEntry.Value,
                            valueMetadata,
                            unknownKeysReported,
                            errorsOnUnknownConfiguration: !bindableProperty.HasPotentialPolymorphicDictionaryValueInitializerForPath(nestedPath),
                            strictUnknownConfigurationKeySuppressed: strictUnknownConfigurationKeySuppressed,
                            compilation: compilation);
                    }
                }
            }
        }
    }

    private static void ReportStrictNestedDictionaryObjectCollectionChildKeys(
        Action<Diagnostic> reportDiagnostic,
        ConcurrentDictionary<string, byte> unknownKeysReported,
        INamedTypeSymbol elementType,
        ConfigurationNode dictionaryValue,
        bool bindsNonPublicProperties,
        bool strictUnknownConfigurationKeySuppressed,
        Compilation compilation)
    {
        var elementMetadata = OptionsTypeMetadata.Create(elementType, bindsNonPublicProperties, compilation);
        foreach (var nestedEntry in dictionaryValue.Properties)
        {
            foreach (var item in nestedEntry.Value.Properties)
            {
                if (!item.Value.Properties.IsDefaultOrEmpty)
                {
                    AnalyzeUnknownKeysInSection(
                        reportDiagnostic,
                        item.Value,
                        elementMetadata,
                        unknownKeysReported,
                        errorsOnUnknownConfiguration: true,
                        strictUnknownConfigurationKeySuppressed: strictUnknownConfigurationKeySuppressed,
                        compilation: compilation);
                }
            }
        }
    }

    private static ITypeSymbol UnwrapNullableValueType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { IsGenericType: true } namedType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            namedType.TypeArguments.Length == 1)
        {
            return namedType.TypeArguments[0];
        }

        return type;
    }

    private static bool CanSuppressKnownStrictScalarClrProperties(ITypeSymbol type)
    {
        return type.TypeKind is TypeKind.Class or TypeKind.Struct;
    }

    private static bool CanSuppressKnownStrictCollectionItemClrProperties(ITypeSymbol type)
    {
        return type.IsValueType ||
               CanCreateDefaultReferenceValue(type);
    }

    private static bool CanCreateDefaultReferenceValue(ITypeSymbol type)
    {
        return type.TypeKind == TypeKind.Class &&
               type is INamedTypeSymbol { IsAbstract: false } namedType &&
               namedType.InstanceConstructors.Any(static constructor =>
                   constructor.DeclaredAccessibility == Accessibility.Public &&
                   constructor.Parameters.Length == 0);
    }

    private static bool CanSuppressKnownBindableScalarClrProperties(
        OptionsTypeMetadata metadata,
        BindableProperty property)
    {
        return IsNullableValueType(property.Symbol.Type) ||
               metadata.CanStrictBindObjectShapedClrOnlyProperty(property.Symbol);
    }

    private static bool IsNullableValueType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { IsGenericType: true } namedType &&
               namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }

    private static bool IsOpenRuntimeShape(ITypeSymbol type)
    {
        return type.TypeKind == TypeKind.Interface ||
               type.SpecialType == SpecialType.System_Object;
    }

    private static bool IsUserDefinedReferenceObject(ITypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class ||
            type.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        var namespaceName = type.ContainingNamespace.ToDisplayString();
        return !string.Equals(namespaceName, "System", StringComparison.Ordinal) &&
               !namespaceName.StartsWith("System.", StringComparison.Ordinal);
    }

    private static bool ContainsName(ImmutableArray<string> names, string key)
    {
        foreach (var name in names)
        {
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void ReportUnknownConfigurationKey(
        Action<Diagnostic> reportDiagnostic,
        ConcurrentDictionary<string, byte> unknownKeysReported,
        string typeKey,
        DiagnosticDescriptor descriptor,
        Location location,
        string fullPath,
        string key,
        string typeName,
        ImmutableArray<string> knownNames)
    {
        var reportKey = typeKey + "|" + descriptor.Id + "|" + location.GetLineSpan().Path + "|" + fullPath;
        if (!unknownKeysReported.TryAdd(reportKey, 0))
        {
            return;
        }

        var suggestion = FindClosest(key, knownNames);
        var suffix = suggestion is null ? "." : $". Did you mean \"{suggestion}\"?";

        reportDiagnostic(Diagnostic.Create(
            descriptor,
            location,
            fullPath,
            typeName,
            suffix));
    }

    private static void AnalyzeScalarValueConversion(
        Action<Diagnostic> reportDiagnostic,
        ConcurrentDictionary<string, byte> unknownKeysReported,
        BindableProperty bindableProperty,
        ConfigurationProperty property)
    {
        if (!ScalarConversion.IsProvablyNotConvertible(
                bindableProperty.Symbol.Type,
                property.ScalarKind,
                property.ScalarValue))
        {
            return;
        }

        var location = property.ValueLocation ?? property.Location;
        var reportKey = CreateConfigurationValueTypeMismatchReportKey(
            bindableProperty.Symbol.Type,
            location,
            property.FullPath);
        if (!unknownKeysReported.TryAdd(reportKey, 0))
        {
            return;
        }

        reportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.ConfigurationValueTypeMismatch,
            location,
            property.FullPath,
            bindableProperty.Symbol.Type.ToDisplayString()));
    }

    private static string CreateConfigurationValueTypeMismatchReportKey(
        ITypeSymbol targetType,
        Location location,
        string fullPath)
    {
        return targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
               "|" + DiagnosticDescriptors.ConfigurationValueTypeMismatch.Id +
               "|" + location.GetLineSpan().Path +
               "|" + fullPath;
    }

}
