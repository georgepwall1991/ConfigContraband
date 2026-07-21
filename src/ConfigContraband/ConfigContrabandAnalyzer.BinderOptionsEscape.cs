using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConfigContraband;

public sealed partial class ConfigContrabandAnalyzer
{
    private static bool ContainsRuntimeBinderOptionsEscape(
        SyntaxNode node,
        SemanticModel semanticModel,
        IParameterSymbol binderOptionsParameter,
        HashSet<ILocalSymbol> binderOptionsAliases,
        bool parameterStillTargetsRuntimeOptions)
    {
        foreach (var assignment in node
                     .DescendantNodesAndSelf(ExecutionScope.ShouldDescend)
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
                     .DescendantNodesAndSelf(ExecutionScope.ShouldDescend)
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
                     .DescendantNodesAndSelf(ExecutionScope.ShouldDescend)
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
                     .DescendantNodesAndSelf(ExecutionScope.ShouldDescend)
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
                     .DescendantNodes(ExecutionScope.ShouldDescend)
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
                     .DescendantNodesAndSelf(ExecutionScope.ShouldDescend)
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
}
