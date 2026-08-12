using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConfigContraband;

/// <summary>
/// Shared traversal guard for straight-line (same immediate execution scope) analyses. Lambda and
/// local-function bodies are deferred — they run when invoked, not where they are written — so any
/// analysis that tracks what executes at a point in the straight-line flow (binder-options escape
/// proofs, constructor-default proofs, configuration-origin provenance, local builder chains) must
/// not descend into them. The four former per-context descend predicates all reduced to this guard.
/// </summary>
internal static class ExecutionScope
{
    public static bool ShouldDescend(SyntaxNode node)
    {
        return node is not AnonymousFunctionExpressionSyntax and
               not LocalFunctionStatementSyntax;
    }

    public static bool IsUnconditionallyEvaluatedWithin(SyntaxNode candidate, SyntaxNode boundary)
    {
        foreach (var ancestor in candidate.Ancestors())
        {
            if (ancestor.Span == boundary.Span && ancestor.RawKind == boundary.RawKind)
            {
                return true;
            }

            if (ancestor is ConditionalExpressionSyntax or
                ConditionalAccessExpressionSyntax or
                SwitchExpressionSyntax)
            {
                return false;
            }

            if (ancestor is BinaryExpressionSyntax binary &&
                (binary.IsKind(SyntaxKind.LogicalAndExpression) ||
                 binary.IsKind(SyntaxKind.LogicalOrExpression) ||
                 binary.IsKind(SyntaxKind.CoalesceExpression)))
            {
                return false;
            }

            if (ancestor is AssignmentExpressionSyntax assignment &&
                assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression))
            {
                return false;
            }
        }

        return false;
    }
}
