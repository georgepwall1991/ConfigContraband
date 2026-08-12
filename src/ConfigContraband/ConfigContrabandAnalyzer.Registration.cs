using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace ConfigContraband;

public sealed partial class ConfigContrabandAnalyzer
{
    private static bool TryCreateRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out OptionsRegistration registration)
    {
        registration = null!;

        if (TryCreateOptionsBuilderRegistration(invocation, semanticModel, out registration))
        {
            return true;
        }

        if (TryCreateConfigureRegistration(invocation, semanticModel, out registration))
        {
            return true;
        }

        return TryCreateBindlessValidationRegistration(invocation, semanticModel, out registration);
    }

    private static bool TryCreateOptionsBuilderRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out OptionsRegistration registration)
    {
        registration = null!;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            invocation.ArgumentList.Arguments.Count == 0)
        {
            return false;
        }

        var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type as INamedTypeSymbol;
        if (receiverType is null ||
            receiverType.Name != "OptionsBuilder" ||
            receiverType.TypeArguments.Length != 1 ||
            receiverType.ContainingNamespace.ToDisplayString() != "Microsoft.Extensions.Options" ||
            receiverType.TypeArguments[0] is not INamedTypeSymbol optionsType)
        {
            return false;
        }

        var methodName = memberAccess.Name.Identifier.ValueText;
        if (!IsOptionsBuilderConfigurationMethod(invocation, semanticModel, methodName))
        {
            return false;
        }

        ExpressionSyntax sectionExpression;
        string sectionPath;
        bool sectionExpressionContainsFullPath;
        if (string.Equals(methodName, "BindConfiguration", StringComparison.Ordinal))
        {
            if (GetInvocationArgumentExpression(
                    invocation,
                    semanticModel,
                    "configSectionPath") is not { } argumentExpression)
            {
                return false;
            }

            sectionExpression = argumentExpression;
            if (!TryGetConstantSectionPath(sectionExpression, semanticModel, out sectionPath))
            {
                return false;
            }

            sectionExpressionContainsFullPath = true;
        }
        else if (string.Equals(methodName, "Bind", StringComparison.Ordinal))
        {
            if (GetInvocationArgumentExpression(
                    invocation,
                    semanticModel,
                    "config") is not { } configurationExpression ||
                !TryGetConfigurationSectionPath(
                    configurationExpression,
                    semanticModel,
                    out sectionPath,
                    out sectionExpression,
                    out sectionExpressionContainsFullPath))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        var chain = InvocationChain.Create(invocation, semanticModel, methodName);
        var hasAddOptionsWithValidateOnStart = HasAddOptionsWithValidateOnStartReceiver(invocation, semanticModel);
        var hasOptionsValidator = HasSameBlockOptionsValidatorRegistration(
            invocation,
            optionsType,
            semanticModel);
        var hasValidateDataAnnotations = chain.MethodNames.Contains("ValidateDataAnnotations") ||
                                         hasOptionsValidator;
        var hasValidateOnStart = chain.MethodNames.Contains("ValidateOnStart") || hasAddOptionsWithValidateOnStart;
        var hasValidation = chain.MethodNames.Any(IsValidationMethod) ||
                            hasAddOptionsWithValidateOnStart ||
                            hasOptionsValidator;
        var bindsNonPublicProperties = HasBindNonPublicPropertiesEnabled(invocation, semanticModel);
        var errorsOnUnknownConfiguration = HasErrorOnUnknownConfigurationEnabled(invocation, semanticModel);
        var supportsValidationRules = true;

        registration = new OptionsRegistration(
            optionsType,
            sectionPath,
            sectionExpression,
            chain.OutermostInvocation,
            supportsValidationRules,
            sectionExpressionContainsFullPath,
            hasValidateDataAnnotations,
            hasValidateOnStart,
            hasValidation,
            bindsNonPublicProperties,
            errorsOnUnknownConfiguration,
            hasValidateDataAnnotations,
            sectionExpression.GetLocation(),
            RequiresRuntimeSection(sectionExpression, semanticModel));
        return true;
    }

    private static ExpressionSyntax? GetInvocationArgumentExpression(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        string parameterName)
    {
        return (semanticModel.GetOperation(invocation) as IInvocationOperation)?
            .Arguments
            .FirstOrDefault(argument =>
                string.Equals(argument.Parameter?.Name, parameterName, StringComparison.Ordinal))?
            .Value.Syntax as ExpressionSyntax;
    }

    private static bool HasAddOptionsWithValidateOnStartReceiver(
        InvocationExpressionSyntax bindInvocation,
        SemanticModel semanticModel)
    {
        var current = ((MemberAccessExpressionSyntax)bindInvocation.Expression).Expression;
        while (current is InvocationExpressionSyntax invocation &&
               invocation.Expression is MemberAccessExpressionSyntax receiverMemberAccess)
        {
            if (IsAddOptionsWithValidateOnStart(invocation, semanticModel))
            {
                return true;
            }

            current = receiverMemberAccess.Expression;
        }

        return false;
    }

    private static bool IsAddOptionsWithValidateOnStart(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var original = symbol?.ReducedFrom ?? symbol;
        return original is not null &&
               string.Equals(original.Name, "AddOptionsWithValidateOnStart", StringComparison.Ordinal) &&
               string.Equals(original.ContainingType.ToDisplayString(), "Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions", StringComparison.Ordinal);
    }

    private static bool TryCreateConfigureRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out OptionsRegistration registration)
    {
        registration = null!;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            !string.Equals(memberAccess.Name.Identifier.ValueText, "Configure", StringComparison.Ordinal))
        {
            return false;
        }

        var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol is null ||
            symbol.TypeArguments.Length != 1 ||
            symbol.TypeArguments[0] is not INamedTypeSymbol optionsType ||
            !IsOptionsConfigurationConfigureMethod(symbol))
        {
            return false;
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (!TryGetConfigurationSectionPath(
                    argument.Expression,
                    semanticModel,
                    out var sectionPath,
                    out var sectionExpression,
                    out var sectionExpressionContainsFullPath))
            {
                continue;
            }

            var hasKnownOptionsName = TryGetConfigureOptionsName(
                invocation,
                argument,
                semanticModel,
                out var optionsName);
            var isDataAnnotationsEnabled = false;
            var reportNestedValidation = false;
            if (hasKnownOptionsName)
            {
                var hasOptionsValidator = HasSameBlockOptionsValidatorRegistration(
                    invocation,
                    optionsType,
                    semanticModel);
                isDataAnnotationsEnabled = hasOptionsValidator ||
                    HasSameBlockDataAnnotationsValidation(
                        invocation,
                        optionsType,
                        optionsName,
                        semanticModel);
                reportNestedValidation = hasOptionsValidator ||
                    HasSameBlockBindlessDataAnnotationsValidation(
                        invocation,
                        optionsType,
                        optionsName,
                        semanticModel);
            }

            registration = new OptionsRegistration(
                optionsType,
                sectionPath,
                sectionExpression,
                invocation,
                supportsValidationRules: false,
                sectionExpressionContainsFullPath: sectionExpressionContainsFullPath,
                hasValidateDataAnnotations: false,
                hasValidateOnStart: false,
                hasValidation: false,
                bindsNonPublicProperties: HasBindNonPublicPropertiesEnabled(invocation, semanticModel),
                errorsOnUnknownConfiguration: HasErrorOnUnknownConfigurationEnabled(invocation, semanticModel),
                isDataAnnotationsEnabled: isDataAnnotationsEnabled,
                bindLocation: sectionExpression.GetLocation(),
                requiresRuntimeSection: RequiresRuntimeSection(sectionExpression, semanticModel),
                validationInvocation: null,
                reportNestedValidation: reportNestedValidation);
            return true;
        }

        return false;
    }

    private static bool TryCreateBindlessValidationRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out OptionsRegistration registration)
    {
        registration = null!;

        if (!ReferenceEquals(GetOutermostFluentInvocation(invocation), invocation))
        {
            return false;
        }

        var isFactoryWithValidateOnStart = IsAddOptionsWithValidateOnStart(invocation, semanticModel);
        if (!isFactoryWithValidateOnStart)
        {
            var isDataAnnotations = IsOptionsBuilderValidateDataAnnotationsInvocation(invocation, semanticModel);
            var isValidateOnStart = IsOptionsBuilderValidateOnStartInvocation(invocation, semanticModel);
            var isValidate = IsOptionsBuilderValidateInvocation(invocation, semanticModel);
            if (!isDataAnnotations && !isValidateOnStart && !isValidate)
            {
                return false;
            }
        }

        if (!TryGetOptionsBuilderFactoryTarget(invocation, semanticModel, out var optionsType, out var optionsName) ||
            !OptionsBuilderInvocationTypeMatches(invocation, optionsType, semanticModel))
        {
            return false;
        }

        if (OptionsBuilderInstanceBinds(invocation, semanticModel))
        {
            return false;
        }

        var chain = InvocationChain.CreateFrom(invocation, semanticModel);
        var hasAddOptionsWithValidateOnStart = isFactoryWithValidateOnStart;
        if (!hasAddOptionsWithValidateOnStart)
        {
            hasAddOptionsWithValidateOnStart = HasAddOptionsWithValidateOnStartReceiver(invocation, semanticModel);
        }

        var hasValidateDataAnnotations = chain.MethodNames.Contains("ValidateDataAnnotations") ||
                                         HasSameBlockOptionsValidatorRegistration(
                                             invocation,
                                             optionsType,
                                             semanticModel);
        var hasValidateOnStart = chain.MethodNames.Contains("ValidateOnStart") || hasAddOptionsWithValidateOnStart;
        var hasValidation = chain.MethodNames.Any(IsValidationMethod) || hasAddOptionsWithValidateOnStart;
        if (!hasValidation)
        {
            return false;
        }

        if (!HasSameBlockMatchingConfigure(invocation, optionsType, optionsName, semanticModel))
        {
            return false;
        }

        registration = new OptionsRegistration(
            optionsType,
            sectionPath: string.Empty,
            sectionExpression: invocation,
            invocation,
            supportsValidationRules: true,
            sectionExpressionContainsFullPath: true,
            hasValidateDataAnnotations,
            hasValidateOnStart,
            hasValidation,
            bindsNonPublicProperties: MatchingConfigureBindsNonPublicProperties(
                invocation,
                optionsType,
                optionsName,
                semanticModel),
            errorsOnUnknownConfiguration: false,
            isDataAnnotationsEnabled: hasValidateDataAnnotations,
            bindLocation: invocation.GetLocation(),
            requiresRuntimeSection: false,
            validationInvocation: invocation,
            reportNestedValidation: false,
            hasBoundSection: false);
        return true;
    }

    private static bool HasSameBlockMatchingConfigure(
        InvocationExpressionSyntax validationInvocation,
        INamedTypeSymbol optionsType,
        string? optionsName,
        SemanticModel semanticModel)
    {
        foreach (var candidate in GetSameExecutableScopeInvocations(validationInvocation))
        {
            if (candidate == validationInvocation ||
                candidate.Expression is not MemberAccessExpressionSyntax memberAccess ||
                !string.Equals(memberAccess.Name.Identifier.ValueText, "Configure", StringComparison.Ordinal))
            {
                continue;
            }

            var symbol = semanticModel.GetSymbolInfo(candidate).Symbol as IMethodSymbol;
            if (symbol is null ||
                symbol.TypeArguments.Length != 1 ||
                symbol.TypeArguments[0] is not INamedTypeSymbol configureType ||
                !SymbolEqualityComparer.Default.Equals(configureType, optionsType) ||
                !IsOptionsConfigurationConfigureMethod(symbol))
            {
                continue;
            }

            foreach (var argument in candidate.ArgumentList.Arguments)
            {
                if (!TryGetConfigurationSectionPath(
                        argument.Expression,
                        semanticModel,
                        out _,
                        out _,
                        out _))
                {
                    continue;
                }

                if (TryGetConfigureOptionsName(candidate, argument, semanticModel, out var configureName) &&
                    OptionsNamesMatch(configureName, optionsName) &&
                    SameServiceCollectionOrUnproven(validationInvocation, candidate, semanticModel))
                {
                    return true;
                }

                break;
            }
        }

        return false;
    }

    private static bool SameServiceCollectionOrUnproven(
        InvocationExpressionSyntax left,
        InvocationExpressionSyntax right,
        SemanticModel semanticModel)
    {
        if (!TryGetServiceCollectionReceiver(left, semanticModel, out var leftCollection) ||
            !TryGetServiceCollectionReceiver(right, semanticModel, out var rightCollection))
        {
            return true;
        }

        return SymbolEqualityComparer.Default.Equals(leftCollection, rightCollection);
    }

    private static bool TryGetServiceCollectionReceiver(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out ISymbol collection)
    {
        collection = null!;
        var current = invocation;
        while (true)
        {
            if (IsAddOptionsWithValidateOnStart(current, semanticModel) ||
                TryGetAddOptionsFactoryTarget(current, semanticModel, out _, out _) ||
                IsOptionsConfigurationConfigureInvocation(current, semanticModel) ||
                IsOptionsValidatorServiceCollectionRegistration(current, semanticModel))
            {
                var memberAccess = (MemberAccessExpressionSyntax)current.Expression;
                var receiver = semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol;
                if (receiver is ILocalSymbol)
                {
                    collection = receiver;
                    return true;
                }

                if (receiver is IParameterSymbol)
                {
                    collection = receiver;
                    return true;
                }

                return false;
            }

            if (current.Expression is MemberAccessExpressionSyntax chainAccess &&
                chainAccess.Expression is InvocationExpressionSyntax receiverInvocation)
            {
                current = receiverInvocation;
                continue;
            }

            return false;
        }
    }

    private static bool IsOptionsConfigurationConfigureInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        return symbol is not null &&
               string.Equals(symbol.Name, "Configure", StringComparison.Ordinal) &&
               IsOptionsConfigurationConfigureMethod(symbol);
    }

    private static bool MatchingConfigureBindsNonPublicProperties(
        InvocationExpressionSyntax validationInvocation,
        INamedTypeSymbol optionsType,
        string? optionsName,
        SemanticModel semanticModel)
    {
        foreach (var candidate in GetSameExecutableScopeInvocations(validationInvocation))
        {
            if (candidate == validationInvocation ||
                candidate.Expression is not MemberAccessExpressionSyntax memberAccess ||
                !string.Equals(memberAccess.Name.Identifier.ValueText, "Configure", StringComparison.Ordinal))
            {
                continue;
            }

            var symbol = semanticModel.GetSymbolInfo(candidate).Symbol as IMethodSymbol;
            if (symbol is null ||
                symbol.TypeArguments.Length != 1 ||
                symbol.TypeArguments[0] is not INamedTypeSymbol configureType ||
                !SymbolEqualityComparer.Default.Equals(configureType, optionsType) ||
                !IsOptionsConfigurationConfigureMethod(symbol))
            {
                continue;
            }

            foreach (var argument in candidate.ArgumentList.Arguments)
            {
                if (!TryGetConfigurationSectionPath(
                        argument.Expression,
                        semanticModel,
                        out _,
                        out _,
                        out _))
                {
                    continue;
                }

                if (TryGetConfigureOptionsName(candidate, argument, semanticModel, out var configureName) &&
                    OptionsNamesMatch(configureName, optionsName) &&
                    HasBindNonPublicPropertiesEnabled(candidate, semanticModel) &&
                    SameServiceCollectionOrUnproven(validationInvocation, candidate, semanticModel))
                {
                    return true;
                }

                break;
            }
        }

        return false;
    }

    private static bool OptionsBuilderInvocationTypeMatches(
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol optionsType,
        SemanticModel semanticModel)
    {
        var invocationType = (INamedTypeSymbol)semanticModel.GetTypeInfo(invocation).Type!;
        return SymbolEqualityComparer.Default.Equals(invocationType.TypeArguments[0], optionsType);
    }

    private static bool RequiresRuntimeSection(
        ExpressionSyntax sectionExpression,
        SemanticModel semanticModel)
    {
        var invocation = sectionExpression.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        return invocation is not null &&
               semanticModel.GetOperation(invocation) is IInvocationOperation operation &&
               TryNormalizeDirectConfigurationInvocation(operation, out var directInvocation) &&
               directInvocation.Kind == DirectConfigurationApiKind.GetRequiredSection;
    }

    /// <summary>
    /// Recognizes an options binding whose section argument is a <c>GetSection</c>/
    /// <c>GetRequiredSection</c> call chained off a stored <c>IConfigurationSection</c> local with
    /// a statically visible origin, and builds a CFG001-only registration for it. The shared
    /// registration factories intentionally stay quiet for stored sections because every options
    /// rule consumes the resolved path; this fallback feeds only the missing-section check, so
    /// validation, unknown-key, strict-binding, and conversion analysis keep their existing
    /// boundary for this shape.
    /// </summary>
    private static bool TryCreateStoredSectionOriginRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out OptionsRegistration registration)
    {
        registration = null!;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            invocation.ArgumentList.Arguments.Count == 0)
        {
            return false;
        }

        var methodName = memberAccess.Name.Identifier.ValueText;
        INamedTypeSymbol? optionsType = null;
        var candidateSectionExpressions = ImmutableArray<ExpressionSyntax>.Empty;

        if (string.Equals(methodName, "Bind", StringComparison.Ordinal) &&
            semanticModel.GetTypeInfo(memberAccess.Expression).Type is INamedTypeSymbol
            {
                Name: "OptionsBuilder",
                TypeArguments.Length: 1,
            } receiverType &&
            string.Equals(
                receiverType.ContainingNamespace.ToDisplayString(),
                "Microsoft.Extensions.Options",
                StringComparison.Ordinal) &&
            receiverType.TypeArguments[0] is INamedTypeSymbol bindOptionsType &&
            IsOptionsBuilderConfigurationMethod(invocation, semanticModel, methodName) &&
            GetInvocationArgumentExpression(
                invocation,
                semanticModel,
                "config") is { } configurationExpression)
        {
            optionsType = bindOptionsType;
            candidateSectionExpressions =
                ImmutableArray.Create(configurationExpression);
        }
        else if (string.Equals(methodName, "Configure", StringComparison.Ordinal) &&
                 semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol
                 {
                     TypeArguments.Length: 1,
                 } symbol &&
                 symbol.TypeArguments[0] is INamedTypeSymbol configureOptionsType &&
                 IsOptionsConfigurationConfigureMethod(symbol))
        {
            optionsType = configureOptionsType;
            candidateSectionExpressions =
                invocation.ArgumentList.Arguments
                    .Select(argument => argument.Expression)
                    .ToImmutableArray();
        }

        if (optionsType is null)
        {
            return false;
        }

        foreach (var candidateExpression in candidateSectionExpressions)
        {
            if (!TryGetConfigurationSectionPath(
                    candidateExpression,
                    semanticModel,
                    out var sectionPath,
                    out var sectionExpression,
                    out var sectionExpressionContainsFullPath,
                    resolveStoredSectionOrigins: true))
            {
                continue;
            }

            registration = new OptionsRegistration(
                optionsType,
                sectionPath,
                sectionExpression,
                invocation,
                supportsValidationRules: false,
                sectionExpressionContainsFullPath: sectionExpressionContainsFullPath,
                hasValidateDataAnnotations: false,
                hasValidateOnStart: false,
                hasValidation: false,
                bindsNonPublicProperties: false,
                errorsOnUnknownConfiguration: false,
                isDataAnnotationsEnabled: false,
                bindLocation: sectionExpression.GetLocation(),
                requiresRuntimeSection: RequiresRuntimeSection(sectionExpression, semanticModel));
            return true;
        }

        return false;
    }

    private static bool TryGetConfigureOptionsName(
        InvocationExpressionSyntax configureInvocation,
        ArgumentSyntax sectionArgument,
        SemanticModel semanticModel,
        out string? optionsName)
    {
        optionsName = null;
        foreach (var argument in configureInvocation.ArgumentList.Arguments)
        {
            if (argument.NameColon is not null &&
                string.Equals(argument.NameColon.Name.Identifier.ValueText, "name", StringComparison.Ordinal))
            {
                return TryGetConstantOptionsName(
                    argument.Expression,
                    semanticModel,
                    out optionsName,
                    nullMeansConfigureAll: true);
            }
        }

        var sectionArgumentIndex = configureInvocation.ArgumentList.Arguments.IndexOf(sectionArgument);
        if (sectionArgumentIndex <= 0)
        {
            return true;
        }

        for (var index = 0; index < sectionArgumentIndex; index++)
        {
            var argument = configureInvocation.ArgumentList.Arguments[index];
            if (argument.NameColon is not null)
            {
                continue;
            }

            return TryGetConstantOptionsName(
                argument.Expression,
                semanticModel,
                out optionsName,
                nullMeansConfigureAll: true);
        }

        return true;
    }

    private static bool HasSameBlockDataAnnotationsValidation(
        InvocationExpressionSyntax configureInvocation,
        INamedTypeSymbol optionsType,
        string? optionsName,
        SemanticModel semanticModel)
    {
        foreach (var invocation in GetSameExecutableScopeInvocations(configureInvocation))
        {
            if (invocation == configureInvocation ||
                !IsOptionsBuilderValidateDataAnnotationsInvocation(invocation, semanticModel))
            {
                continue;
            }

            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                TryGetOptionsBuilderFactoryTarget(
                    memberAccess.Expression,
                    semanticModel,
                    out var validationOptionsType,
                    out var validationOptionsName) &&
                SymbolEqualityComparer.Default.Equals(validationOptionsType, optionsType) &&
                OptionsNamesMatch(optionsName, validationOptionsName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSameBlockBindlessDataAnnotationsValidation(
        InvocationExpressionSyntax configureInvocation,
        INamedTypeSymbol optionsType,
        string? optionsName,
        SemanticModel semanticModel)
    {
        foreach (var invocation in GetSameExecutableScopeInvocations(configureInvocation))
        {
            if (invocation == configureInvocation ||
                !TryGetMatchingOptionsBuilderValidation(
                    invocation,
                    optionsType,
                    optionsName,
                    semanticModel,
                    out var isDataAnnotations,
                    out _,
                    out _) ||
                !isDataAnnotations ||
                OptionsBuilderInstanceBinds(invocation, semanticModel) ||
                !SameServiceCollectionOrUnproven(configureInvocation, invocation, semanticModel))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool TryGetMatchingOptionsBuilderValidation(
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol optionsType,
        string? optionsName,
        SemanticModel semanticModel,
        out bool isDataAnnotations,
        out bool isValidateOnStart,
        out bool isFactoryWithValidateOnStart)
    {
        isDataAnnotations = false;
        isValidateOnStart = false;
        isFactoryWithValidateOnStart = false;

        if (IsAddOptionsWithValidateOnStart(invocation, semanticModel) &&
            TryGetAddOptionsFactoryTarget(invocation, semanticModel, out var factoryType, out var factoryName) &&
            SymbolEqualityComparer.Default.Equals(factoryType, optionsType) &&
            OptionsNamesMatch(optionsName, factoryName))
        {
            isFactoryWithValidateOnStart = true;
            return true;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        isDataAnnotations = IsOptionsBuilderValidateDataAnnotationsInvocation(invocation, semanticModel);
        isValidateOnStart = IsOptionsBuilderValidateOnStartInvocation(invocation, semanticModel);
        var isValidate = IsOptionsBuilderValidateInvocation(invocation, semanticModel);
        if (!isDataAnnotations && !isValidateOnStart && !isValidate)
        {
            return false;
        }

        return TryGetOptionsBuilderFactoryTarget(
                   memberAccess.Expression,
                   semanticModel,
                   out var validationOptionsType,
                   out var validationOptionsName) &&
               SymbolEqualityComparer.Default.Equals(validationOptionsType, optionsType) &&
               OptionsNamesMatch(optionsName, validationOptionsName);
    }

    private static bool IsOptionsBuilderValidateOnStartInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var original = symbol?.ReducedFrom ?? symbol;
        return original is not null &&
               string.Equals(original.Name, "ValidateOnStart", StringComparison.Ordinal) &&
               string.Equals(
                   original.ContainingType.ToDisplayString(),
                   "Microsoft.Extensions.DependencyInjection.OptionsBuilderExtensions",
                   StringComparison.Ordinal);
    }

    private static bool IsOptionsBuilderValidateInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var original = symbol?.ReducedFrom ?? symbol;
        return original is not null &&
               string.Equals(original.Name, "Validate", StringComparison.Ordinal) &&
               string.Equals(original.ContainingType.Name, "OptionsBuilder", StringComparison.Ordinal) &&
               string.Equals(
                   original.ContainingType.ContainingNamespace.ToDisplayString(),
                   "Microsoft.Extensions.Options",
                   StringComparison.Ordinal);
    }

    private static bool OptionsBuilderInstanceBinds(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (FluentChainContainsBind(invocation, semanticModel))
        {
            return true;
        }

        if (!TryGetTrackedOptionsBuilderSymbol(invocation, semanticModel, out var builderSymbol))
        {
            return false;
        }

        if (builderSymbol is ILocalSymbol localSymbol)
        {
            foreach (var syntax in localSymbol.DeclaringSyntaxReferences.Select(static reference => reference.GetSyntax()))
            {
                if (syntax is VariableDeclaratorSyntax { Initializer.Value: InvocationExpressionSyntax initializer } &&
                    FluentChainContainsBind(initializer, semanticModel))
                {
                    return true;
                }
            }
        }

        foreach (var candidate in GetSameExecutableScopeInvocations(invocation))
        {
            if (!IsInvocationOnSymbol(candidate, builderSymbol, semanticModel))
            {
                continue;
            }

            var bindAccess = (MemberAccessExpressionSyntax)candidate.Expression;
            if (IsOptionsBuilderBindMethodName(bindAccess.Name.Identifier.ValueText) &&
                IsOptionsBuilderConfigurationMethod(
                    candidate,
                    semanticModel,
                    bindAccess.Name.Identifier.ValueText))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FluentChainContainsBind(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        var current = GetOutermostFluentInvocation(invocation);
        while (true)
        {
            if (current.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            var methodName = memberAccess.Name.Identifier.ValueText;
            if (IsOptionsBuilderBindMethodName(methodName) &&
                IsOptionsBuilderConfigurationMethod(current, semanticModel, methodName))
            {
                return true;
            }

            if (memberAccess.Expression is not InvocationExpressionSyntax receiver)
            {
                return false;
            }

            current = receiver;
        }
    }

    private static bool IsOptionsBuilderBindMethodName(string methodName)
    {
        return string.Equals(methodName, "Bind", StringComparison.Ordinal) ||
               string.Equals(methodName, "BindConfiguration", StringComparison.Ordinal);
    }

    private static InvocationExpressionSyntax GetOutermostFluentInvocation(InvocationExpressionSyntax invocation)
    {
        var current = invocation;
        while (current.Parent is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Expression == current &&
               memberAccess.Parent is InvocationExpressionSyntax next)
        {
            current = next;
        }

        return current;
    }

    private static bool TryGetTrackedOptionsBuilderSymbol(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out ISymbol builderSymbol)
    {
        builderSymbol = null!;
        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        var receiver = semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol;
        if (receiver is ILocalSymbol)
        {
            builderSymbol = receiver;
            return true;
        }

        if (receiver is IParameterSymbol)
        {
            builderSymbol = receiver;
            return true;
        }

        if (invocation.Parent is EqualsValueClauseSyntax equalsValue &&
            equalsValue.Parent is VariableDeclaratorSyntax declarator)
        {
            if (semanticModel.GetDeclaredSymbol(declarator) is ILocalSymbol declared)
            {
                builderSymbol = declared;
                return true;
            }
        }

        return false;
    }

    private static bool IsInvocationOnSymbol(
        InvocationExpressionSyntax invocation,
        ISymbol builderSymbol,
        SemanticModel semanticModel)
    {
        return invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
               SymbolEqualityComparer.Default.Equals(
                   semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol,
                   builderSymbol);
    }

    private static IEnumerable<InvocationExpressionSyntax> GetSameExecutableScopeInvocations(
        InvocationExpressionSyntax configureInvocation)
    {
        var block = configureInvocation.FirstAncestorOrSelf<BlockSyntax>();
        if (block is not null)
        {
            foreach (var statement in block.Statements)
            {
                foreach (var invocation in GetTopLevelStatementInvocations(statement))
                {
                    yield return invocation;
                }
            }

            yield break;
        }

        var globalStatement = configureInvocation.FirstAncestorOrSelf<GlobalStatementSyntax>();
        if (globalStatement?.Parent is CompilationUnitSyntax compilationUnit)
        {
            foreach (var statement in compilationUnit.Members
                         .OfType<GlobalStatementSyntax>()
                         .Select(static member => member.Statement))
            {
                foreach (var invocation in GetTopLevelStatementInvocations(statement))
                {
                    yield return invocation;
                }
            }

            yield break;
        }

        var expressionBody = configureInvocation.FirstAncestorOrSelf<ArrowExpressionClauseSyntax>()?.Expression;
        if (expressionBody is not null)
        {
            foreach (var invocation in expressionBody
                         .DescendantNodesAndSelf(ExecutionScope.ShouldDescend)
                         .OfType<InvocationExpressionSyntax>())
            {
                yield return invocation;
            }

            yield break;
        }

        yield return configureInvocation;
    }

    private static IEnumerable<InvocationExpressionSyntax> GetTopLevelStatementInvocations(StatementSyntax statement)
    {
        SyntaxNode? scanRoot = statement switch
        {
            ExpressionStatementSyntax expressionStatement => expressionStatement.Expression,
            LocalDeclarationStatementSyntax => statement,
            ReturnStatementSyntax { Expression: { } expression } => expression,
            _ => null
        };
        if (scanRoot is null)
        {
            yield break;
        }

        foreach (var invocation in scanRoot
                     .DescendantNodesAndSelf(ExecutionScope.ShouldDescend)
                     .OfType<InvocationExpressionSyntax>())
        {
            yield return invocation;
        }
    }

    private static bool TryGetOptionsBuilderFactoryTarget(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        out INamedTypeSymbol optionsType,
        out string? optionsName)
    {
        optionsType = null!;
        optionsName = null;
        var visitedLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        while (true)
        {
            if (expression is InvocationExpressionSyntax invocation)
            {
                if (TryGetAddOptionsFactoryTarget(invocation, semanticModel, out optionsType, out optionsName))
                {
                    return true;
                }

                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    expression = memberAccess.Expression;
                    continue;
                }

                return false;
            }

            if (expression is IdentifierNameSyntax identifier)
            {
                var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
                if (symbol is ILocalSymbol localSymbol)
                {
                    if (!visitedLocals.Add(localSymbol))
                    {
                        return false;
                    }

                    var initializer = localSymbol.DeclaringSyntaxReferences
                        .Select(static reference => reference.GetSyntax())
                        .OfType<VariableDeclaratorSyntax>()
                        .Select(static declaration => declaration.Initializer?.Value)
                        .FirstOrDefault(static value => value is not null);
                    if (initializer is null)
                    {
                        return false;
                    }

                    expression = initializer;
                    continue;
                }

                if (symbol is IParameterSymbol parameter &&
                    parameter.Type is INamedTypeSymbol
                    {
                        Name: "OptionsBuilder",
                        TypeArguments.Length: 1,
                    } parameterType &&
                    string.Equals(
                        parameterType.ContainingNamespace.ToDisplayString(),
                        "Microsoft.Extensions.Options",
                        StringComparison.Ordinal) &&
                    parameterType.TypeArguments[0] is INamedTypeSymbol parameterOptionsType)
                {
                    optionsType = parameterOptionsType;
                    return true;
                }
            }

            return false;
        }
    }

    private static bool TryGetAddOptionsFactoryTarget(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out INamedTypeSymbol optionsType,
        out string? optionsName)
    {
        optionsType = null!;
        optionsName = null;

        var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var original = symbol?.ReducedFrom ?? symbol;
        if (original is null ||
            !IsOptionsBuilderFactoryMethod(original) ||
            symbol?.TypeArguments.Length != 1 ||
            symbol.TypeArguments[0] is not INamedTypeSymbol candidateOptionsType)
        {
            return false;
        }

        if (invocation.ArgumentList.Arguments.Count == 0)
        {
            optionsType = candidateOptionsType;
            return true;
        }

        if (!TryGetConstantOptionsName(invocation.ArgumentList.Arguments[0].Expression, semanticModel, out optionsName))
        {
            return false;
        }

        optionsType = candidateOptionsType;
        return true;
    }

    private static bool IsOptionsBuilderFactoryMethod(IMethodSymbol method)
    {
        return (string.Equals(method.Name, "AddOptions", StringComparison.Ordinal) ||
                string.Equals(method.Name, "AddOptionsWithValidateOnStart", StringComparison.Ordinal)) &&
               string.Equals(
                   method.ContainingType.ToDisplayString(),
                   "Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions",
                   StringComparison.Ordinal);
    }

    private static bool IsOptionsBuilderValidateDataAnnotationsInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var original = symbol?.ReducedFrom ?? symbol;
        return original is not null &&
               string.Equals(original.Name, "ValidateDataAnnotations", StringComparison.Ordinal) &&
               string.Equals(
                   original.ContainingType.ToDisplayString(),
                   "Microsoft.Extensions.DependencyInjection.OptionsBuilderDataAnnotationsExtensions",
                   StringComparison.Ordinal);
    }

    private static bool TryGetConstantOptionsName(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        out string? optionsName,
        bool nullMeansConfigureAll = false)
    {
        var constant = semanticModel.GetConstantValue(expression);
        if (constant.HasValue)
        {
            if (constant.Value is string value)
            {
                optionsName = value;
                return true;
            }

            if (constant.Value is null)
            {
                optionsName = nullMeansConfigureAll ? ConfigureAllOptionsName : "";
                return true;
            }
        }

        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        if ((symbol is IFieldSymbol or IPropertySymbol) &&
            string.Equals(symbol.Name, "DefaultName", StringComparison.Ordinal) &&
            string.Equals(symbol.ContainingType.ToDisplayString(), "Microsoft.Extensions.Options.Options", StringComparison.Ordinal))
        {
            optionsName = "";
            return true;
        }

        if (symbol is IFieldSymbol stringField &&
            string.Equals(stringField.Name, "Empty", StringComparison.Ordinal) &&
            stringField.ContainingType.SpecialType == SpecialType.System_String)
        {
            optionsName = "";
            return true;
        }

        optionsName = null;
        return false;
    }

    private static bool HasSameBlockOptionsValidatorRegistration(
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol optionsType,
        SemanticModel semanticModel)
    {
        foreach (var candidate in GetSameExecutableScopeInvocations(invocation))
        {
            if (!IsUnconditionallyEvaluatedCandidate(candidate) ||
                !OptionsValidatorRegistration.TryGetValidatedOptionsType(
                    candidate,
                    semanticModel,
                    out var validatedType) ||
                !SymbolEqualityComparer.Default.Equals(validatedType, optionsType) ||
                !SameServiceCollectionOrUnproven(invocation, candidate, semanticModel))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsUnconditionallyEvaluatedCandidate(InvocationExpressionSyntax candidate)
    {
        SyntaxNode? boundary = candidate.FirstAncestorOrSelf<StatementSyntax>() ??
                               (SyntaxNode?)candidate.FirstAncestorOrSelf<ArrowExpressionClauseSyntax>() ??
                               candidate.FirstAncestorOrSelf<EqualsValueClauseSyntax>();
        return boundary is not null &&
               ExecutionScope.IsUnconditionallyEvaluatedWithin(candidate, boundary);
    }

    private static bool IsOptionsValidatorServiceCollectionRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        return semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol method &&
               OptionsValidatorRegistration.IsServiceCollectionRegistration(method);
    }

    private static bool OptionsNamesMatch(string? configureOptionsName, string? validationOptionsName)
    {
        if (string.Equals(configureOptionsName, ConfigureAllOptionsName, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(
            NormalizeOptionsName(validationOptionsName),
            NormalizeOptionsName(configureOptionsName),
            StringComparison.Ordinal);
    }

    private static string NormalizeOptionsName(string? optionsName)
    {
        return optionsName ?? "";
    }

}
