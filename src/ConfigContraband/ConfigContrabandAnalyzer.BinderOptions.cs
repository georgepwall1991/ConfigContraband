using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConfigContraband;

public sealed partial class ConfigContrabandAnalyzer
{
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
                     .DescendantNodes(ExecutionScope.ShouldDescend)
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
                     .DescendantNodes(ExecutionScope.ShouldDescend)
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
                     .DescendantNodes(ExecutionScope.ShouldDescend)
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
        foreach (var descendant in node.DescendantNodesAndSelf(ExecutionScope.ShouldDescend))
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
}
