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
public sealed class ConfigContrabandAnalyzer : DiagnosticAnalyzer
{
    internal const string SuggestedSectionPropertyName = "SuggestedSection";
    internal const string SuggestedSectionReplacementPropertyName = "SuggestedSectionReplacement";
    internal const string HasValidateOnStartPropertyName = "HasValidateOnStart";
    internal const string RecursiveAttributePropertyName = "RecursiveAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        DiagnosticDescriptors.MissingConfigurationSection,
        DiagnosticDescriptors.ValidationNotOnStart,
        DiagnosticDescriptors.DataAnnotationsNotEnabled,
        DiagnosticDescriptors.NestedValidationNotRecursive,
        DiagnosticDescriptors.UnknownConfigurationKey);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var configuration = ConfigurationSnapshot.Create(
                compilationContext.Options.AdditionalFiles,
                compilationContext.CancellationToken);
            var nestedValidationReported = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            var unknownKeysReported = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext =>
                {
                    var invocation = (InvocationExpressionSyntax)syntaxContext.Node;
                    if (!TryCreateRegistration(invocation, syntaxContext.SemanticModel, out var registration))
                    {
                        return;
                    }

                    AnalyzeRegistrationChain(syntaxContext.ReportDiagnostic, registration);
                    if (registration.SupportsValidationRules)
                    {
                        AnalyzeOptionType(syntaxContext.ReportDiagnostic, registration, nestedValidationReported);
                    }

                    if (configuration.HasFiles)
                    {
                        AnalyzeConfigurationSection(syntaxContext.ReportDiagnostic, registration, configuration);
                        AnalyzeUnknownKeys(syntaxContext.ReportDiagnostic, registration, configuration, unknownKeysReported);
                    }
                },
                SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeRegistrationChain(Action<Diagnostic> reportDiagnostic, OptionsRegistration registration)
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

        var metadata = OptionsTypeMetadata.Create(registration.OptionsType);
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
        ConcurrentDictionary<string, byte> nestedValidationReported)
    {
        var metadata = OptionsTypeMetadata.Create(registration.OptionsType);
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
        ConfigurationSnapshot configuration)
    {
        if (configuration.TryFindSection(registration.SectionPath, out _))
        {
            return;
        }

        var suggestion = FindClosest(registration.SectionPath.Split(':').Last(), configuration.GetSiblingSectionNames(registration.SectionPath));
        var suggestedSectionPath = suggestion is null ? null : ReplaceSectionLeaf(registration.SectionPath, suggestion);
        var suffix = suggestedSectionPath is null ? "." : $". Did you mean \"{suggestedSectionPath}\"?";
        var properties = ImmutableDictionary<string, string?>.Empty;
        if (suggestedSectionPath is not null)
        {
            var suggestedReplacement = registration.SectionExpressionContainsFullPath
                ? suggestedSectionPath
                : suggestion;
            properties = properties
                .Add(SuggestedSectionPropertyName, suggestedSectionPath)
                .Add(SuggestedSectionReplacementPropertyName, suggestedReplacement);
        }

        reportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.MissingConfigurationSection,
            registration.SectionExpression.GetLocation(),
            properties,
            registration.SectionPath,
            suffix));
    }

    private static string ReplaceSectionLeaf(string sectionPath, string replacement)
    {
        var separatorIndex = sectionPath.LastIndexOf(':');
        return separatorIndex < 0
            ? replacement
            : sectionPath.Substring(0, separatorIndex + 1) + replacement;
    }

    private static void AnalyzeUnknownKeys(
        Action<Diagnostic> reportDiagnostic,
        OptionsRegistration registration,
        ConfigurationSnapshot configuration,
        ConcurrentDictionary<string, byte> unknownKeysReported)
    {
        var sections = configuration.FindSections(registration.SectionPath);
        if (sections.IsDefaultOrEmpty)
        {
            return;
        }

        var metadata = OptionsTypeMetadata.Create(registration.OptionsType);
        foreach (var matchingSection in sections)
        {
            AnalyzeUnknownKeysInSection(
                reportDiagnostic,
                matchingSection,
                metadata,
                unknownKeysReported);
        }
    }

    private static void AnalyzeUnknownKeysInSection(
        Action<Diagnostic> reportDiagnostic,
        ConfigurationNode section,
        OptionsTypeMetadata metadata,
        ConcurrentDictionary<string, byte> unknownKeysReported)
    {
        var knownNames = metadata.GetConfigurationNames();
        foreach (var property in section.Properties)
        {
            if (!metadata.TryGetConfigurationProperty(property.Key, out var bindableProperty))
            {
                var reportKey = metadata.TypeKey + "|" + property.Location.GetLineSpan().Path + "|" + property.FullPath;
                if (!unknownKeysReported.TryAdd(reportKey, 0))
                {
                    continue;
                }

                var suggestion = FindClosest(property.Key, knownNames);
                var suffix = suggestion is null ? "." : $". Did you mean \"{suggestion}\"?";

                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.UnknownConfigurationKey,
                    property.Location,
                    property.FullPath,
                    metadata.TypeName,
                    suffix));

                continue;
            }

            if (property.Value.Properties.IsDefaultOrEmpty)
            {
                continue;
            }

            if (metadata.TryCreateNestedMetadata(bindableProperty, out var nestedMetadata))
            {
                AnalyzeUnknownKeysInSection(
                    reportDiagnostic,
                    property.Value,
                    nestedMetadata,
                    unknownKeysReported);
                continue;
            }

            if (metadata.TryCreateDictionaryValueMetadata(bindableProperty, out var dictionaryValueMetadata))
            {
                foreach (var entry in property.Value.Properties)
                {
                    if (!entry.Value.Properties.IsDefaultOrEmpty)
                    {
                        AnalyzeUnknownKeysInSection(
                            reportDiagnostic,
                            entry.Value,
                            dictionaryValueMetadata,
                            unknownKeysReported);
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
                                unknownKeysReported);
                        }
                    }
                }

                continue;
            }

            if (!metadata.TryCreateCollectionElementMetadata(bindableProperty, out var elementMetadata))
            {
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
                        unknownKeysReported);
                }
            }
        }
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

        registration = new OptionsRegistration(
            optionsType,
            sectionPath,
            sectionExpression,
            chain.OutermostInvocation,
            supportsValidationRules: true,
            sectionExpressionContainsFullPath,
            chain.MethodNames.Contains("ValidateDataAnnotations"),
            hasValidateOnStart,
            chain.MethodNames.Any(IsValidationMethod));
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

            registration = new OptionsRegistration(
                optionsType,
                sectionPath,
                sectionExpression,
                invocation,
                supportsValidationRules: false,
                sectionExpressionContainsFullPath,
                hasValidateDataAnnotations: false,
                hasValidateOnStart: false,
                hasValidation: false);
            return true;
        }

        return false;
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
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        out string sectionPath,
        out ExpressionSyntax sectionExpression,
        out bool sectionExpressionContainsFullPath)
    {
        sectionPath = null!;
        sectionExpression = null!;
        sectionExpressionContainsFullPath = false;

        if (expression is not InvocationExpressionSyntax invocation ||
            invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            invocation.ArgumentList.Arguments.Count == 0 ||
            !IsConfigurationSectionMethodName(memberAccess.Name.Identifier.ValueText))
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

        if (!IsConfigurationType(semanticModel.GetTypeInfo(memberAccess.Expression).Type))
        {
            return false;
        }

        sectionPath = currentSectionPath;
        sectionExpression = argumentExpression;
        sectionExpressionContainsFullPath = true;
        return true;
    }

    private static bool IsConfigurationSectionMethodName(string methodName)
    {
        return string.Equals(methodName, "GetSection", StringComparison.Ordinal) ||
               string.Equals(methodName, "GetRequiredSection", StringComparison.Ordinal);
    }

    private static bool IsConfigurationType(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        if (string.Equals(type.ToDisplayString(), "Microsoft.Extensions.Configuration.IConfiguration", StringComparison.Ordinal))
        {
            return true;
        }

        if (type is INamedTypeSymbol namedType)
        {
            foreach (var iface in namedType.AllInterfaces)
            {
                if (string.Equals(iface.ToDisplayString(), "Microsoft.Extensions.Configuration.IConfiguration", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
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
            bool hasValidation)
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
            AddReceiverInvocations(bindInvocation, methods);

            var current = bindInvocation;
            var outermost = bindInvocation;

            while (current.Parent is MemberAccessExpressionSyntax memberAccess &&
                   memberAccess.Expression == current &&
                   memberAccess.Parent is InvocationExpressionSyntax nextInvocation)
            {
                methods.Add(memberAccess.Name.Identifier.ValueText);
                outermost = nextInvocation;
                current = nextInvocation;
            }

            AddSubsequentLocalInvocations(bindInvocation, semanticModel, methods);

            return new InvocationChain(outermost, methods.ToImmutable());
        }

        private static void AddReceiverInvocations(
            InvocationExpressionSyntax invocation,
            ImmutableHashSet<string>.Builder methods)
        {
            var current = invocation;
            while (current.Expression is MemberAccessExpressionSyntax memberAccess &&
                   memberAccess.Expression is InvocationExpressionSyntax receiverInvocation &&
                   receiverInvocation.Expression is MemberAccessExpressionSyntax receiverMemberAccess)
            {
                methods.Add(receiverMemberAccess.Name.Identifier.ValueText);
                current = receiverInvocation;
            }
        }

        private static void AddSubsequentLocalInvocations(
            InvocationExpressionSyntax bindInvocation,
            SemanticModel semanticModel,
            ImmutableHashSet<string>.Builder methods)
        {
            var declarator = bindInvocation.FirstAncestorOrSelf<VariableDeclaratorSyntax>();
            if (declarator?.Initializer?.Value is null ||
                !declarator.Initializer.Value.Span.Contains(bindInvocation.Span) ||
                declarator.Parent?.Parent is not LocalDeclarationStatementSyntax declarationStatement ||
                declarationStatement.Parent is not BlockSyntax block ||
                semanticModel.GetDeclaredSymbol(declarator) is not ILocalSymbol localSymbol)
            {
                return;
            }

            var declarationIndex = block.Statements.IndexOf(declarationStatement);
            for (var i = declarationIndex + 1; i < block.Statements.Count; i++)
            {
                if (block.Statements[i] is not ExpressionStatementSyntax expressionStatement ||
                    expressionStatement.Expression is not InvocationExpressionSyntax invocation ||
                    !TryCollectLocalInvocationChain(invocation, localSymbol, semanticModel, methods))
                {
                    break;
                }
            }
        }

        private static bool TryCollectLocalInvocationChain(
            InvocationExpressionSyntax invocation,
            ILocalSymbol localSymbol,
            SemanticModel semanticModel,
            ImmutableHashSet<string>.Builder methods)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (IsLocalReference(memberAccess.Expression, localSymbol, semanticModel))
            {
                methods.Add(memberAccess.Name.Identifier.ValueText);
                return true;
            }

            if (memberAccess.Expression is not InvocationExpressionSyntax receiverInvocation ||
                !TryCollectLocalInvocationChain(receiverInvocation, localSymbol, semanticModel, methods))
            {
                return false;
            }

            methods.Add(memberAccess.Name.Identifier.ValueText);
            return true;
        }

        private static bool IsLocalReference(
            ExpressionSyntax expression,
            ILocalSymbol localSymbol,
            SemanticModel semanticModel)
        {
            return expression is IdentifierNameSyntax identifier &&
                   string.Equals(identifier.Identifier.ValueText, localSymbol.Name, StringComparison.Ordinal) &&
                   SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(identifier).Symbol, localSymbol);
        }
    }
}
