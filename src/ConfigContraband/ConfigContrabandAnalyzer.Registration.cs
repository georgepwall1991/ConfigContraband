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

        return TryCreateConfigureRegistration(invocation, semanticModel, out registration);
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
            if (semanticModel.GetOperation(invocation) is not IInvocationOperation operation)
            {
                return false;
            }

            var sectionArgument = operation.Arguments.FirstOrDefault(argument =>
                string.Equals(argument.Parameter?.Name, "configSectionPath", StringComparison.Ordinal));
            if (sectionArgument?.Value.Syntax is not ExpressionSyntax argumentExpression)
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
            if (!TryGetConfigurationSectionPath(
                    invocation.ArgumentList.Arguments[0].Expression,
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
        var hasValidateOnStart = chain.MethodNames.Contains("ValidateOnStart") || hasAddOptionsWithValidateOnStart;
        var hasValidation = chain.MethodNames.Any(IsValidationMethod) || hasAddOptionsWithValidateOnStart;
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
            chain.MethodNames.Contains("ValidateDataAnnotations"),
            hasValidateOnStart,
            hasValidation,
            bindsNonPublicProperties,
            errorsOnUnknownConfiguration,
            chain.MethodNames.Contains("ValidateDataAnnotations"),
            sectionExpression.GetLocation(),
            RequiresRuntimeSection(sectionExpression, semanticModel));
        return true;
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
            var isDataAnnotationsEnabled = hasKnownOptionsName &&
                HasSameBlockDataAnnotationsValidation(invocation, optionsType, optionsName, semanticModel);

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
                requiresRuntimeSection: RequiresRuntimeSection(sectionExpression, semanticModel));
            return true;
        }

        return false;
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
        ImmutableArray<ExpressionSyntax> candidateSectionExpressions = [];

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
            IsOptionsBuilderConfigurationMethod(invocation, semanticModel, methodName))
        {
            optionsType = bindOptionsType;
            candidateSectionExpressions = [invocation.ArgumentList.Arguments[0].Expression];
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
                [.. invocation.ArgumentList.Arguments.Select(argument => argument.Expression)];
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

            if (expression is IdentifierNameSyntax identifier &&
                semanticModel.GetSymbolInfo(identifier).Symbol is ILocalSymbol localSymbol)
            {
                if (!visitedLocals.Add(localSymbol))
                {
                    return false;
                }

                var declaration = localSymbol.DeclaringSyntaxReferences
                    .Select(static reference => reference.GetSyntax())
                    .OfType<VariableDeclaratorSyntax>()
                    .FirstOrDefault();
                if (declaration?.Initializer?.Value is null)
                {
                    return false;
                }

                expression = declaration.Initializer.Value;
                continue;
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
