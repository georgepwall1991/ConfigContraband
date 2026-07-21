using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ConfigContraband;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class ConfigContrabandAnalyzer : DiagnosticAnalyzer
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
                            if (TryCreateStoredSectionOriginRegistration(
                                    invocation,
                                    syntaxContext.SemanticModel,
                                    out var storedSectionRegistration))
                            {
                                AnalyzeConfigurationSection(
                                    syntaxContext.ReportDiagnostic,
                                    storedSectionRegistration,
                                    configuration,
                                    providerSemantics);
                            }
                            else
                            {
                                AnalyzeDirectConfigurationRead(
                                    syntaxContext,
                                    configuration,
                                    providerSemantics,
                                    unknownKeysReported);
                            }
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
