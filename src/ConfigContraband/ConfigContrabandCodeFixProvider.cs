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
                SyntaxFactory.Literal(suggestion))
            .WithTriviaFrom(expression);

        return document.WithSyntaxRoot(root.ReplaceNode(expression, replacement));
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

        var property = root.FindNode(location.SourceSpan).FirstAncestorOrSelf<PropertyDeclarationSyntax>();
        if (property is null)
        {
            return document.Project.Solution;
        }

        var propertyLeadingTrivia = property.GetLeadingTrivia();
        var attributeList = SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(SyntaxFactory.ParseName(attributeName))))
            .WithLeadingTrivia(propertyLeadingTrivia)
            .WithTrailingTrivia(SyntaxFactory.ElasticLineFeed);

        var updatedProperty = property
            .WithLeadingTrivia(SyntaxTriviaList.Empty)
            .WithAttributeLists(property.AttributeLists.Insert(0, attributeList))
            .WithAdditionalAnnotations(Formatter.Annotation);

        var updatedRoot = root.ReplaceNode(property, updatedProperty);
        updatedRoot = EnsureUsing(updatedRoot, "Microsoft.Extensions.Options");

        return targetDocument.Project.Solution.WithDocumentSyntaxRoot(targetDocument.Id, updatedRoot);
    }

    private static SyntaxNode EnsureUsing(SyntaxNode root, string namespaceName)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
        {
            return root;
        }

        if (compilationUnit.Usings.Any(usingDirective =>
                string.Equals(usingDirective.Name?.ToString(), namespaceName, StringComparison.Ordinal)))
        {
            return root;
        }

        var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
            .WithTrailingTrivia(SyntaxFactory.ElasticLineFeed);

        return compilationUnit.WithUsings(compilationUnit.Usings.Add(usingDirective));
    }
}
