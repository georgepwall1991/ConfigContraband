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
                    AnalyzeOptionType(syntaxContext.ReportDiagnostic, registration, nestedValidationReported);

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
            properties = properties.Add(SuggestedSectionPropertyName, suggestedSectionPath);
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

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.ValueText != "BindConfiguration" ||
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

        var sectionExpression = invocation.ArgumentList.Arguments[0].Expression;
        var constant = semanticModel.GetConstantValue(sectionExpression);
        if (!constant.HasValue ||
            constant.Value is not string sectionPath ||
            string.IsNullOrWhiteSpace(sectionPath))
        {
            return false;
        }

        var chain = InvocationChain.Create(invocation, semanticModel);
        registration = new OptionsRegistration(
            optionsType,
            sectionPath,
            sectionExpression,
            chain.OutermostInvocation,
            chain.MethodNames.Contains("ValidateDataAnnotations"),
            chain.MethodNames.Contains("ValidateOnStart"),
            chain.MethodNames.Any(IsValidationMethod));
        return true;
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
            bool hasValidateDataAnnotations,
            bool hasValidateOnStart,
            bool hasValidation)
        {
            OptionsType = optionsType;
            SectionPath = sectionPath;
            SectionExpression = sectionExpression;
            OutermostInvocation = outermostInvocation;
            HasValidateDataAnnotations = hasValidateDataAnnotations;
            HasValidateOnStart = hasValidateOnStart;
            HasValidation = hasValidation;
        }

        public INamedTypeSymbol OptionsType { get; }
        public string SectionPath { get; }
        public ExpressionSyntax SectionExpression { get; }
        public InvocationExpressionSyntax OutermostInvocation { get; }
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

        public static InvocationChain Create(InvocationExpressionSyntax bindInvocation, SemanticModel semanticModel)
        {
            var methods = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            methods.Add("BindConfiguration");

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
