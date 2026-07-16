using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ConfigContraband;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConfigContrabandAnalyzer : DiagnosticAnalyzer
{
    internal const string SuggestedSectionPropertyName = "SuggestedSection";
    internal const string SuggestedSectionReplacementPropertyName = "SuggestedSectionReplacement";
    internal const string HasValidateOnStartPropertyName = "HasValidateOnStart";
    internal const string RecursiveAttributePropertyName = "RecursiveAttribute";

    private const string ConfigureAllOptionsName = "\0configure-all";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        DiagnosticDescriptors.MissingConfigurationSection,
        DiagnosticDescriptors.MissingRequiredConfigurationKey,
        DiagnosticDescriptors.ValidationNotOnStart,
        DiagnosticDescriptors.DataAnnotationsNotEnabled,
        DiagnosticDescriptors.NestedValidationNotRecursive,
        DiagnosticDescriptors.UnknownConfigurationKey,
        DiagnosticDescriptors.UnknownConfigurationKeyWillThrow,
        DiagnosticDescriptors.ConfigurationValueTypeMismatch,
        DiagnosticDescriptors.ConfigurationKeyNotFound);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var configuration = ConfigurationSnapshot.Create(
                compilationContext.Options.AdditionalFiles,
                additionalFile => IsDiagnosticSuppressed(
                    compilationContext.Options,
                    additionalFile,
                    DiagnosticIds.UnknownConfigurationKeyWillThrow),
                compilationContext.CancellationToken);
            var providerSemantics = GetConfigurationProviderSemantics(compilationContext.Compilation);
            var nestedValidationReported = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            var unknownKeysReported = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    var invocation = (InvocationExpressionSyntax)syntaxContext.Node;
                    if (!TryCreateRegistration(invocation, syntaxContext.SemanticModel, out var registration))
                    {
                        if (configuration.HasFiles)
                        {
                            AnalyzeDirectConfigurationRead(syntaxContext, configuration, providerSemantics);
                        }

                        return;
                    }

                    var compilation = syntaxContext.SemanticModel.Compilation;
                    AnalyzeRegistrationChain(syntaxContext.ReportDiagnostic, registration, compilation);
                    if (registration.SupportsValidationRules)
                    {
                        AnalyzeOptionType(syntaxContext.ReportDiagnostic, registration, nestedValidationReported, compilation);
                    }

                    if (configuration.HasFiles)
                    {
                        var strictUnknownConfigurationKeySuppressed = IsDiagnosticSuppressed(
                            syntaxContext.Options,
                            compilation,
                            invocation.SyntaxTree,
                            DiagnosticIds.UnknownConfigurationKeyWillThrow);
                        AnalyzeConfigurationSection(
                            syntaxContext.ReportDiagnostic,
                            registration,
                            configuration,
                            providerSemantics);
                        AnalyzeUnknownKeys(
                                syntaxContext.ReportDiagnostic,
                                registration,
                                configuration,
                                providerSemantics,
                                unknownKeysReported,
                                compilation,
                                strictUnknownConfigurationKeySuppressed);
                    }
                },
                SyntaxKind.InvocationExpression);
        });
    }

    private static bool IsDiagnosticSuppressed(
        AnalyzerOptions options,
        Compilation compilation,
        SyntaxTree syntaxTree,
        string diagnosticId)
    {
        if (compilation.Options.SpecificDiagnosticOptions.TryGetValue(diagnosticId, out var reportDiagnostic) &&
            reportDiagnostic == ReportDiagnostic.Suppress)
        {
            return true;
        }

        return HasSeverityNone(
                   options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree),
                   diagnosticId) ||
               HasSeverityNone(options.AnalyzerConfigOptionsProvider.GlobalOptions, diagnosticId);
    }

    private static bool IsDiagnosticSuppressed(
        AnalyzerOptions options,
        AdditionalText additionalFile,
        string diagnosticId)
    {
        return HasSeverityNone(options.AnalyzerConfigOptionsProvider.GetOptions(additionalFile), diagnosticId) ||
               HasSeverityNone(options.AnalyzerConfigOptionsProvider.GlobalOptions, diagnosticId);
    }

    private static bool HasSeverityNone(AnalyzerConfigOptions options, string diagnosticId)
    {
        return HasSeverityNoneKey(options, "dotnet_diagnostic." + diagnosticId + ".severity") ||
               HasSeverityNoneKey(options, "dotnet_diagnostic." + diagnosticId.ToLowerInvariant() + ".severity");
    }

    private static bool HasSeverityNoneKey(AnalyzerConfigOptions options, string key)
    {
        return options.TryGetValue(key, out var severity) &&
               string.Equals(severity, "none", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeneratedSyntaxTree(SyntaxTree syntaxTree, SyntaxNode root)
    {
        var fileName = System.IO.Path.GetFileName(syntaxTree.FilePath);
        if (fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var trivia in root.GetLeadingTrivia())
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                var text = trivia.ToString();
                if (text.IndexOf("<auto-generated", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            if (!trivia.IsKind(SyntaxKind.WhitespaceTrivia) &&
                !trivia.IsKind(SyntaxKind.EndOfLineTrivia) &&
                !trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) &&
                !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                break;
            }
        }

        return false;
    }

    private static void AnalyzeRegistrationChain(
        Action<Diagnostic> reportDiagnostic,
        OptionsRegistration registration,
        Compilation compilation)
    {
        if (!registration.SupportsValidationRules)
        {
            return;
        }

        if (registration.HasValidation && !registration.HasValidateOnStart)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ValidationNotOnStart,
                registration.OutermostInvocation.GetLocation(),
                registration.OptionsType.Name));
        }

        var metadata = OptionsTypeMetadata.Create(
            registration.OptionsType,
            registration.BindsNonPublicProperties,
            compilation);
        if (metadata.HasAnyDataAnnotations() && !registration.HasValidateDataAnnotations)
        {
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(HasValidateOnStartPropertyName, registration.HasValidateOnStart ? "true" : "false");

            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.DataAnnotationsNotEnabled,
                registration.OutermostInvocation.GetLocation(),
                properties,
                registration.OptionsType.Name));
        }
    }

    private static void AnalyzeOptionType(
        Action<Diagnostic> reportDiagnostic,
        OptionsRegistration registration,
        ConcurrentDictionary<string, byte> nestedValidationReported,
        Compilation compilation)
    {
        var metadata = OptionsTypeMetadata.Create(
            registration.OptionsType,
            registration.BindsNonPublicProperties,
            compilation);
        foreach (var candidate in metadata.GetNestedValidationCandidates())
        {
            var reportKey = candidate.Property.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                            "|" +
                            candidate.Property.Symbol.Locations.FirstOrDefault()?.GetLineSpan().Span.Start;
            if (!nestedValidationReported.TryAdd(reportKey, 0))
            {
                continue;
            }

            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(RecursiveAttributePropertyName, candidate.AttributeName);

            var propertyLocation = candidate.Property.Symbol.Locations.FirstOrDefault();

            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.NestedValidationNotRecursive,
                registration.OutermostInvocation.GetLocation(),
                propertyLocation is null ? null : new[] { propertyLocation },
                properties,
                candidate.Property.Symbol.ContainingType.Name,
                candidate.Property.Symbol.Name));
        }
    }

    private static void AnalyzeConfigurationSection(
        Action<Diagnostic> reportDiagnostic,
        OptionsRegistration registration,
        ConfigurationSnapshot configuration,
        ConfigurationProviderSemantics providerSemantics)
    {
        if (configuration.TryFindSection(registration.SectionPath, out _) &&
            !registration.RequiresRuntimeSection)
        {
            return;
        }

        if (configuration.GetSectionExistence(registration.SectionPath, providerSemantics) !=
            ConfigurationSectionExistence.Missing)
        {
            return;
        }

        ReportMissingSection(
            reportDiagnostic,
            DiagnosticDescriptors.MissingConfigurationSection,
            registration.SectionPath,
            registration.SectionExpression,
            registration.SectionExpressionContainsFullPath,
            configuration);
    }

    private static void ReportMissingSection(
        Action<Diagnostic> reportDiagnostic,
        DiagnosticDescriptor descriptor,
        string sectionPath,
        ExpressionSyntax sectionExpression,
        bool sectionExpressionContainsFullPath,
        ConfigurationSnapshot configuration,
        bool requireSuggestion = false)
    {
        var sectionLeaf = sectionPath.Split(':').Last();
        var suggestion = configuration.TryFindSection(sectionPath, out _)
            ? null
            : FindClosest(
                sectionLeaf,
                configuration.GetSiblingSectionNames(sectionPath)
                    .Where(candidate => !string.Equals(candidate, sectionLeaf, StringComparison.OrdinalIgnoreCase))
                    .ToImmutableArray());
        if (suggestion is null && requireSuggestion)
        {
            return;
        }

        var suggestedSectionPath = suggestion is null ? null : ReplaceSectionLeaf(sectionPath, suggestion);
        var suffix = suggestedSectionPath is null ? "." : $". Did you mean \"{suggestedSectionPath}\"?";
        var properties = ImmutableDictionary<string, string?>.Empty;
        if (suggestion is not null && suggestedSectionPath is not null)
        {
            // The offered fix overwrites the whole anchored section-expression literal, so
            // the replacement text must reproduce that literal's own value with only its
            // leaf corrected. When the section literal is the full path we can substitute
            // the full corrected path; when it is a chained non-root literal we must
            // preserve any leading segments the literal itself carries (for example the
            // "Sub:" in `.GetSection("Features").GetSection("Sub:Strpie")`), otherwise the
            // fix would silently drop them and produce a still-broken binding.
            var suggestedReplacement = sectionExpressionContainsFullPath
                ? suggestedSectionPath
                : TryBuildChainedLeafReplacement(sectionExpression, suggestion);
            if (suggestedReplacement is not null)
            {
                properties = properties
                    .Add(SuggestedSectionPropertyName, suggestedSectionPath)
                    .Add(SuggestedSectionReplacementPropertyName, suggestedReplacement);
            }
        }

        reportDiagnostic(Diagnostic.Create(
            descriptor,
            sectionExpression.GetLocation(),
            properties,
            sectionPath,
            suffix));
    }

    private static string ReplaceSectionLeaf(string sectionPath, string replacement)
    {
        var separatorIndex = sectionPath.LastIndexOf(':');
        return separatorIndex < 0
            ? replacement
            : sectionPath.Substring(0, separatorIndex + 1) + replacement;
    }

    /// <summary>
    /// Builds the replacement text for a chained non-root section literal, preserving any
    /// leading colon-delimited segments the literal itself carries and correcting only its
    /// leaf. Returns <c>null</c> when the anchored expression is not a plain string literal
    /// whose leading segments can be reproduced safely, so the caller suppresses the fix
    /// rather than risk dropping a segment.
    /// </summary>
    private static string? TryBuildChainedLeafReplacement(ExpressionSyntax sectionExpression, string suggestion)
    {
        if (sectionExpression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return ReplaceSectionLeaf(literal.Token.ValueText, suggestion);
        }

        return null;
    }

    private enum DirectConfigurationApiKind
    {
        GetRequiredSection,
        GetConnectionString,
        Get,
        Bind,
    }

    private readonly struct DirectConfigurationInvocation
    {
        public DirectConfigurationInvocation(
            DirectConfigurationApiKind kind,
            IMethodSymbol originalMethod,
            ExpressionSyntax receiver,
            ExpressionSyntax? keyExpression,
            InvocationExpressionSyntax syntax)
        {
            Kind = kind;
            OriginalMethod = originalMethod;
            Receiver = receiver;
            KeyExpression = keyExpression;
            Syntax = syntax;
        }

        public DirectConfigurationApiKind Kind { get; }
        public IMethodSymbol OriginalMethod { get; }
        public ExpressionSyntax Receiver { get; }
        public ExpressionSyntax? KeyExpression { get; }
        public InvocationExpressionSyntax Syntax { get; }
    }

    private static void AnalyzeDirectConfigurationRead(
        SyntaxNodeAnalysisContext syntaxContext,
        ConfigurationSnapshot configuration,
        ConfigurationProviderSemantics providerSemantics)
    {
        var invocation = (InvocationExpressionSyntax)syntaxContext.Node;
        if (syntaxContext.SemanticModel.GetOperation(invocation, syntaxContext.CancellationToken) is not IInvocationOperation operation ||
            !TryNormalizeDirectConfigurationInvocation(operation, out var directInvocation))
        {
            return;
        }

        switch (directInvocation.Kind)
        {
            case DirectConfigurationApiKind.GetRequiredSection:
                AnalyzeStandaloneRequiredSectionRead(
                    syntaxContext,
                    directInvocation,
                    configuration,
                    providerSemantics);
                break;
            case DirectConfigurationApiKind.GetConnectionString:
                AnalyzeConnectionStringRead(
                    syntaxContext,
                    directInvocation,
                    configuration,
                    providerSemantics);
                break;
            case DirectConfigurationApiKind.Get:
            case DirectConfigurationApiKind.Bind:
                AnalyzeBoundSectionRead(
                    syntaxContext,
                    directInvocation,
                    configuration,
                    providerSemantics);
                break;
        }
    }

    private static bool TryNormalizeDirectConfigurationInvocation(
        IInvocationOperation operation,
        out DirectConfigurationInvocation invocation)
    {
        invocation = default;
        if (operation.Syntax is not InvocationExpressionSyntax syntax)
        {
            return false;
        }

        var originalMethod = operation.TargetMethod.ReducedFrom ?? operation.TargetMethod;
        var containingType = originalMethod.ContainingType?.ToDisplayString();
        DirectConfigurationApiKind kind;
        string? keyParameterName = null;

        if (string.Equals(containingType, "Microsoft.Extensions.Configuration.ConfigurationExtensions", StringComparison.Ordinal))
        {
            if (string.Equals(originalMethod.Name, "GetRequiredSection", StringComparison.Ordinal))
            {
                kind = DirectConfigurationApiKind.GetRequiredSection;
                keyParameterName = "key";
            }
            else if (string.Equals(originalMethod.Name, "GetConnectionString", StringComparison.Ordinal))
            {
                kind = DirectConfigurationApiKind.GetConnectionString;
                keyParameterName = "name";
            }
            else
            {
                return false;
            }
        }
        else if (string.Equals(containingType, "Microsoft.Extensions.Configuration.ConfigurationBinder", StringComparison.Ordinal))
        {
            if (string.Equals(originalMethod.Name, "Get", StringComparison.Ordinal))
            {
                kind = DirectConfigurationApiKind.Get;
            }
            else if (string.Equals(originalMethod.Name, "Bind", StringComparison.Ordinal))
            {
                if (originalMethod.Parameters.Any(parameter =>
                        string.Equals(parameter.Name, "key", StringComparison.Ordinal) &&
                        parameter.Type.SpecialType == SpecialType.System_String))
                {
                    return false;
                }

                kind = DirectConfigurationApiKind.Bind;
            }
            else
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        ExpressionSyntax? receiver = null;
        if (operation.TargetMethod.ReducedFrom is not null)
        {
            receiver = operation.Instance?.Syntax as ExpressionSyntax;
        }
        else
        {
            foreach (var argument in operation.Arguments)
            {
                if (argument.Parameter?.Ordinal == 0 && argument.Value.Syntax is ExpressionSyntax receiverExpression)
                {
                    receiver = receiverExpression;
                    break;
                }
            }
        }

        if (receiver is null)
        {
            return false;
        }

        ExpressionSyntax? keyExpression = null;
        if (keyParameterName is not null)
        {
            foreach (var argument in operation.Arguments)
            {
                if (string.Equals(argument.Parameter?.Name, keyParameterName, StringComparison.Ordinal) &&
                    argument.Value.Syntax is ExpressionSyntax argumentExpression)
                {
                    keyExpression = argumentExpression;
                    break;
                }
            }

            if (keyExpression is null)
            {
                return false;
            }
        }

        invocation = new DirectConfigurationInvocation(kind, originalMethod, receiver, keyExpression, syntax);
        return true;
    }

    private static void AnalyzeStandaloneRequiredSectionRead(
        SyntaxNodeAnalysisContext syntaxContext,
        DirectConfigurationInvocation invocation,
        ConfigurationSnapshot configuration,
        ConfigurationProviderSemantics providerSemantics)
    {
        var semanticModel = syntaxContext.SemanticModel;
        if (invocation.KeyExpression is not { } keyExpression ||
            !TryGetConfigurationSectionPath(
                invocation.Receiver,
                keyExpression,
                semanticModel,
                out var sectionPath,
                out var sectionExpression,
                out var sectionExpressionContainsFullPath) ||
            configuration.GetSectionExistence(sectionPath, providerSemantics) != ConfigurationSectionExistence.Missing ||
            IsOptionsRegistrationSectionRead(invocation.Syntax, semanticModel) ||
            ClassifyConfigurationReceiver(invocation.Receiver, semanticModel) !=
                ConfigurationReceiverProvenance.Contract ||
            ChainContainsMissingRequiredParent(
                invocation.Receiver,
                semanticModel,
                configuration,
                providerSemantics))
        {
            return;
        }

        ReportMissingSection(
            syntaxContext.ReportDiagnostic,
            DiagnosticDescriptors.ConfigurationKeyNotFound,
            sectionPath,
            sectionExpression,
            sectionExpressionContainsFullPath,
            configuration);
    }

    private static void AnalyzeBoundSectionRead(
        SyntaxNodeAnalysisContext syntaxContext,
        DirectConfigurationInvocation invocation,
        ConfigurationSnapshot configuration,
        ConfigurationProviderSemantics providerSemantics)
    {
        var semanticModel = syntaxContext.SemanticModel;
        if (!TryGetConfigurationSectionPath(invocation.Receiver, semanticModel, out var sectionPath, out var sectionExpression, out var sectionExpressionContainsFullPath) ||
            configuration.GetSectionExistence(sectionPath, providerSemantics) != ConfigurationSectionExistence.Missing ||
            IsOptionsRegistrationSectionRead(invocation.Syntax, semanticModel) ||
            ClassifyConfigurationReceiver(invocation.Receiver, semanticModel) !=
                ConfigurationReceiverProvenance.Contract ||
            ChainContainsMissingRequiredParent(
                invocation.Receiver,
                semanticModel,
                configuration,
                providerSemantics))
        {
            return;
        }

        ReportMissingSection(
            syntaxContext.ReportDiagnostic,
            DiagnosticDescriptors.ConfigurationKeyNotFound,
            sectionPath,
            sectionExpression,
            sectionExpressionContainsFullPath,
            configuration,
            requireSuggestion: true);
    }

    private static void AnalyzeConnectionStringRead(
        SyntaxNodeAnalysisContext syntaxContext,
        DirectConfigurationInvocation invocation,
        ConfigurationSnapshot configuration,
        ConfigurationProviderSemantics providerSemantics)
    {
        if (invocation.KeyExpression is not { } nameExpression)
        {
            return;
        }

        var semanticModel = syntaxContext.SemanticModel;
        if (!TryGetConstantSectionPath(nameExpression, semanticModel, out var connectionName))
        {
            return;
        }

        string? prefix = null;
        if (TryGetConfigurationSectionPath(invocation.Receiver, semanticModel, out var receiverPath, out _, out _))
        {
            prefix = receiverPath;
        }
        else
        {
            var receiverType = semanticModel.GetTypeInfo(invocation.Receiver).Type;
            if (IsConfigurationSectionType(receiverType) || !IsConfigurationType(receiverType))
            {
                return;
            }
        }

        var connectionStringsPath = prefix is null ? "ConnectionStrings" : prefix + ":ConnectionStrings";
        var fullPath = connectionStringsPath + ":" + connectionName;
        if (configuration.FindSections(connectionStringsPath).IsDefaultOrEmpty ||
            configuration.GetSectionExistence(fullPath, providerSemantics) != ConfigurationSectionExistence.Missing ||
            ClassifyConfigurationReceiver(invocation.Receiver, semanticModel) !=
                ConfigurationReceiverProvenance.Contract)
        {
            return;
        }

        // Connection strings are routinely supplied by environment variables or secret
        // stores, so a plain "name not in appsettings" signal is too weak on its own.
        // Only report when the name is a near-miss of a connection string that IS
        // declared in appsettings — a provable typo.
        ReportMissingSection(
            syntaxContext.ReportDiagnostic,
            DiagnosticDescriptors.ConfigurationKeyNotFound,
            fullPath,
            nameExpression,
            sectionExpressionContainsFullPath: false,
            configuration,
            requireSuggestion: true);
    }

    /// <summary>
    /// Determines whether the direct read feeds a recognized options registration in the
    /// same statement (for example `services.Configure&lt;T&gt;(config.GetRequiredSection("X"))`).
    /// Those reads are already covered by CFG001 through the registration itself, so
    /// reporting CFG009 as well would double-report the same missing section.
    /// </summary>
    private static bool IsOptionsRegistrationSectionRead(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        foreach (var candidate in invocation.Ancestors().OfType<InvocationExpressionSyntax>())
        {
            if (!TryCreateRegistration(candidate, semanticModel, out var registration))
            {
                continue;
            }

            var sectionArgument = candidate.ArgumentList.Arguments.FirstOrDefault(argument =>
                argument.Span.Contains(registration.SectionExpression.Span));
            if (sectionArgument is not null && sectionArgument.Expression.Span.Contains(invocation.Span))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Walks the receiver chain of a direct-read invocation looking for an earlier
    /// `GetRequiredSection` link whose own resolved path is already missing. That inner
    /// link produces its own CFG009, so the outer read stays quiet instead of cascading
    /// a second diagnostic for the same root cause.
    /// </summary>
    private static bool ChainContainsMissingRequiredParent(
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        ConfigurationSnapshot configuration,
        ConfigurationProviderSemantics providerSemantics)
    {
        ExpressionSyntax? expression = receiver;

        while (expression is not null)
        {
            expression = UnwrapForSectionChainResolution(expression);
            if (expression is ConditionalAccessExpressionSyntax conditionalAccess)
            {
                expression = conditionalAccess.Expression;
                continue;
            }

            if (expression is not InvocationExpressionSyntax parentInvocation ||
                parentInvocation.Expression is not MemberAccessExpressionSyntax parentMemberAccess ||
                !IsConfigurationSectionMethodName(parentMemberAccess.Name.Identifier.ValueText))
            {
                return false;
            }

            if (string.Equals(parentMemberAccess.Name.Identifier.ValueText, "GetRequiredSection", StringComparison.Ordinal) &&
                semanticModel.GetSymbolInfo(parentInvocation).Symbol is IMethodSymbol parentMethod &&
                string.Equals((parentMethod.ReducedFrom ?? parentMethod).ContainingType?.ToDisplayString(), "Microsoft.Extensions.Configuration.ConfigurationExtensions", StringComparison.Ordinal) &&
                TryGetConfigurationSectionPath(parentInvocation, semanticModel, out var parentPath, out _, out _) &&
                configuration.GetSectionExistence(parentPath, providerSemantics) == ConfigurationSectionExistence.Missing)
            {
                return true;
            }

            expression = parentMemberAccess.Expression;
        }

        return false;
    }

    private static ConfigurationProviderSemantics GetConfigurationProviderSemantics(Compilation compilation)
    {
        var jsonProvider = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider");
        if (jsonProvider is null)
        {
            return ConfigurationProviderSemantics.Unknown;
        }

        return jsonProvider.ContainingAssembly.Identity.Version.Major >= 10
            ? ConfigurationProviderSemantics.Net10OrLater
            : ConfigurationProviderSemantics.BeforeNet10;
    }

    private enum ConfigurationReceiverProvenance
    {
        Contract,
        Local,
        Custom,
        Ambiguous,
    }

    /// <summary>
    /// Classifies the root that supplies a direct configuration read. CFG009 only judges
    /// host configuration contracts; locally constructed, concrete custom, escaped, or
    /// flow-ambiguous receivers stay quiet because appsettings cannot prove their keys.
    /// </summary>
    private static ConfigurationReceiverProvenance ClassifyConfigurationReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        var root = GetConfigurationChainRoot(expression, semanticModel);
        return ClassifyConfigurationExpression(
            root,
            semanticModel,
            root.SpanStart,
            root.SpanStart,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default));
    }

    private static ConfigurationReceiverProvenance ClassifyConfigurationExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        int resolutionPosition,
        int safetyUntilPosition,
        HashSet<ILocalSymbol> visitedLocals)
    {
        expression = UnwrapForSectionChainResolution(expression);
        if (IsConfigurationBuilderBuildInvocation(expression, semanticModel))
        {
            return ConfigurationReceiverProvenance.Local;
        }

        if (expression is ObjectCreationExpressionSyntax)
        {
            var createdType = semanticModel.GetTypeInfo(expression).Type;
            return IsFrameworkConfigurationImplementation(createdType)
                ? ConfigurationReceiverProvenance.Local
                : ClassifyConfigurationType(createdType);
        }

        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        if (symbol is ILocalSymbol local)
        {
            return ClassifyLocalConfiguration(
                local,
                expression,
                semanticModel,
                resolutionPosition,
                safetyUntilPosition,
                visitedLocals);
        }

        if (symbol is IParameterSymbol or IFieldSymbol or IPropertySymbol)
        {
            var typeClassification = ClassifyConfigurationType(GetSymbolType(symbol));
            if (typeClassification != ConfigurationReceiverProvenance.Contract)
            {
                return typeClassification;
            }

            return HasUnsafeConfigurationUse(
                    symbol,
                    expression.FirstAncestorOrSelf<BlockSyntax>(),
                    semanticModel,
                    startPosition: expression.FirstAncestorOrSelf<BlockSyntax>()?.SpanStart ?? expression.SpanStart,
                    resolutionPosition,
                    safetyUntilPosition)
                ? ConfigurationReceiverProvenance.Ambiguous
                : ConfigurationReceiverProvenance.Contract;
        }

        return ConfigurationReceiverProvenance.Ambiguous;
    }

    private static ConfigurationReceiverProvenance ClassifyLocalConfiguration(
        ILocalSymbol local,
        ExpressionSyntax useExpression,
        SemanticModel semanticModel,
        int resolutionPosition,
        int safetyUntilPosition,
        HashSet<ILocalSymbol> visitedLocals)
    {
        if (!visitedLocals.Add(local) ||
            local.DeclaringSyntaxReferences.Length != 1 ||
            local.DeclaringSyntaxReferences[0].GetSyntax() is not VariableDeclaratorSyntax declarator ||
            declarator.SyntaxTree != useExpression.SyntaxTree ||
            declarator.FirstAncestorOrSelf<BlockSyntax>() is not { } declarationBlock ||
            useExpression.FirstAncestorOrSelf<BlockSyntax>() != declarationBlock)
        {
            return ConfigurationReceiverProvenance.Ambiguous;
        }

        try
        {
            ExpressionSyntax? definition = declarator.Initializer?.Value;
            var definitionEnd = declarator.Span.End;
            foreach (var statement in declarationBlock.Statements)
            {
                if (statement.SpanStart >= resolutionPosition)
                {
                    break;
                }

                if (TryGetDirectLocalAssignment(statement, local, semanticModel, out var assignment))
                {
                    definition = assignment.Right;
                    definitionEnd = statement.Span.End;
                }
            }

            if (definition is null || definitionEnd > resolutionPosition)
            {
                return ConfigurationReceiverProvenance.Ambiguous;
            }

            if (HasUnsafeConfigurationUse(
                    local,
                    declarationBlock,
                    semanticModel,
                    definitionEnd,
                    resolutionPosition,
                    safetyUntilPosition))
            {
                return ConfigurationReceiverProvenance.Ambiguous;
            }

            return ClassifyConfigurationExpression(
                definition,
                semanticModel,
                definition.SpanStart,
                safetyUntilPosition,
                visitedLocals);
        }
        finally
        {
            visitedLocals.Remove(local);
        }
    }

    private static bool TryGetDirectLocalAssignment(
        StatementSyntax statement,
        ILocalSymbol local,
        SemanticModel semanticModel,
        out AssignmentExpressionSyntax assignment)
    {
        if (statement is ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax candidate
            } &&
            candidate.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
            SymbolEqualityComparer.Default.Equals(
                semanticModel.GetSymbolInfo(candidate.Left).Symbol,
                local))
        {
            assignment = candidate;
            return true;
        }

        assignment = null!;
        return false;
    }

    private static bool HasUnsafeConfigurationUse(
        ISymbol symbol,
        BlockSyntax? block,
        SemanticModel semanticModel,
        int startPosition,
        int resolutionPosition,
        int safetyUntilPosition)
    {
        if (block is null)
        {
            return false;
        }

        foreach (var assignment in block.DescendantNodes()
                     .OfType<AssignmentExpressionSyntax>()
                     .Where(candidate => candidate.SpanStart >= startPosition &&
                                         candidate.Span.End <= safetyUntilPosition))
        {
            if (SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(assignment.Left).Symbol,
                    symbol))
            {
                if (assignment.SpanStart >= resolutionPosition ||
                    symbol is ILocalSymbol &&
                    assignment.Parent is ExpressionStatementSyntax { Parent: BlockSyntax parentBlock } &&
                    parentBlock == block &&
                    assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                {
                    continue;
                }

                return true;
            }

            if (IsExpressionRootedInSymbol(assignment.Left, symbol, semanticModel) ||
                assignment.Right.SpanStart != resolutionPosition &&
                ReferencesSymbol(assignment.Right, symbol, semanticModel))
            {
                if (assignment.Left is IdentifierNameSyntax { Identifier.ValueText: "_" } &&
                    semanticModel.GetSymbolInfo(assignment.Left).Symbol is null or IDiscardSymbol &&
                    assignment.Right is InvocationExpressionSyntax discardedInvocation &&
                    IsReadOnlyFrameworkConfigurationInvocation(discardedInvocation, semanticModel))
                {
                    continue;
                }

                return true;
            }
        }

        foreach (var invocation in block.DescendantNodes()
                     .OfType<InvocationExpressionSyntax>()
                     .Where(candidate => candidate.SpanStart >= startPosition &&
                                         candidate.Span.End <= safetyUntilPosition))
        {
            if (IsReadOnlyFrameworkConfigurationInvocation(invocation, semanticModel))
            {
                continue;
            }

            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                IsExpressionRootedInSymbol(memberAccess.Expression, symbol, semanticModel))
            {
                return true;
            }

            if (invocation.ArgumentList.Arguments.Any(argument =>
                    ReferencesSymbol(argument.Expression, symbol, semanticModel)))
            {
                return true;
            }
        }

        foreach (var creation in block.DescendantNodes()
                     .OfType<ObjectCreationExpressionSyntax>()
                     .Where(candidate => candidate.SpanStart >= startPosition &&
                                         candidate.Span.End <= safetyUntilPosition))
        {
            if (creation.ArgumentList?.Arguments.Any(argument =>
                    ReferencesSymbol(argument.Expression, symbol, semanticModel)) == true)
            {
                return true;
            }
        }

        foreach (var declarator in block.DescendantNodes()
                     .OfType<VariableDeclaratorSyntax>()
                     .Where(candidate => candidate.SpanStart >= startPosition &&
                                         candidate.Span.End <= safetyUntilPosition))
        {
            if (declarator.Initializer?.Value is { } initializer &&
                initializer.SpanStart != resolutionPosition &&
                ReferencesSymbol(initializer, symbol, semanticModel))
            {
                return true;
            }
        }

        foreach (var anonymousFunction in block.DescendantNodes()
                     .OfType<AnonymousFunctionExpressionSyntax>()
                     .Where(candidate => candidate.SpanStart >= startPosition &&
                                         candidate.Span.End <= safetyUntilPosition))
        {
            if (ReferencesSymbol(anonymousFunction, symbol, semanticModel))
            {
                return true;
            }
        }

        foreach (var localFunction in block.DescendantNodes()
                     .OfType<LocalFunctionStatementSyntax>()
                     .Where(candidate => candidate.SpanStart >= startPosition &&
                                         candidate.Span.End <= safetyUntilPosition))
        {
            if (ReferencesSymbol(localFunction, symbol, semanticModel))
            {
                return true;
            }
        }

        return block.DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Where(candidate => candidate.SpanStart >= startPosition &&
                                candidate.Span.End <= safetyUntilPosition)
            .Any(candidate => candidate.Expression is { } returned &&
                              ReferencesSymbol(returned, symbol, semanticModel));
    }

    private static bool IsExpressionRootedInSymbol(
        ExpressionSyntax expression,
        ISymbol symbol,
        SemanticModel semanticModel)
    {
        expression = UnwrapForSectionChainResolution(expression);
        return expression switch
        {
            IdentifierNameSyntax or MemberAccessExpressionSyntax
                when SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(expression).Symbol,
                    symbol) => true,
            MemberAccessExpressionSyntax memberAccess =>
                IsExpressionRootedInSymbol(memberAccess.Expression, symbol, semanticModel),
            ElementAccessExpressionSyntax elementAccess =>
                IsExpressionRootedInSymbol(elementAccess.Expression, symbol, semanticModel),
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax memberAccess } =>
                IsExpressionRootedInSymbol(memberAccess.Expression, symbol, semanticModel),
            _ => false,
        };
    }

    private static bool ReferencesSymbol(SyntaxNode node, ISymbol symbol, SemanticModel semanticModel)
    {
        return node.DescendantNodesAndSelf()
            .OfType<ExpressionSyntax>()
            .Any(expression => SymbolEqualityComparer.Default.Equals(
                semanticModel.GetSymbolInfo(expression).Symbol,
                symbol));
    }

    private static ITypeSymbol? GetSymbolType(ISymbol symbol)
    {
        return symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null,
        };
    }

    private static ConfigurationReceiverProvenance ClassifyConfigurationType(ITypeSymbol? type)
    {
        if (!IsConfigurationType(type))
        {
            return ConfigurationReceiverProvenance.Ambiguous;
        }

        if (type?.TypeKind == TypeKind.Interface || IsFrameworkConfigurationImplementation(type))
        {
            return ConfigurationReceiverProvenance.Contract;
        }

        return ConfigurationReceiverProvenance.Custom;
    }

    private static bool IsFrameworkConfigurationImplementation(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        var display = GetNonNullableDisplayString(type);
        return string.Equals(
                   display,
                   "Microsoft.Extensions.Configuration.ConfigurationManager",
                   StringComparison.Ordinal) ||
               string.Equals(
                   display,
                   "Microsoft.Extensions.Configuration.ConfigurationRoot",
                   StringComparison.Ordinal);
    }

    private static ExpressionSyntax GetConfigurationChainRoot(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        while (true)
        {
            expression = UnwrapForSectionChainResolution(expression);
            if (expression is ConditionalAccessExpressionSyntax conditionalAccess)
            {
                expression = conditionalAccess.Expression;
                continue;
            }

            if (expression is InvocationExpressionSyntax invocation &&
                semanticModel.GetOperation(invocation) is IInvocationOperation operation &&
                TryNormalizeDirectConfigurationInvocation(operation, out var directInvocation) &&
                directInvocation.Kind == DirectConfigurationApiKind.GetRequiredSection)
            {
                expression = directInvocation.Receiver;
                continue;
            }

            if (expression is InvocationExpressionSyntax getSectionInvocation &&
                getSectionInvocation.Expression is MemberAccessExpressionSyntax getSectionMemberAccess &&
                IsFrameworkConfigurationGetSectionInvocation(getSectionInvocation, semanticModel))
            {
                expression = getSectionMemberAccess.Expression;
                continue;
            }

            return expression;
        }
    }

    private static bool IsConfigurationBuilderBuildInvocation(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        return expression is InvocationExpressionSyntax invocation &&
               invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
               string.Equals(memberAccess.Name.Identifier.ValueText, "Build", StringComparison.Ordinal) &&
               IsOrImplements(semanticModel.GetTypeInfo(memberAccess.Expression).Type, "Microsoft.Extensions.Configuration.IConfigurationBuilder");
    }

    private static bool IsFrameworkConfigurationGetSectionInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (semanticModel.GetOperation(invocation) is not IInvocationOperation operation ||
            operation.TargetMethod.ReducedFrom is not null)
        {
            return false;
        }

        var configurationType = semanticModel.Compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.Configuration.IConfiguration");
        if (configurationType is null)
        {
            return false;
        }

        var contract = configurationType.GetMembers("GetSection")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method =>
                !method.IsStatic &&
                method.Parameters.Length == 1 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_String);
        if (contract is null)
        {
            return false;
        }

        var target = operation.TargetMethod;
        if (SymbolEqualityComparer.Default.Equals(target.OriginalDefinition, contract.OriginalDefinition))
        {
            return true;
        }

        return target.ContainingType.FindImplementationForInterfaceMember(contract) is IMethodSymbol implementation &&
               SymbolEqualityComparer.Default.Equals(
                   target.OriginalDefinition,
                   implementation.OriginalDefinition);
    }

    private static bool IsReadOnlyFrameworkConfigurationInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (IsFrameworkConfigurationGetSectionInvocation(invocation, semanticModel))
        {
            return true;
        }

        if (semanticModel.GetOperation(invocation) is not IInvocationOperation operation)
        {
            return false;
        }

        if (TryNormalizeDirectConfigurationInvocation(operation, out _))
        {
            return true;
        }

        var method = operation.TargetMethod.ReducedFrom ?? operation.TargetMethod;
        return string.Equals(
                   method.ContainingType?.ToDisplayString(),
                   "Microsoft.Extensions.Configuration.ConfigurationExtensions",
                   StringComparison.Ordinal) &&
               string.Equals(method.Name, "Exists", StringComparison.Ordinal);
    }

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
                    metadata.TypeKey,
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
        string typeKey,
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
        var reportKey =
            typeKey + "|" + DiagnosticDescriptors.ConfigurationValueTypeMismatch.Id + "|" + location.GetLineSpan().Path + "|" + property.FullPath;
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
            sectionExpression = invocation.ArgumentList.Arguments[0].Expression;
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
        var hasValidateOnStart = chain.MethodNames.Contains("ValidateOnStart") ||
            HasAddOptionsWithValidateOnStartReceiver(invocation, semanticModel);
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
            chain.MethodNames.Contains("ValidateOnStart") || HasAddOptionsWithValidateOnStartReceiver(invocation, semanticModel),
            chain.MethodNames.Any(IsValidationMethod) || HasAddOptionsWithValidateOnStartReceiver(invocation, semanticModel),
            bindsNonPublicProperties,
            errorsOnUnknownConfiguration,
            !supportsValidationRules || chain.MethodNames.Contains("ValidateDataAnnotations"),
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
                         .DescendantNodesAndSelf(ShouldDescendIntoSameExecutableScope)
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
                     .DescendantNodesAndSelf(ShouldDescendIntoSameExecutableScope)
                     .OfType<InvocationExpressionSyntax>())
        {
            yield return invocation;
        }
    }

    private static bool ShouldDescendIntoSameExecutableScope(SyntaxNode node)
    {
        return node is not LocalFunctionStatementSyntax &&
               node is not AnonymousFunctionExpressionSyntax;
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

    private static bool HasBindNonPublicPropertiesEnabled(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        return HasBinderOptionsBooleanEnabled(
            invocation,
            semanticModel,
            "BindNonPublicProperties",
            BinderOptionsBooleanDetection.AnyTopLevelConstantTrue);
    }

    private static bool HasErrorOnUnknownConfigurationEnabled(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        return HasBinderOptionsBooleanEnabled(
            invocation,
            semanticModel,
            "ErrorOnUnknownConfiguration",
            BinderOptionsBooleanDetection.LinearFinalConstantTrue);
    }

    private static bool HasBinderOptionsBooleanEnabled(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        string propertyName,
        BinderOptionsBooleanDetection detection)
    {
        return invocation.ArgumentList.Arguments.Any(argument =>
            ContainsBinderOptionsBooleanEnabled(argument.Expression, semanticModel, propertyName, detection));
    }

    private static bool ContainsBinderOptionsBooleanEnabled(
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        string propertyName,
        BinderOptionsBooleanDetection detection)
    {
        if (expression is null)
        {
            return false;
        }

        if (expression is SimpleLambdaExpressionSyntax simpleLambda)
        {
            var parameter = semanticModel.GetDeclaredSymbol(simpleLambda.Parameter);
            return parameter is not null &&
                   (ContainsBinderOptionsBooleanEnabled(simpleLambda.ExpressionBody, semanticModel, parameter, propertyName) ||
                    ContainsBinderOptionsBooleanEnabled(simpleLambda.Block, semanticModel, parameter, propertyName, detection));
        }

        if (expression is ParenthesizedLambdaExpressionSyntax parenthesizedLambda)
        {
            var parameter = parenthesizedLambda.ParameterList.Parameters.Count == 1
                ? semanticModel.GetDeclaredSymbol(parenthesizedLambda.ParameterList.Parameters[0])
                : null;
            return parameter is not null &&
                   (ContainsBinderOptionsBooleanEnabled(parenthesizedLambda.ExpressionBody, semanticModel, parameter, propertyName) ||
                    ContainsBinderOptionsBooleanEnabled(parenthesizedLambda.Block, semanticModel, parameter, propertyName, detection));
        }

        return false;
    }

    private static bool ContainsBinderOptionsBooleanEnabled(
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        string propertyName)
    {
        return TryGetBinderOptionsBooleanAssignment(
            expression,
            semanticModel,
            binderOptionsParameter,
            binderOptionsAliases: null,
            parameterStillTargetsRuntimeOptions: true,
            propertyName,
            out var value) &&
            value == true;
    }

    private static bool ContainsBinderOptionsBooleanEnabled(
        BlockSyntax? block,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        string propertyName,
        BinderOptionsBooleanDetection detection)
    {
        if (block is null)
        {
            return false;
        }

        if (detection == BinderOptionsBooleanDetection.AnyTopLevelConstantTrue)
        {
            var topLevelParameterStillTargetsRuntimeOptions = true;
            var binderOptionsAliases = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
            foreach (var statement in block.Statements)
            {
                UpdateBinderOptionsAliases(
                    statement,
                    semanticModel,
                    binderOptionsParameter,
                    topLevelParameterStillTargetsRuntimeOptions,
                    binderOptionsAliases);

                if (IsTopLevelBinderOptionsParameterAssignment(statement, semanticModel, binderOptionsParameter))
                {
                    topLevelParameterStillTargetsRuntimeOptions = false;
                }

                if (statement is ExpressionStatementSyntax expressionStatement &&
                    TryGetBinderOptionsBooleanAssignment(
                        expressionStatement.Expression,
                        semanticModel,
                        binderOptionsParameter,
                        binderOptionsAliases,
                        topLevelParameterStillTargetsRuntimeOptions,
                        propertyName,
                        out var value) &&
                    value == true)
                {
                    return true;
                }

                if (ContainsBinderOptionsParameterAssignment(statement, semanticModel, binderOptionsParameter))
                {
                    topLevelParameterStillTargetsRuntimeOptions = false;
                }
            }

            return false;
        }

        bool? finalValue = null;
        var hasNonLinearControlFlow = false;
        var parameterStillTargetsRuntimeOptions = true;
        var runtimeBinderOptionsAliases = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        foreach (var statement in block.Statements)
        {
            UpdateBinderOptionsAliases(
                statement,
                semanticModel,
                binderOptionsParameter,
                parameterStillTargetsRuntimeOptions,
                runtimeBinderOptionsAliases);

            if (ContainsNonLinearControlFlow(statement))
            {
                hasNonLinearControlFlow = true;
            }

            if (IsTopLevelBinderOptionsParameterAssignment(statement, semanticModel, binderOptionsParameter))
            {
                parameterStillTargetsRuntimeOptions = false;
                continue;
            }

            if (statement is ExpressionStatementSyntax expressionStatement &&
                TryGetBinderOptionsBooleanAssignment(
                    expressionStatement.Expression,
                    semanticModel,
                    binderOptionsParameter,
                    runtimeBinderOptionsAliases,
                    parameterStillTargetsRuntimeOptions,
                    propertyName,
                    out var value))
            {
                finalValue = value;
                continue;
            }

            if (ContainsBinderOptionsBooleanAssignment(
                        statement,
                        semanticModel,
                        binderOptionsParameter,
                        runtimeBinderOptionsAliases,
                        parameterStillTargetsRuntimeOptions,
                        propertyName))
            {
                finalValue = null;
            }

            if (ContainsRuntimeBinderOptionsAliasDeclaration(
                    statement,
                    semanticModel,
                    binderOptionsParameter,
                    runtimeBinderOptionsAliases,
                    parameterStillTargetsRuntimeOptions))
            {
                finalValue = null;
            }

            if (ContainsRuntimeBinderOptionsEscape(
                    statement,
                    semanticModel,
                    binderOptionsParameter,
                    runtimeBinderOptionsAliases,
                    parameterStillTargetsRuntimeOptions))
            {
                finalValue = null;
            }

            if (ContainsBinderOptionsParameterAssignment(statement, semanticModel, binderOptionsParameter))
            {
                parameterStillTargetsRuntimeOptions = false;
                finalValue = null;
            }
        }

        return !hasNonLinearControlFlow &&
               finalValue == true;
    }

    private enum BinderOptionsBooleanDetection
    {
        AnyTopLevelConstantTrue,
        LinearFinalConstantTrue
    }

    private static bool TryGetBinderOptionsBooleanAssignment(
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        HashSet<ILocalSymbol>? binderOptionsAliases,
        bool parameterStillTargetsRuntimeOptions,
        string propertyName,
        out bool? value)
    {
        value = null;
        if (expression is not AssignmentExpressionSyntax assignment ||
            !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            return false;
        }

        if (IsBinderOptionsBooleanAssignmentTarget(
                assignment.Left,
                semanticModel,
                binderOptionsParameter,
                binderOptionsAliases,
                parameterStillTargetsRuntimeOptions,
                propertyName))
        {
            var constant = semanticModel.GetConstantValue(assignment.Right);
            if (constant.HasValue &&
                constant.Value is bool enabled)
            {
                value = enabled;
            }

            return true;
        }

        return TryGetTupleBinderOptionsBooleanAssignment(
            assignment.Left,
            assignment.Right,
            semanticModel,
            binderOptionsParameter,
            binderOptionsAliases,
            parameterStillTargetsRuntimeOptions,
            propertyName,
            out value);
    }

    /// <summary>
    /// Handles a tuple-deconstruction assignment such as
    /// <c>(options.ErrorOnUnknownConfiguration, options.BindNonPublicProperties) = (true, false);</c>,
    /// whose top-level <see cref="AssignmentExpressionSyntax"/> has a <see cref="TupleExpressionSyntax"/>
    /// on both sides rather than a direct member-access target. Matches each left-side element against
    /// <paramref name="propertyName"/> and, when found, reads the correspondingly-positioned right-side
    /// element's constant value.
    /// </summary>
    private static bool TryGetTupleBinderOptionsBooleanAssignment(
        ExpressionSyntax left,
        ExpressionSyntax right,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        HashSet<ILocalSymbol>? binderOptionsAliases,
        bool parameterStillTargetsRuntimeOptions,
        string propertyName,
        out bool? value)
    {
        value = null;
        if (left is not TupleExpressionSyntax leftTuple ||
            right is not TupleExpressionSyntax rightTuple ||
            leftTuple.Arguments.Count != rightTuple.Arguments.Count)
        {
            return false;
        }

        var found = false;
        for (var i = 0; i < leftTuple.Arguments.Count; i++)
        {
            var leftElement = leftTuple.Arguments[i].Expression;
            var rightElement = rightTuple.Arguments[i].Expression;

            if (IsBinderOptionsBooleanAssignmentTarget(
                    leftElement,
                    semanticModel,
                    binderOptionsParameter,
                    binderOptionsAliases,
                    parameterStillTargetsRuntimeOptions,
                    propertyName))
            {
                found = true;
                var constant = semanticModel.GetConstantValue(rightElement);
                value = constant.HasValue && constant.Value is bool enabled ? enabled : null;
                continue;
            }

            // A tuple element can itself be a nested tuple deconstruction
            // (e.g. `((options.ErrorOnUnknownConfiguration, _), y) = ((true, 0), 0);`).
            if (TryGetTupleBinderOptionsBooleanAssignment(
                    leftElement,
                    rightElement,
                    semanticModel,
                    binderOptionsParameter,
                    binderOptionsAliases,
                    parameterStillTargetsRuntimeOptions,
                    propertyName,
                    out var nestedValue))
            {
                found = true;
                value = nestedValue;
            }
        }

        // A sibling tuple element can alias the runtime BinderOptions object itself
        // (e.g. `(options.ErrorOnUnknownConfiguration, alias) = (true, options);`). The
        // caller treats this whole statement as handled once the target property is
        // found, so a would-be alias created this way is never added to
        // binderOptionsAliases and a later reset through it would go unseen. Stay
        // conservative rather than trust the constant when that risk is present.
        if (found &&
            TupleCreatesUntrackedBinderOptionsAlias(
                leftTuple,
                rightTuple,
                semanticModel,
                binderOptionsParameter,
                binderOptionsAliases,
                parameterStillTargetsRuntimeOptions))
        {
            value = null;
        }

        return found;
    }

    private static bool TupleCreatesUntrackedBinderOptionsAlias(
        TupleExpressionSyntax leftTuple,
        TupleExpressionSyntax rightTuple,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        HashSet<ILocalSymbol>? binderOptionsAliases,
        bool parameterStillTargetsRuntimeOptions)
    {
        // Beyond a bare sibling reference (checked per-element below), a sibling element's
        // right-hand side can pass the runtime BinderOptions into a helper call, or assign
        // it to a non-local (field/property), the same broader escape shapes
        // ContainsRuntimeBinderOptionsEscape already recognizes for a plain assignment.
        if (ContainsRuntimeBinderOptionsEscape(
                rightTuple,
                semanticModel,
                binderOptionsParameter,
                binderOptionsAliases ?? new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                parameterStillTargetsRuntimeOptions))
        {
            return true;
        }

        for (var i = 0; i < rightTuple.Arguments.Count; i++)
        {
            var leftElement = leftTuple.Arguments[i].Expression;
            var rightElement = rightTuple.Arguments[i].Expression;

            if (leftElement is TupleExpressionSyntax nestedLeft &&
                rightElement is TupleExpressionSyntax nestedRight)
            {
                if (TupleCreatesUntrackedBinderOptionsAlias(
                        nestedLeft,
                        nestedRight,
                        semanticModel,
                        binderOptionsParameter,
                        binderOptionsAliases,
                        parameterStillTargetsRuntimeOptions))
                {
                    return true;
                }

                continue;
            }

            // Reassigning the binder-options parameter itself through a tuple element
            // (e.g. `(options.ErrorOnUnknownConfiguration, options) = (true, new BinderOptions());`)
            // means later writes through `options` in this statement no longer target the
            // runtime BinderOptions, the same shape ContainsBinderOptionsParameterAssignment
            // already tracks for a plain (non-tuple) assignment.
            if (IsBinderOptionsParameterAssignmentTarget(leftElement, semanticModel, binderOptionsParameter))
            {
                return true;
            }

            if (IsRuntimeBinderOptionsReference(
                    rightElement,
                    semanticModel,
                    binderOptionsParameter,
                    binderOptionsAliases,
                    parameterStillTargetsRuntimeOptions))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsBinderOptionsBooleanAssignment(
        SyntaxNode node,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        HashSet<ILocalSymbol> binderOptionsAliases,
        bool parameterStillTargetsRuntimeOptions,
        string propertyName)
    {
        foreach (var assignment in node
                     .DescendantNodes(ShouldDescendIntoBinderOptionsNode)
                     .OfType<AssignmentExpressionSyntax>())
        {
            if (IsBinderOptionsBooleanAssignmentTarget(
                    assignment.Left,
                    semanticModel,
                    binderOptionsParameter,
                    binderOptionsAliases,
                    parameterStillTargetsRuntimeOptions,
                    propertyName))
            {
                return true;
            }

            if (assignment.Left is TupleExpressionSyntax tuple &&
                TupleContainsBinderOptionsBooleanAssignmentTarget(
                    tuple,
                    semanticModel,
                    binderOptionsParameter,
                    binderOptionsAliases,
                    parameterStillTargetsRuntimeOptions,
                    propertyName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TupleContainsBinderOptionsBooleanAssignmentTarget(
        TupleExpressionSyntax tuple,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        HashSet<ILocalSymbol> binderOptionsAliases,
        bool parameterStillTargetsRuntimeOptions,
        string propertyName)
    {
        foreach (var argument in tuple.Arguments)
        {
            if (IsBinderOptionsBooleanAssignmentTarget(
                    argument.Expression,
                    semanticModel,
                    binderOptionsParameter,
                    binderOptionsAliases,
                    parameterStillTargetsRuntimeOptions,
                    propertyName))
            {
                return true;
            }

            if (argument.Expression is TupleExpressionSyntax nested &&
                TupleContainsBinderOptionsBooleanAssignmentTarget(
                    nested,
                    semanticModel,
                    binderOptionsParameter,
                    binderOptionsAliases,
                    parameterStillTargetsRuntimeOptions,
                    propertyName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsRuntimeBinderOptionsAliasDeclaration(
        SyntaxNode node,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        HashSet<ILocalSymbol> binderOptionsAliases,
        bool parameterStillTargetsRuntimeOptions)
    {
        foreach (var localDeclaration in node
                     .DescendantNodes(ShouldDescendIntoBinderOptionsNode)
                     .OfType<LocalDeclarationStatementSyntax>())
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (variable.Initializer?.Value is { } initializer &&
                    IsRuntimeBinderOptionsReference(
                        initializer,
                        semanticModel,
                        binderOptionsParameter,
                        binderOptionsAliases,
                        parameterStillTargetsRuntimeOptions))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsRuntimeBinderOptionsEscape(
        SyntaxNode node,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        HashSet<ILocalSymbol> binderOptionsAliases,
        bool parameterStillTargetsRuntimeOptions)
    {
        foreach (var assignment in node
                     .DescendantNodesAndSelf(ShouldDescendIntoBinderOptionsNode)
                     .OfType<AssignmentExpressionSyntax>())
        {
            if (IsRuntimeBinderOptionsReference(
                    assignment.Right,
                    semanticModel,
                    binderOptionsParameter,
                    binderOptionsAliases,
                    parameterStillTargetsRuntimeOptions) &&
                semanticModel.GetSymbolInfo(assignment.Left).Symbol is not ILocalSymbol)
            {
                return true;
            }
        }

        foreach (var invocation in node
                     .DescendantNodesAndSelf(ShouldDescendIntoBinderOptionsNode)
                     .OfType<InvocationExpressionSyntax>())
        {
            if (InvocationMayRunLocalBinderOptionsHelper(
                    invocation,
                    semanticModel,
                    binderOptionsParameter,
                    binderOptionsAliases,
                    parameterStillTargetsRuntimeOptions))
            {
                return true;
            }

            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol { ReducedFrom: not null } &&
                IsRuntimeBinderOptionsReference(
                    memberAccess.Expression,
                    semanticModel,
                    binderOptionsParameter,
                    binderOptionsAliases,
                    parameterStillTargetsRuntimeOptions))
            {
                return true;
            }

            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                if (IsRuntimeBinderOptionsReference(
                        argument.Expression,
                        semanticModel,
                        binderOptionsParameter,
                        binderOptionsAliases,
                        parameterStillTargetsRuntimeOptions) ||
                    ArgumentMayCaptureRuntimeBinderOptions(
                        argument.Expression,
                        semanticModel,
                        binderOptionsParameter,
                        binderOptionsAliases,
                        parameterStillTargetsRuntimeOptions))
                {
                    return true;
                }
            }
        }

        foreach (var objectCreation in node
                     .DescendantNodesAndSelf(ShouldDescendIntoBinderOptionsNode)
                     .OfType<ObjectCreationExpressionSyntax>())
        {
            if (ContainsRuntimeBinderOptionsArgument(
                    objectCreation.ArgumentList?.Arguments,
                    semanticModel,
                    binderOptionsParameter,
                    binderOptionsAliases,
                    parameterStillTargetsRuntimeOptions))
            {
                return true;
            }
        }

        foreach (var implicitObjectCreation in node
                     .DescendantNodesAndSelf(ShouldDescendIntoBinderOptionsNode)
                     .OfType<ImplicitObjectCreationExpressionSyntax>())
        {
            if (ContainsRuntimeBinderOptionsArgument(
                    implicitObjectCreation.ArgumentList?.Arguments,
                    semanticModel,
                    binderOptionsParameter,
                    binderOptionsAliases,
                    parameterStillTargetsRuntimeOptions))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InvocationMayRunLocalBinderOptionsHelper(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        HashSet<ILocalSymbol> binderOptionsAliases,
        bool parameterStillTargetsRuntimeOptions)
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol { MethodKind: MethodKind.LocalFunction } localFunction &&
            LocalFunctionReferencesRuntimeBinderOptions(
                localFunction,
                semanticModel,
                binderOptionsParameter,
                binderOptionsAliases,
                parameterStillTargetsRuntimeOptions))
        {
            return true;
        }

        if (TryGetInvokedLocalDelegate(invocation, semanticModel, out var local) &&
            LocalDelegateMayReferenceRuntimeBinderOptions(
                local,
                semanticModel,
                binderOptionsParameter,
                binderOptionsAliases,
                parameterStillTargetsRuntimeOptions))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Detects an argument that hands a captured reset delegate/lambda to a call which may
    /// invoke it — an inline lambda (<c>RunNow(() =&gt; options.ErrorOnUnknownConfiguration =
    /// false)</c>) or a local delegate variable (<c>RunNow(disableStrict)</c>) whose body
    /// references the runtime binder options. A directly-invoked reset delegate is already
    /// handled by <see cref="InvocationMayRunLocalBinderOptionsHelper"/>; this closes the
    /// passed-as-argument shape so the runtime binder options are treated as escaped and
    /// CFG007 stays conservative instead of firing a false Warning.
    /// </summary>
    private static bool ArgumentMayCaptureRuntimeBinderOptions(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        HashSet<ILocalSymbol> binderOptionsAliases,
        bool parameterStillTargetsRuntimeOptions)
    {
        // An inline lambda or anonymous method (all derive from
        // AnonymousFunctionExpressionSyntax, which exposes both the expression body and the
        // statement block) whose body references the runtime binder options.
        if (expression is AnonymousFunctionExpressionSyntax anonymousFunction)
        {
            return AnonymousFunctionReferencesRuntimeBinderOptions(
                anonymousFunction.ExpressionBody,
                anonymousFunction.Block,
                semanticModel,
                binderOptionsParameter,
                binderOptionsAliases,
                parameterStillTargetsRuntimeOptions);
        }

        return semanticModel.GetSymbolInfo(expression).Symbol is ILocalSymbol { Type.TypeKind: TypeKind.Delegate } local &&
               LocalDelegateMayReferenceRuntimeBinderOptions(
                   local,
                   semanticModel,
                   binderOptionsParameter,
                   binderOptionsAliases,
                   parameterStillTargetsRuntimeOptions);
    }

    private static bool TryGetInvokedLocalDelegate(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        out ILocalSymbol local)
    {
        if (semanticModel.GetSymbolInfo(invocation.Expression).Symbol is ILocalSymbol directLocal &&
            directLocal.Type.TypeKind == TypeKind.Delegate)
        {
            local = directLocal;
            return true;
        }

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            string.Equals(memberAccess.Name.Identifier.ValueText, "Invoke", StringComparison.Ordinal) &&
            semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol is ILocalSymbol invokeLocal &&
            invokeLocal.Type.TypeKind == TypeKind.Delegate)
        {
            local = invokeLocal;
            return true;
        }

        if (invocation.Expression is MemberBindingExpressionSyntax memberBinding &&
            string.Equals(memberBinding.Name.Identifier.ValueText, "Invoke", StringComparison.Ordinal) &&
            TryGetConditionalAccess(invocation, out var conditionalAccess) &&
            semanticModel.GetSymbolInfo(conditionalAccess.Expression).Symbol is ILocalSymbol conditionalLocal &&
            conditionalLocal.Type.TypeKind == TypeKind.Delegate)
        {
            local = conditionalLocal;
            return true;
        }

        local = null!;
        return false;
    }

    private static bool TryGetConditionalAccess(
        InvocationExpressionSyntax invocation,
        out ConditionalAccessExpressionSyntax conditionalAccess)
    {
        if (invocation.Parent is ConditionalAccessExpressionSyntax directParent &&
            directParent.WhenNotNull == invocation)
        {
            conditionalAccess = directParent;
            return true;
        }

        if (invocation.Parent?.Parent is ConditionalAccessExpressionSyntax grandParent &&
            grandParent.WhenNotNull == invocation.Parent)
        {
            conditionalAccess = grandParent;
            return true;
        }

        conditionalAccess = null!;
        return false;
    }

    private static bool LocalFunctionReferencesRuntimeBinderOptions(
        IMethodSymbol localFunction,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        HashSet<ILocalSymbol> binderOptionsAliases,
        bool parameterStillTargetsRuntimeOptions)
    {
        foreach (var declaration in localFunction.DeclaringSyntaxReferences
                     .Select(reference => reference.GetSyntax())
                     .OfType<LocalFunctionStatementSyntax>())
        {
            if (declaration.ExpressionBody?.Expression is { } expressionBody &&
                ContainsRuntimeBinderOptionsReference(
                    expressionBody,
                    semanticModel,
                    binderOptionsParameter,
                    binderOptionsAliases,
                    parameterStillTargetsRuntimeOptions))
            {
                return true;
            }

            if (declaration.Body is { } body &&
                ContainsRuntimeBinderOptionsReference(
                    body,
                    semanticModel,
                    binderOptionsParameter,
                    binderOptionsAliases,
                    parameterStillTargetsRuntimeOptions))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LocalDelegateMayReferenceRuntimeBinderOptions(
        ILocalSymbol local,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        HashSet<ILocalSymbol> binderOptionsAliases,
        bool parameterStillTargetsRuntimeOptions)
    {
        foreach (var declaration in local.DeclaringSyntaxReferences
                     .Select(reference => reference.GetSyntax())
                     .OfType<VariableDeclaratorSyntax>())
        {
            if (declaration.Initializer?.Value is null)
            {
                return true;
            }

            if (LocalDelegateIsReassigned(local, declaration, semanticModel))
            {
                return true;
            }

            return declaration.Initializer.Value switch
            {
                SimpleLambdaExpressionSyntax simpleLambda => AnonymousFunctionReferencesRuntimeBinderOptions(
                    simpleLambda.ExpressionBody,
                    simpleLambda.Block,
                    semanticModel,
                    binderOptionsParameter,
                    binderOptionsAliases,
                    parameterStillTargetsRuntimeOptions),
                ParenthesizedLambdaExpressionSyntax parenthesizedLambda => AnonymousFunctionReferencesRuntimeBinderOptions(
                    parenthesizedLambda.ExpressionBody,
                    parenthesizedLambda.Block,
                    semanticModel,
                    binderOptionsParameter,
                    binderOptionsAliases,
                    parameterStillTargetsRuntimeOptions),
                AnonymousMethodExpressionSyntax anonymousMethod => anonymousMethod.Block is null ||
                    ContainsRuntimeBinderOptionsReference(
                        anonymousMethod.Block,
                        semanticModel,
                        binderOptionsParameter,
                        binderOptionsAliases,
                        parameterStillTargetsRuntimeOptions),
                _ => true
            };
        }

        return true;
    }

    private static bool LocalDelegateIsReassigned(
        ILocalSymbol local,
        VariableDeclaratorSyntax declaration,
        SemanticModel semanticModel)
    {
        var containingBlock = declaration.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock is null)
        {
            return true;
        }

        foreach (var assignment in containingBlock
                     .DescendantNodes(ShouldDescendIntoBinderOptionsNode)
                     .OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left.SpanStart <= declaration.SpanStart)
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(assignment.Left).Symbol,
                    local))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnonymousFunctionReferencesRuntimeBinderOptions(
        CSharpSyntaxNode? expressionBody,
        BlockSyntax? block,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        HashSet<ILocalSymbol> binderOptionsAliases,
        bool parameterStillTargetsRuntimeOptions)
    {
        if (expressionBody is not null &&
            ContainsRuntimeBinderOptionsReference(
                expressionBody,
                semanticModel,
                binderOptionsParameter,
                binderOptionsAliases,
                parameterStillTargetsRuntimeOptions))
        {
            return true;
        }

        return block is not null &&
               ContainsRuntimeBinderOptionsReference(
                   block,
                   semanticModel,
                   binderOptionsParameter,
                   binderOptionsAliases,
                   parameterStillTargetsRuntimeOptions);
    }

    private static bool ContainsRuntimeBinderOptionsReference(
        SyntaxNode node,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        HashSet<ILocalSymbol> binderOptionsAliases,
        bool parameterStillTargetsRuntimeOptions)
    {
        foreach (var expression in node
                     .DescendantNodesAndSelf(ShouldDescendIntoBinderOptionsNode)
                     .OfType<ExpressionSyntax>())
        {
            if (IsRuntimeBinderOptionsReference(
                    expression,
                    semanticModel,
                    binderOptionsParameter,
                    binderOptionsAliases,
                    parameterStillTargetsRuntimeOptions))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsRuntimeBinderOptionsArgument(
        SeparatedSyntaxList<ArgumentSyntax>? arguments,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        HashSet<ILocalSymbol> binderOptionsAliases,
        bool parameterStillTargetsRuntimeOptions)
    {
        if (arguments is null)
        {
            return false;
        }

        foreach (var argument in arguments.Value)
        {
            if (IsRuntimeBinderOptionsReference(
                    argument.Expression,
                    semanticModel,
                    binderOptionsParameter,
                    binderOptionsAliases,
                    parameterStillTargetsRuntimeOptions))
            {
                return true;
            }
        }

        return false;
    }

    private static void UpdateBinderOptionsAliases(
        StatementSyntax statement,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        bool parameterStillTargetsRuntimeOptions,
        HashSet<ILocalSymbol> binderOptionsAliases)
    {
        if (statement is LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol local)
                {
                    continue;
                }

                if (variable.Initializer?.Value is { } initializer &&
                    IsRuntimeBinderOptionsReference(
                        initializer,
                        semanticModel,
                        binderOptionsParameter,
                        binderOptionsAliases,
                        parameterStillTargetsRuntimeOptions))
                {
                    binderOptionsAliases.Add(local);
                }
                else
                {
                    binderOptionsAliases.Remove(local);
                }
            }

            return;
        }

        if (statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } &&
            semanticModel.GetSymbolInfo(assignment.Left).Symbol is ILocalSymbol localSymbol)
        {
            if (IsRuntimeBinderOptionsReference(
                    assignment.Right,
                    semanticModel,
                    binderOptionsParameter,
                    binderOptionsAliases,
                    parameterStillTargetsRuntimeOptions))
            {
                binderOptionsAliases.Add(localSymbol);
            }
            else
            {
                binderOptionsAliases.Remove(localSymbol);
            }
        }
    }

    private static bool IsTopLevelBinderOptionsParameterAssignment(
        StatementSyntax statement,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter)
    {
        return statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } &&
               AssignmentTargetsBinderOptionsParameter(assignment.Left, semanticModel, binderOptionsParameter);
    }

    /// <summary>
    /// True when <paramref name="left"/> reassigns the binder-options parameter itself, either
    /// directly or as one element of a (possibly nested) tuple-deconstruction assignment (e.g.
    /// <c>(options.ErrorOnUnknownConfiguration, options) = (true, new BinderOptions());</c>).
    /// </summary>
    private static bool AssignmentTargetsBinderOptionsParameter(
        ExpressionSyntax left,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter)
    {
        if (IsBinderOptionsParameterAssignmentTarget(left, semanticModel, binderOptionsParameter))
        {
            return true;
        }

        if (left is TupleExpressionSyntax tuple)
        {
            foreach (var argument in tuple.Arguments)
            {
                if (AssignmentTargetsBinderOptionsParameter(argument.Expression, semanticModel, binderOptionsParameter))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsBinderOptionsParameterAssignment(
        SyntaxNode node,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter)
    {
        foreach (var assignment in node
                     .DescendantNodes(ShouldDescendIntoBinderOptionsNode)
                     .OfType<AssignmentExpressionSyntax>())
        {
            if (AssignmentTargetsBinderOptionsParameter(assignment.Left, semanticModel, binderOptionsParameter))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBinderOptionsParameterAssignmentTarget(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter)
    {
        return SymbolEqualityComparer.Default.Equals(
            semanticModel.GetSymbolInfo(expression).Symbol,
            binderOptionsParameter);
    }

    private static bool ContainsNonLinearControlFlow(SyntaxNode node)
    {
        foreach (var descendant in node.DescendantNodesAndSelf(ShouldDescendIntoBinderOptionsNode))
        {
            if (descendant.IsKind(SyntaxKind.ReturnStatement) ||
                descendant.IsKind(SyntaxKind.GotoStatement) ||
                descendant.IsKind(SyntaxKind.GotoCaseStatement) ||
                descendant.IsKind(SyntaxKind.GotoDefaultStatement) ||
                descendant.IsKind(SyntaxKind.BreakStatement) ||
                descendant.IsKind(SyntaxKind.ContinueStatement) ||
                descendant.IsKind(SyntaxKind.ThrowStatement) ||
                descendant.IsKind(SyntaxKind.YieldBreakStatement))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldDescendIntoBinderOptionsNode(SyntaxNode node)
    {
        return node is not AnonymousFunctionExpressionSyntax and
               not LocalFunctionStatementSyntax;
    }

    private static bool IsBinderOptionsBooleanAssignmentTarget(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        HashSet<ILocalSymbol>? binderOptionsAliases,
        bool parameterStillTargetsRuntimeOptions,
        string propertyName)
    {
        if (expression is not MemberAccessExpressionSyntax memberAccess ||
            !string.Equals(memberAccess.Name.Identifier.ValueText, propertyName, StringComparison.Ordinal))
        {
            return false;
        }

        var property = semanticModel.GetSymbolInfo(memberAccess).Symbol as IPropertySymbol;
        if (property is null ||
            !string.Equals(property.Name, propertyName, StringComparison.Ordinal) ||
            !string.Equals(property.ContainingType.ToDisplayString(), "Microsoft.Extensions.Configuration.BinderOptions", StringComparison.Ordinal))
        {
            return false;
        }

        return IsRuntimeBinderOptionsReference(
            memberAccess.Expression,
            semanticModel,
            binderOptionsParameter,
            binderOptionsAliases,
            parameterStillTargetsRuntimeOptions);
    }

    private static bool IsRuntimeBinderOptionsReference(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        HashSet<ILocalSymbol>? binderOptionsAliases,
        bool parameterStillTargetsRuntimeOptions)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        if (parameterStillTargetsRuntimeOptions &&
            SymbolEqualityComparer.Default.Equals(symbol, binderOptionsParameter))
        {
            return true;
        }

        return symbol is ILocalSymbol localSymbol &&
               binderOptionsAliases is not null &&
               binderOptionsAliases.Contains(localSymbol);
    }

    private static bool TryGetConstantSectionPath(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        out string sectionPath)
    {
        var constant = semanticModel.GetConstantValue(expression);
        if (constant.HasValue &&
            constant.Value is string value &&
            !string.IsNullOrWhiteSpace(value))
        {
            sectionPath = value;
            return true;
        }

        sectionPath = null!;
        return false;
    }

    private static bool TryGetConfigurationSectionPath(
        ExpressionSyntax receiver,
        ExpressionSyntax keyExpression,
        SemanticModel semanticModel,
        out string sectionPath,
        out ExpressionSyntax sectionExpression,
        out bool sectionExpressionContainsFullPath)
    {
        sectionPath = null!;
        sectionExpression = null!;
        sectionExpressionContainsFullPath = false;

        if (!TryGetConstantSectionPath(keyExpression, semanticModel, out var currentSectionPath))
        {
            return false;
        }

        if (TryGetConfigurationSectionPath(
                receiver,
                semanticModel,
                out var parentSectionPath,
                out _,
                out _))
        {
            sectionPath = parentSectionPath + ":" + currentSectionPath;
            sectionExpression = keyExpression;
            return true;
        }

        var receiverType = semanticModel.GetTypeInfo(receiver).Type;
        if (IsConfigurationSectionType(receiverType) || !IsConfigurationType(receiverType))
        {
            return false;
        }

        sectionPath = currentSectionPath;
        sectionExpression = keyExpression;
        sectionExpressionContainsFullPath = true;
        return true;
    }

    private static bool TryGetConfigurationSectionPath(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        out string sectionPath,
        out ExpressionSyntax sectionExpression,
        out bool sectionExpressionContainsFullPath)
    {
        sectionPath = null!;
        sectionExpression = null!;
        sectionExpressionContainsFullPath = false;

        expression = UnwrapForSectionChainResolution(expression);

        if (expression is ConditionalAccessExpressionSyntax conditionalAccess)
        {
            return TryGetConfigurationSectionPathFromWhenNotNull(
                conditionalAccess.WhenNotNull,
                conditionalAccess.Expression,
                semanticModel,
                out sectionPath,
                out sectionExpression,
                out sectionExpressionContainsFullPath);
        }

        if (expression is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        if (semanticModel.GetOperation(invocation) is IInvocationOperation operation &&
            TryNormalizeDirectConfigurationInvocation(operation, out var directInvocation) &&
            directInvocation.Kind == DirectConfigurationApiKind.GetRequiredSection &&
            directInvocation.KeyExpression is { } requiredSectionKey)
        {
            return TryGetConfigurationSectionPath(
                directInvocation.Receiver,
                requiredSectionKey,
                semanticModel,
                out sectionPath,
                out sectionExpression,
                out sectionExpressionContainsFullPath);
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            invocation.ArgumentList.Arguments.Count == 0 ||
            !string.Equals(memberAccess.Name.Identifier.ValueText, "GetSection", StringComparison.Ordinal) ||
            !IsFrameworkConfigurationGetSectionInvocation(invocation, semanticModel))
        {
            return false;
        }

        var argumentExpression = invocation.ArgumentList.Arguments[0].Expression;
        if (!TryGetConstantSectionPath(argumentExpression, semanticModel, out var currentSectionPath))
        {
            return false;
        }

        if (TryGetConfigurationSectionPath(
                memberAccess.Expression,
                semanticModel,
                out var parentSectionPath,
                out _,
                out _))
        {
            sectionPath = parentSectionPath + ":" + currentSectionPath;
            sectionExpression = argumentExpression;
            sectionExpressionContainsFullPath = false;
            return true;
        }

        var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
        if (IsConfigurationSectionType(receiverType))
        {
            // The receiver is itself a stored/received IConfigurationSection (a local,
            // parameter, or other expression typed as a section rather than the
            // configuration root). Its own section path isn't a constant we can see
            // here, so treating the chained literal as a root-anchored path would
            // both false-positive (the key may exist under the real nested path) and
            // false-negative (a typo could be checked against the wrong namespace).
            // Stay quiet, matching the existing "ignore a directly-passed stored
            // IConfigurationSection" behavior.
            return false;
        }

        if (!IsConfigurationType(receiverType))
        {
            return false;
        }

        sectionPath = currentSectionPath;
        sectionExpression = argumentExpression;
        sectionExpressionContainsFullPath = true;
        return true;
    }

    private static ExpressionSyntax UnwrapForSectionChainResolution(ExpressionSyntax expression)
    {
        while (true)
        {
            if (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
                continue;
            }

            if (expression is PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppressed)
            {
                expression = suppressed.Operand;
                continue;
            }

            return expression;
        }
    }

    /// <summary>
    /// Resolves a conditional-access `WhenNotNull` expression (the part after `?.`) the same way
    /// <see cref="TryGetConfigurationSectionPath"/> resolves a normal invocation chain, without
    /// constructing any new syntax nodes: a `?.`-bound <see cref="MemberBindingExpressionSyntax"/>
    /// implicitly receives <paramref name="conditionalReceiver"/> (the expression before `?.`),
    /// so it is resolved against that receiver directly instead of being treated as a detached
    /// invocation with no receiver. All nodes touched remain part of the original syntax tree.
    /// </summary>
    private static bool TryGetConfigurationSectionPathFromWhenNotNull(
        ExpressionSyntax whenNotNull,
        ExpressionSyntax conditionalReceiver,
        SemanticModel semanticModel,
        out string sectionPath,
        out ExpressionSyntax sectionExpression,
        out bool sectionExpressionContainsFullPath)
    {
        sectionPath = null!;
        sectionExpression = null!;
        sectionExpressionContainsFullPath = false;

        whenNotNull = UnwrapForSectionChainResolution(whenNotNull);

        if (whenNotNull is not InvocationExpressionSyntax invocation ||
            invocation.ArgumentList.Arguments.Count == 0)
        {
            return false;
        }

        string methodName;
        var isBoundToConditionalReceiver = false;
        ExpressionSyntax? innerWhenNotNull = null;

        switch (invocation.Expression)
        {
            case MemberBindingExpressionSyntax memberBinding:
                methodName = memberBinding.Name.Identifier.ValueText;
                isBoundToConditionalReceiver = true;
                break;
            case MemberAccessExpressionSyntax memberAccess:
                methodName = memberAccess.Name.Identifier.ValueText;
                innerWhenNotNull = memberAccess.Expression;
                break;
            default:
                return false;
        }

        if (string.Equals(methodName, "GetRequiredSection", StringComparison.Ordinal))
        {
            if (semanticModel.GetOperation(invocation) is not IInvocationOperation operation ||
                !TryNormalizeDirectConfigurationInvocation(operation, out var directInvocation) ||
                directInvocation.Kind != DirectConfigurationApiKind.GetRequiredSection)
            {
                return false;
            }
        }
        else if (!string.Equals(methodName, "GetSection", StringComparison.Ordinal) ||
                 !IsFrameworkConfigurationGetSectionInvocation(invocation, semanticModel))
        {
            return false;
        }

        var argumentExpression = invocation.ArgumentList.Arguments[0].Expression;
        if (!TryGetConstantSectionPath(argumentExpression, semanticModel, out var currentSectionPath))
        {
            return false;
        }

        if (isBoundToConditionalReceiver)
        {
            if (TryGetConfigurationSectionPath(
                    conditionalReceiver,
                    semanticModel,
                    out var parentSectionPath,
                    out _,
                    out _))
            {
                sectionPath = parentSectionPath + ":" + currentSectionPath;
                sectionExpression = argumentExpression;
                sectionExpressionContainsFullPath = false;
                return true;
            }

            var receiverType = semanticModel.GetTypeInfo(conditionalReceiver).Type;
            if (IsConfigurationSectionType(receiverType))
            {
                // See the matching comment in TryGetConfigurationSectionPath: the receiver is
                // itself a stored/received IConfigurationSection, so its own path isn't a
                // constant we can see here. Stay quiet rather than checking against the wrong
                // namespace.
                return false;
            }

            if (!IsConfigurationType(receiverType))
            {
                return false;
            }

            sectionPath = currentSectionPath;
            sectionExpression = argumentExpression;
            sectionExpressionContainsFullPath = true;
            return true;
        }

        if (innerWhenNotNull is not null &&
            TryGetConfigurationSectionPathFromWhenNotNull(
                innerWhenNotNull,
                conditionalReceiver,
                semanticModel,
                out var innerParentSectionPath,
                out _,
                out _))
        {
            sectionPath = innerParentSectionPath + ":" + currentSectionPath;
            sectionExpression = argumentExpression;
            sectionExpressionContainsFullPath = false;
            return true;
        }

        return false;
    }

    private static bool IsConfigurationSectionMethodName(string methodName)
    {
        return string.Equals(methodName, "GetSection", StringComparison.Ordinal) ||
               string.Equals(methodName, "GetRequiredSection", StringComparison.Ordinal);
    }

    private static bool IsConfigurationSectionType(ITypeSymbol? type)
    {
        return IsOrImplements(type, "Microsoft.Extensions.Configuration.IConfigurationSection");
    }

    private static bool IsConfigurationType(ITypeSymbol? type)
    {
        return IsOrImplements(type, "Microsoft.Extensions.Configuration.IConfiguration");
    }

    private static bool IsOrImplements(ITypeSymbol? type, string interfaceDisplayName)
    {
        if (type is null)
        {
            return false;
        }

        if (string.Equals(GetNonNullableDisplayString(type), interfaceDisplayName, StringComparison.Ordinal))
        {
            return true;
        }

        if (type is INamedTypeSymbol namedType)
        {
            foreach (var iface in namedType.AllInterfaces)
            {
                if (string.Equals(GetNonNullableDisplayString(iface), interfaceDisplayName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string GetNonNullableDisplayString(ITypeSymbol type)
    {
        // ToDisplayString() appends a "?" for a nullable-annotated reference type
        // (e.g. "IConfigurationSection?"), which would otherwise break an exact
        // fully-qualified-name comparison for a nullable-annotated receiver.
        var normalized = type.NullableAnnotation == NullableAnnotation.None
            ? type
            : type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        return normalized.ToDisplayString();
    }

    private static bool IsOptionsConfigurationConfigureMethod(IMethodSymbol method)
    {
        var original = method.ReducedFrom ?? method;
        return string.Equals(original.ContainingType.ToDisplayString(), "Microsoft.Extensions.DependencyInjection.OptionsConfigurationServiceCollectionExtensions", StringComparison.Ordinal);
    }

    private static bool IsOptionsBuilderConfigurationMethod(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        string methodName)
    {
        var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var original = symbol?.ReducedFrom ?? symbol;
        return original is not null &&
               string.Equals(original.Name, methodName, StringComparison.Ordinal) &&
               string.Equals(original.ContainingType.ToDisplayString(), "Microsoft.Extensions.DependencyInjection.OptionsBuilderConfigurationExtensions", StringComparison.Ordinal);
    }

    private static bool IsValidationMethod(string methodName)
    {
        return string.Equals(methodName, "ValidateDataAnnotations", StringComparison.Ordinal) ||
               string.Equals(methodName, "Validate", StringComparison.Ordinal);
    }

    private static string? FindClosest(string value, ImmutableArray<string> candidates)
    {
        string? best = null;
        var bestDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            var distance = LevenshteinDistance(value, candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return bestDistance <= Math.Max(2, value.Length / 3) ? best : null;
    }

    private static int LevenshteinDistance(string left, string right)
    {
        if (left.Length == 0)
        {
            return right.Length;
        }

        if (right.Length == 0)
        {
            return left.Length;
        }

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var i = 0; i <= right.Length; i++)
        {
            previous[i] = i;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = char.ToUpperInvariant(left[i - 1]) == char.ToUpperInvariant(right[j - 1]) ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            var temp = previous;
            previous = current;
            current = temp;
        }

        return previous[right.Length];
    }

    private sealed class OptionsRegistration
    {
        public OptionsRegistration(
            INamedTypeSymbol optionsType,
            string sectionPath,
            ExpressionSyntax sectionExpression,
            InvocationExpressionSyntax outermostInvocation,
            bool supportsValidationRules,
            bool sectionExpressionContainsFullPath,
            bool hasValidateDataAnnotations,
            bool hasValidateOnStart,
            bool hasValidation,
            bool bindsNonPublicProperties,
            bool errorsOnUnknownConfiguration,
            bool isDataAnnotationsEnabled,
            Location bindLocation,
            bool requiresRuntimeSection)
        {
            OptionsType = optionsType;
            SectionPath = sectionPath;
            SectionExpression = sectionExpression;
            OutermostInvocation = outermostInvocation;
            SupportsValidationRules = supportsValidationRules;
            SectionExpressionContainsFullPath = sectionExpressionContainsFullPath;
            HasValidateDataAnnotations = hasValidateDataAnnotations;
            HasValidateOnStart = hasValidateOnStart;
            HasValidation = hasValidation;
            BindsNonPublicProperties = bindsNonPublicProperties;
            ErrorsOnUnknownConfiguration = errorsOnUnknownConfiguration;
            IsDataAnnotationsEnabled = isDataAnnotationsEnabled;
            BindLocation = bindLocation;
            RequiresRuntimeSection = requiresRuntimeSection;
        }

        public INamedTypeSymbol OptionsType { get; }
        public string SectionPath { get; }
        public ExpressionSyntax SectionExpression { get; }
        public InvocationExpressionSyntax OutermostInvocation { get; }
        public bool SupportsValidationRules { get; }
        public bool SectionExpressionContainsFullPath { get; }
        public bool HasValidateDataAnnotations { get; }
        public bool HasValidateOnStart { get; }
        public bool HasValidation { get; }
        public bool BindsNonPublicProperties { get; }
        public bool ErrorsOnUnknownConfiguration { get; }
        public bool IsDataAnnotationsEnabled { get; }
        public Location BindLocation { get; }
        public bool RequiresRuntimeSection { get; }
    }

    private sealed class InvocationChain
    {
        private InvocationChain(InvocationExpressionSyntax outermostInvocation, ImmutableHashSet<string> methodNames)
        {
            OutermostInvocation = outermostInvocation;
            MethodNames = methodNames;
        }

        public InvocationExpressionSyntax OutermostInvocation { get; }
        public ImmutableHashSet<string> MethodNames { get; }

        public static InvocationChain Create(InvocationExpressionSyntax bindInvocation, SemanticModel semanticModel, string bindMethodName)
        {
            var methods = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            methods.Add(bindMethodName);
            AddReceiverInvocations(bindInvocation, semanticModel, methods);

            var current = bindInvocation;
            var outermost = bindInvocation;

            while (current.Parent is MemberAccessExpressionSyntax memberAccess &&
                   memberAccess.Expression == current &&
                   memberAccess.Parent is InvocationExpressionSyntax nextInvocation)
            {
                AddRecognizedOptionsBuilderMethod(nextInvocation, semanticModel, methods);
                outermost = nextInvocation;
                current = nextInvocation;
            }

            AddSubsequentLocalInvocations(bindInvocation, semanticModel, methods);

            return new InvocationChain(outermost, methods.ToImmutable());
        }

        private static void AddReceiverInvocations(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            ImmutableHashSet<string>.Builder methods)
        {
            var current = invocation;
            while (current.Expression is MemberAccessExpressionSyntax memberAccess &&
                   memberAccess.Expression is InvocationExpressionSyntax receiverInvocation &&
                   receiverInvocation.Expression is MemberAccessExpressionSyntax)
            {
                AddRecognizedOptionsBuilderMethod(receiverInvocation, semanticModel, methods);
                current = receiverInvocation;
            }
        }

        private static void AddSubsequentLocalInvocations(
            InvocationExpressionSyntax bindInvocation,
            SemanticModel semanticModel,
            ImmutableHashSet<string>.Builder methods)
        {
            var declarator = bindInvocation.FirstAncestorOrSelf<VariableDeclaratorSyntax>();
            if (declarator?.Initializer?.Value is not null &&
                declarator.Initializer.Value.Span.Contains(bindInvocation.Span) &&
                declarator.Parent?.Parent is LocalDeclarationStatementSyntax declarationStatement &&
                declarationStatement.Parent is BlockSyntax declarationBlock &&
                semanticModel.GetDeclaredSymbol(declarator) is ILocalSymbol declaredLocalSymbol)
            {
                AddSubsequentLocalInvocations(
                    declarationBlock,
                    declarationBlock.Statements.IndexOf(declarationStatement) + 1,
                    declaredLocalSymbol,
                    semanticModel,
                    methods);
                return;
            }

            // The split-statement receiver may be a local variable or a method parameter of the
            // enclosing method — both name a single OptionsBuilder<T> instance whose bind and
            // validation calls can be tracked across separate statements the same way.
            if (bindInvocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol is not { } receiverLocalSymbol ||
                receiverLocalSymbol is not (ILocalSymbol or IParameterSymbol) ||
                bindInvocation.FirstAncestorOrSelf<ExpressionStatementSyntax>() is not ExpressionStatementSyntax expressionStatement ||
                !expressionStatement.Expression.Span.Contains(bindInvocation.Span) ||
                expressionStatement.Parent is not BlockSyntax expressionBlock)
            {
                return;
            }

            var expressionIndex = expressionBlock.Statements.IndexOf(expressionStatement);
            AddPreviousLocalInvocations(
                expressionBlock,
                expressionIndex - 1,
                receiverLocalSymbol,
                semanticModel,
                methods);

            AddSubsequentLocalInvocations(
                expressionBlock,
                expressionIndex + 1,
                receiverLocalSymbol,
                semanticModel,
                methods);
        }

        private static void AddPreviousLocalInvocations(
            BlockSyntax block,
            int startIndex,
            ISymbol localSymbol,
            SemanticModel semanticModel,
            ImmutableHashSet<string>.Builder methods)
        {
            // Walk backward from just before the bind, collecting validation/startup calls on the
            // tracked builder and skipping intervening statements, so a validation call placed
            // before the bind but separated from it by an unrelated statement is still recognized.
            // Unlike the forward scan, control flow does NOT stop the backward scan: a top-level
            // statement before the bind always executes before the bind is reached, so an earlier
            // unconditional validation call still applies (a conditionally-executed validation call
            // lives inside the control-flow statement, not as a top-level statement, and so is not
            // collected). The scan stops only at the builder's own declaration (its origin) or at a
            // statement that retargets the builder (earlier calls then belong to a different
            // OptionsBuilder instance) — including a conditional reassignment nested inside a
            // control-flow statement, which StatementRetargetsLocal detects.
            for (var i = startIndex; i >= 0; i--)
            {
                var statement = block.Statements[i];

                // A matching invocation chain on the tracked builder — collect and keep scanning.
                if (statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation } &&
                    TryCollectLocalInvocationChain(invocation, localSymbol, semanticModel, methods))
                {
                    continue;
                }

                // The builder's own local declaration (with a recognized initializer chain) — collect
                // it and stop, since nothing before the declaration can belong to this builder.
                if (statement is LocalDeclarationStatementSyntax declarationStatement &&
                    TryAddMatchingLocalDeclarationInitializer(declarationStatement, localSymbol, semanticModel, methods))
                {
                    return;
                }

                // Retargeting the builder before this point means earlier calls belong to a
                // different OptionsBuilder instance, so stop.
                if (StatementRetargetsLocal(statement, localSymbol, semanticModel))
                {
                    return;
                }

                // A labelled statement is a jump target: control can reach the bind via a `goto`
                // that skips the statements before the label, so source order no longer proves the
                // earlier validation ran. Stop conservatively.
                if (statement is LabeledStatementSyntax)
                {
                    return;
                }

                // Any other statement is inert with respect to this registration; keep scanning.
            }
        }

        private static bool TryAddMatchingLocalDeclarationInitializer(
            LocalDeclarationStatementSyntax declarationStatement,
            ISymbol localSymbol,
            SemanticModel semanticModel,
            ImmutableHashSet<string>.Builder methods)
        {
            foreach (var declarator in declarationStatement.Declaration.Variables)
            {
                if (semanticModel.GetDeclaredSymbol(declarator) is not ILocalSymbol declaredLocal ||
                    !SymbolEqualityComparer.Default.Equals(declaredLocal, localSymbol))
                {
                    continue;
                }

                if (declarator.Initializer?.Value is InvocationExpressionSyntax initializerInvocation)
                {
                    AddRecognizedInitializerInvocation(initializerInvocation, semanticModel, methods);
                }

                // This declaration declares the tracked builder (whether or not its initializer is a
                // recognized chain), so the backward scan has reached the builder's origin.
                return true;
            }

            return false;
        }

        private static void AddRecognizedInitializerInvocation(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            ImmutableHashSet<string>.Builder methods)
        {
            var current = invocation;
            while (true)
            {
                AddRecognizedOptionsBuilderMethod(current, semanticModel, methods);
                if (IsAddOptionsWithValidateOnStart(current, semanticModel))
                {
                    methods.Add("ValidateOnStart");
                }

                if (current.Expression is not MemberAccessExpressionSyntax memberAccess ||
                    memberAccess.Expression is not InvocationExpressionSyntax receiverInvocation ||
                    receiverInvocation.Expression is not MemberAccessExpressionSyntax)
                {
                    return;
                }

                current = receiverInvocation;
            }
        }

        private static void AddSubsequentLocalInvocations(
            BlockSyntax block,
            int startIndex,
            ISymbol localSymbol,
            SemanticModel semanticModel,
            ImmutableHashSet<string>.Builder methods)
        {
            for (var i = startIndex; i < block.Statements.Count; i++)
            {
                var statement = block.Statements[i];
                if (statement is ExpressionStatementSyntax expressionStatement &&
                    expressionStatement.Expression is InvocationExpressionSyntax invocation &&
                    TryCollectLocalInvocationChain(invocation, localSymbol, semanticModel, methods))
                {
                    continue;
                }

                // Retargeting the tracked builder local — a direct reassignment or
                // passing it by ref/out to a call that may reassign it — repoints it at
                // a different OptionsBuilder instance, so later calls on it (e.g. a
                // genuine ValidateOnStart()) no longer belong to this registration. Stop
                // the scan conservatively rather than attribute them to the wrong builder.
                if (StatementRetargetsLocal(statement, localSymbol, semanticModel))
                {
                    break;
                }

                // Only skip statements that cannot change reachability or retarget the
                // builder: an interleaved expression statement (an unrelated service
                // registration, or a call/assignment on another variable) or a local
                // declaration. Any other statement — control flow such as
                // if/return/throw/loops/switch, a nested block, using/lock, etc. — can
                // prevent a later validation call from executing on every path, so stop
                // the scan instead of assuming the later call always runs.
                if (statement is not ExpressionStatementSyntax &&
                    statement is not LocalDeclarationStatementSyntax)
                {
                    break;
                }

                // Otherwise the statement is inert with respect to this registration;
                // keep scanning so a validation call split across it still applies to
                // this same builder local.
            }
        }

        private static bool StatementRetargetsLocal(
            StatementSyntax statement,
            ISymbol localSymbol,
            SemanticModel semanticModel)
        {
            // Do not descend into lambda or local-function bodies: an assignment there
            // is deferred until the delegate is invoked, so it does not retarget the
            // local at this point in the straight-line flow.
            foreach (var node in statement.DescendantNodesAndSelf(ShouldDescendIntoBinderOptionsNode))
            {
                switch (node)
                {
                    // Direct assignment or a tuple-deconstruction assignment whose left
                    // side (possibly nested) targets the local.
                    case AssignmentExpressionSyntax assignment
                        when AssignmentTargetsLocal(assignment.Left, localSymbol, semanticModel):
                        return true;

                    // A ref/out argument lets the callee reassign the local; `in` is
                    // read-only and cannot, so it is intentionally not treated as a retarget.
                    case ArgumentSyntax argument
                        when (argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
                              argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)) &&
                             IsLocalReference(argument.Expression, localSymbol, semanticModel):
                        return true;
                }
            }

            return false;
        }

        private static bool AssignmentTargetsLocal(
            ExpressionSyntax left,
            ISymbol localSymbol,
            SemanticModel semanticModel)
        {
            if (IsLocalReference(left, localSymbol, semanticModel))
            {
                return true;
            }

            // Deconstruction assignment: the left is a tuple whose element expressions
            // are themselves assignment targets (and may be further nested tuples).
            if (left is TupleExpressionSyntax tuple)
            {
                foreach (var argument in tuple.Arguments)
                {
                    if (AssignmentTargetsLocal(argument.Expression, localSymbol, semanticModel))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryCollectLocalInvocationChain(
            InvocationExpressionSyntax invocation,
            ISymbol localSymbol,
            SemanticModel semanticModel,
            ImmutableHashSet<string>.Builder methods)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (IsLocalReference(memberAccess.Expression, localSymbol, semanticModel))
            {
                AddRecognizedOptionsBuilderMethod(invocation, semanticModel, methods);
                return true;
            }

            if (memberAccess.Expression is not InvocationExpressionSyntax receiverInvocation ||
                !TryCollectLocalInvocationChain(receiverInvocation, localSymbol, semanticModel, methods))
            {
                return false;
            }

            AddRecognizedOptionsBuilderMethod(invocation, semanticModel, methods);
            return true;
        }

        private static void AddRecognizedOptionsBuilderMethod(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            ImmutableHashSet<string>.Builder methods)
        {
            var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            var original = symbol?.ReducedFrom ?? symbol;
            if (original is null)
            {
                return;
            }

            if (IsOptionsBuilderValidateMethod(original))
            {
                methods.Add("Validate");
                return;
            }

            if (IsOptionsBuilderValidateOnStartMethod(original))
            {
                methods.Add("ValidateOnStart");
                return;
            }

            if (IsOptionsBuilderValidateDataAnnotationsMethod(original))
            {
                methods.Add("ValidateDataAnnotations");
            }
        }

        private static bool IsOptionsBuilderValidateMethod(IMethodSymbol method)
        {
            return string.Equals(method.Name, "Validate", StringComparison.Ordinal) &&
                   method.ContainingType.Name == "OptionsBuilder" &&
                   method.ContainingType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.Options";
        }

        private static bool IsOptionsBuilderValidateOnStartMethod(IMethodSymbol method)
        {
            return string.Equals(method.Name, "ValidateOnStart", StringComparison.Ordinal) &&
                   string.Equals(method.ContainingType.ToDisplayString(), "Microsoft.Extensions.DependencyInjection.OptionsBuilderExtensions", StringComparison.Ordinal);
        }

        private static bool IsOptionsBuilderValidateDataAnnotationsMethod(IMethodSymbol method)
        {
            return string.Equals(method.Name, "ValidateDataAnnotations", StringComparison.Ordinal) &&
                   string.Equals(method.ContainingType.ToDisplayString(), "Microsoft.Extensions.DependencyInjection.OptionsBuilderDataAnnotationsExtensions", StringComparison.Ordinal);
        }

        private static bool IsLocalReference(
            ExpressionSyntax expression,
            ISymbol localSymbol,
            SemanticModel semanticModel)
        {
            return expression is IdentifierNameSyntax identifier &&
                   string.Equals(identifier.Identifier.ValueText, localSymbol.Name, StringComparison.Ordinal) &&
                   SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(identifier).Symbol, localSymbol);
        }
    }
}
