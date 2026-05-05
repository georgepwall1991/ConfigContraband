using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace ConfigContraband;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ConfigContrabandCodeFixProvider)), Shared]
public sealed class ConfigContrabandCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(
        DiagnosticIds.MissingConfigurationSection,
        DiagnosticIds.ValidationNotOnStart,
        DiagnosticIds.DataAnnotationsNotEnabled,
        DiagnosticIds.NestedValidationNotRecursive);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            switch (diagnostic.Id)
            {
                case DiagnosticIds.MissingConfigurationSection:
                    RegisterMissingSectionFix(context, diagnostic);
                    break;

                case DiagnosticIds.ValidationNotOnStart:
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            "Add ValidateOnStart()",
                            cancellationToken => AppendInvocationsAsync(context.Document, diagnostic.Location.SourceSpan, new[] { "ValidateOnStart" }, cancellationToken),
                            equivalenceKey: "AddValidateOnStart"),
                        diagnostic);
                    break;

                case DiagnosticIds.DataAnnotationsNotEnabled:
                    var methods = diagnostic.Properties.TryGetValue(ConfigContrabandAnalyzer.HasValidateOnStartPropertyName, out var hasValidateOnStart) &&
                        string.Equals(hasValidateOnStart, "true", StringComparison.Ordinal)
                            ? new[] { "ValidateDataAnnotations" }
                            : new[] { "ValidateDataAnnotations", "ValidateOnStart" };

                    context.RegisterCodeFix(
                        CodeAction.Create(
                            "Add ValidateDataAnnotations()",
                            cancellationToken => AppendInvocationsAsync(context.Document, diagnostic.Location.SourceSpan, methods, cancellationToken),
                            equivalenceKey: "AddValidateDataAnnotations"),
                        diagnostic);
                    break;

                case DiagnosticIds.NestedValidationNotRecursive:
                    if (!diagnostic.Properties.TryGetValue(ConfigContrabandAnalyzer.RecursiveAttributePropertyName, out var attributeName) ||
                        string.IsNullOrWhiteSpace(attributeName))
                    {
                        break;
                    }

                    context.RegisterCodeFix(
                        CodeAction.Create(
                            $"Add [{attributeName}]",
                            cancellationToken => AddRecursiveValidationAttributeAsync(context.Document, diagnostic, attributeName!, cancellationToken),
                            equivalenceKey: "AddRecursiveValidationAttribute"),
                        diagnostic);
                    break;
            }
        }

        return Task.CompletedTask;
    }

    private static void RegisterMissingSectionFix(CodeFixContext context, Diagnostic diagnostic)
    {
        if (!diagnostic.Properties.TryGetValue(ConfigContrabandAnalyzer.SuggestedSectionPropertyName, out var suggestion) ||
            string.IsNullOrWhiteSpace(suggestion))
        {
            return;
        }

        var replacement = diagnostic.Properties.TryGetValue(ConfigContrabandAnalyzer.SuggestedSectionReplacementPropertyName, out var suggestedReplacement) &&
            !string.IsNullOrWhiteSpace(suggestedReplacement)
                ? suggestedReplacement!
                : suggestion!;

        context.RegisterCodeFix(
            CodeAction.Create(
                $"Use \"{suggestion}\"",
                cancellationToken => ReplaceSectionAsync(context.Document, diagnostic.Location.SourceSpan, replacement, cancellationToken),
                equivalenceKey: "UseSuggestedSection"),
            diagnostic);
    }

    private static async Task<Document> ReplaceSectionAsync(
        Document document,
        TextSpan span,
        string suggestion,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var node = root.FindNode(span, getInnermostNodeForTie: true);
        var expression = node as ExpressionSyntax ?? node.FirstAncestorOrSelf<ExpressionSyntax>();
        if (expression is null)
        {
            return document;
        }

        var replacement = SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                CreateReplacementStringLiteral(expression, suggestion))
            .WithTriviaFrom(expression);

        return document.WithSyntaxRoot(root.ReplaceNode(expression, replacement));
    }

    private static SyntaxToken CreateReplacementStringLiteral(ExpressionSyntax expression, string suggestion)
    {
        if (expression is not LiteralExpressionSyntax literalExpression ||
            !literalExpression.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return SyntaxFactory.Literal(suggestion);
        }

        var tokenText = literalExpression.Token.Text;
        if (tokenText.StartsWith("@\"", StringComparison.Ordinal))
        {
            return SyntaxFactory.Literal("@\"" + suggestion.Replace("\"", "\"\"") + "\"", suggestion);
        }

        if (TryGetRawStringDelimiterLength(tokenText, out var delimiterLength))
        {
            if (suggestion.Contains('\n') || suggestion.Contains('\r'))
            {
                return SyntaxFactory.Literal(suggestion);
            }

            var delimiter = new string('"', Math.Max(delimiterLength, LongestQuoteRun(suggestion) + 1));
            return SyntaxFactory.Token(
                SyntaxTriviaList.Empty,
                SyntaxKind.SingleLineRawStringLiteralToken,
                delimiter + suggestion + delimiter,
                suggestion,
                SyntaxTriviaList.Empty);
        }

        return SyntaxFactory.Literal(suggestion);
    }

    private static bool TryGetRawStringDelimiterLength(string tokenText, out int delimiterLength)
    {
        delimiterLength = 0;
        while (delimiterLength < tokenText.Length && tokenText[delimiterLength] == '"')
        {
            delimiterLength++;
        }

        return delimiterLength >= 3;
    }

    private static int LongestQuoteRun(string value)
    {
        var longest = 0;
        var current = 0;
        foreach (var ch in value)
        {
            if (ch == '"')
            {
                current++;
                longest = Math.Max(longest, current);
                continue;
            }

            current = 0;
        }

        return longest;
    }

    private static async Task<Document> AppendInvocationsAsync(
        Document document,
        TextSpan span,
        string[] methodNames,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var invocation = root.FindNode(span).FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
        {
            return document;
        }

        var outermost = FindOutermostInvocation(invocation);
        var dotToken = CreateAppendedInvocationDotToken(outermost);

        ExpressionSyntax replacement = outermost
            .WithLeadingTrivia(SyntaxTriviaList.Empty)
            .WithTrailingTrivia(SyntaxTriviaList.Empty);
        foreach (var methodName in methodNames)
        {
            replacement = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    replacement,
                    dotToken,
                    SyntaxFactory.IdentifierName(methodName)),
                SyntaxFactory.ArgumentList());
        }

        replacement = replacement
            .WithTriviaFrom(outermost)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(outermost, replacement));
    }

    private static SyntaxToken CreateAppendedInvocationDotToken(InvocationExpressionSyntax outermost)
    {
        var memberAccess = (MemberAccessExpressionSyntax)outermost.Expression;
        var text = outermost.SyntaxTree.GetText();
        var line = text.Lines.GetLineFromPosition(memberAccess.OperatorToken.SpanStart);
        var indentation = text.ToString(TextSpan.FromBounds(line.Start, memberAccess.OperatorToken.SpanStart));
        if (indentation.All(char.IsWhiteSpace))
        {
            return SyntaxFactory.Token(
                SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine("\n"), SyntaxFactory.Whitespace(indentation)),
                SyntaxKind.DotToken,
                SyntaxTriviaList.Empty);
        }

        return SyntaxFactory.Token(SyntaxKind.DotToken);
    }

    private static InvocationExpressionSyntax FindOutermostInvocation(InvocationExpressionSyntax invocation)
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

    private static async Task<Solution> AddRecursiveValidationAttributeAsync(
        Document document,
        Diagnostic diagnostic,
        string attributeName,
        CancellationToken cancellationToken)
    {
        var location = diagnostic.AdditionalLocations.FirstOrDefault() ?? diagnostic.Location;
        var targetDocument = location.SourceTree is null
            ? document
            : document.Project.Solution.GetDocument(location.SourceTree) ?? document;

        var root = await targetDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document.Project.Solution;
        }

        var targetNode = root.FindNode(location.SourceSpan, getInnermostNodeForTie: true);

        var semanticModel = await targetDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var targetPosition = targetNode.SpanStart;
        var attributeSyntaxName = ShouldFullyQualifyRecursiveAttribute(targetPosition, semanticModel, attributeName)
            ? $"global::Microsoft.Extensions.Options.{attributeName}Attribute"
            : attributeName;

        if (targetNode.FirstAncestorOrSelf<PropertyDeclarationSyntax>() is { } property)
        {
            return AddRecursiveValidationAttributeToProperty(
                targetDocument,
                root,
                property,
                attributeSyntaxName,
                attributeName);
        }

        if (targetNode.FirstAncestorOrSelf<ParameterSyntax>() is { } parameter)
        {
            return AddRecursiveValidationAttributeToParameter(
                targetDocument,
                root,
                parameter,
                attributeSyntaxName,
                attributeName);
        }

        return document.Project.Solution;
    }

    private static Solution AddRecursiveValidationAttributeToProperty(
        Document targetDocument,
        SyntaxNode root,
        PropertyDeclarationSyntax property,
        string attributeSyntaxName,
        string attributeName)
    {
        var propertyLeadingTrivia = property.GetLeadingTrivia();
        var attributeList = SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(SyntaxFactory.ParseName(attributeSyntaxName))))
            .WithLeadingTrivia(propertyLeadingTrivia)
            .WithTrailingTrivia(SyntaxFactory.ElasticLineFeed);

        var updatedRoot = root.TrackNodes(property);
        if (attributeSyntaxName == attributeName)
        {
            updatedRoot = EnsureUsing(
                updatedRoot,
                property,
                "Microsoft.Extensions.Options");
        }

        var currentProperty = updatedRoot.GetCurrentNode(property);
        if (currentProperty is null)
        {
            return targetDocument.Project.Solution.WithDocumentSyntaxRoot(targetDocument.Id, updatedRoot);
        }

        var updatedProperty = currentProperty
            .WithLeadingTrivia(SyntaxTriviaList.Empty)
            .WithAttributeLists(currentProperty.AttributeLists.Insert(0, attributeList))
            .WithAdditionalAnnotations(Formatter.Annotation);

        return targetDocument.Project.Solution.WithDocumentSyntaxRoot(
            targetDocument.Id,
            updatedRoot.ReplaceNode(currentProperty, updatedProperty));
    }

    private static Solution AddRecursiveValidationAttributeToParameter(
        Document targetDocument,
        SyntaxNode root,
        ParameterSyntax parameter,
        string attributeSyntaxName,
        string attributeName)
    {
        var attributeList = SyntaxFactory.AttributeList(
                SyntaxFactory.AttributeTargetSpecifier(SyntaxFactory.Token(SyntaxKind.PropertyKeyword)),
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(SyntaxFactory.ParseName(attributeSyntaxName))))
            .WithTrailingTrivia(SyntaxFactory.Space);

        var updatedRoot = root.TrackNodes(parameter);
        if (attributeSyntaxName == attributeName)
        {
            updatedRoot = EnsureUsing(
                updatedRoot,
                parameter,
                "Microsoft.Extensions.Options");
        }

        var currentParameter = updatedRoot.GetCurrentNode(parameter);
        if (currentParameter is null)
        {
            return targetDocument.Project.Solution.WithDocumentSyntaxRoot(targetDocument.Id, updatedRoot);
        }

        var updatedParameter = currentParameter
            .WithAttributeLists(currentParameter.AttributeLists.Insert(0, attributeList))
            .WithAdditionalAnnotations(Formatter.Annotation);

        return targetDocument.Project.Solution.WithDocumentSyntaxRoot(
            targetDocument.Id,
            updatedRoot.ReplaceNode(currentParameter, updatedParameter));
    }

    private static bool ShouldFullyQualifyRecursiveAttribute(
        int position,
        SemanticModel? semanticModel,
        string attributeName)
    {
        if (semanticModel is null)
        {
            return false;
        }

        var expectedMetadataName = $"Microsoft.Extensions.Options.{attributeName}Attribute";
        return HasConflictingTypeInScope(semanticModel, position, attributeName, expectedMetadataName) ||
               HasConflictingTypeInScope(semanticModel, position, attributeName + "Attribute", expectedMetadataName);
    }

    private static bool HasConflictingTypeInScope(
        SemanticModel semanticModel,
        int position,
        string name,
        string expectedMetadataName)
    {
        return semanticModel
            .LookupSymbols(position, name: name)
            .OfType<INamedTypeSymbol>()
            .Any(symbol => !string.Equals(symbol.ToDisplayString(), expectedMetadataName, StringComparison.Ordinal));
    }

    private static SyntaxNode EnsureUsing(
        SyntaxNode root,
        PropertyDeclarationSyntax property,
        string namespaceName)
    {
        return EnsureUsing(root, (SyntaxNode)property, namespaceName);
    }

    private static SyntaxNode EnsureUsing(
        SyntaxNode root,
        ParameterSyntax parameter,
        string namespaceName)
    {
        return EnsureUsing(root, (SyntaxNode)parameter, namespaceName);
    }

    private static SyntaxNode EnsureUsing(
        SyntaxNode root,
        SyntaxNode target,
        string namespaceName)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
        {
            return root;
        }

        var currentTarget = root.GetCurrentNode(target);
        if (currentTarget is null)
        {
            return root;
        }

        var namespaceAncestors = currentTarget
            .Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .ToArray();

        if (HasUsing(compilationUnit.Usings, namespaceName) ||
            namespaceAncestors.Any(namespaceDeclaration => HasUsing(namespaceDeclaration.Usings, namespaceName)))
        {
            return root;
        }

        var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
            .WithTrailingTrivia(SyntaxFactory.ElasticLineFeed);

        var namespaceWithUsings = namespaceAncestors.FirstOrDefault(namespaceDeclaration => namespaceDeclaration.Usings.Count > 0);
        if (namespaceWithUsings is not null)
        {
            return root.ReplaceNode(
                namespaceWithUsings,
                namespaceWithUsings.WithUsings(namespaceWithUsings.Usings.Add(usingDirective)));
        }

        return compilationUnit.WithUsings(compilationUnit.Usings.Add(usingDirective));
    }

    private static bool HasUsing(SyntaxList<UsingDirectiveSyntax> usings, string namespaceName)
    {
        return usings.Any(usingDirective =>
            usingDirective.Alias is null &&
            !usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) &&
            string.Equals(usingDirective.Name?.ToString(), namespaceName, StringComparison.Ordinal));
    }
}
