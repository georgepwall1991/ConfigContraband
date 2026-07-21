using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace ConfigContraband;

public sealed partial class ConfigContrabandAnalyzer
{
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
        out bool sectionExpressionContainsFullPath,
        bool resolveStoredSectionOrigins = false)
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
                out _,
                resolveStoredSectionOrigins))
        {
            sectionPath = parentSectionPath + ":" + currentSectionPath;
            sectionExpression = keyExpression;
            return true;
        }

        var receiverType = semanticModel.GetTypeInfo(receiver).Type;
        if (IsConfigurationSectionType(receiverType))
        {
            return resolveStoredSectionOrigins &&
                   TryResolveStoredSectionChainedPath(
                       receiver,
                       currentSectionPath,
                       keyExpression,
                       semanticModel,
                       out sectionPath,
                       out sectionExpression,
                       out sectionExpressionContainsFullPath);
        }

        if (!IsConfigurationType(receiverType))
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
        out bool sectionExpressionContainsFullPath,
        bool resolveStoredSectionOrigins = false)
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
                out sectionExpressionContainsFullPath,
                resolveStoredSectionOrigins);
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
                out sectionExpressionContainsFullPath,
                resolveStoredSectionOrigins);
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
                out _,
                resolveStoredSectionOrigins))
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
            // Stay quiet unless the caller opted into origin resolution and the stored
            // section's own path is statically visible.
            return resolveStoredSectionOrigins &&
                   TryResolveStoredSectionChainedPath(
                       memberAccess.Expression,
                       currentSectionPath,
                       argumentExpression,
                       semanticModel,
                       out sectionPath,
                       out sectionExpression,
                       out sectionExpressionContainsFullPath);
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

    /// <summary>
    /// Composes the full chained path for a <c>GetSection</c>/<c>GetRequiredSection</c> call whose
    /// receiver is a stored <c>IConfigurationSection</c> local with a statically visible origin.
    /// The anchored literal carries only the chained key, so the replacement stays a leaf rewrite.
    /// </summary>
    private static bool TryResolveStoredSectionChainedPath(
        ExpressionSyntax receiver,
        string currentSectionPath,
        ExpressionSyntax keyExpression,
        SemanticModel semanticModel,
        out string sectionPath,
        out ExpressionSyntax sectionExpression,
        out bool sectionExpressionContainsFullPath)
    {
        sectionPath = null!;
        sectionExpression = null!;
        sectionExpressionContainsFullPath = false;

        if (!TryGetStoredSectionOriginPath(receiver, semanticModel, out var storedSectionPath))
        {
            return false;
        }

        sectionPath = storedSectionPath + ":" + currentSectionPath;
        sectionExpression = keyExpression;
        sectionExpressionContainsFullPath = false;
        return true;
    }

    /// <summary>
    /// Resolves the constant section path of a stored <c>IConfigurationSection</c> receiver when
    /// its origin is statically visible: a same-block local whose value — including the last
    /// same-block simple reassignment before the use — is a provable constant section chain, with
    /// no conditional reassignment, mutation, capture, or escape between that definition and the
    /// use. Parameters, method returns, fields, and conditionally reassigned or escaped locals
    /// stay quiet because the stored section's real path cannot be proven.
    /// </summary>
    private static bool TryGetStoredSectionOriginPath(
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        out string originPath)
    {
        return TryGetStoredSectionOriginPath(
            receiver,
            semanticModel,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
            out originPath);
    }

    private static bool TryGetStoredSectionOriginPath(
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> visitedLocals,
        out string originPath)
    {
        originPath = null!;

        var expression = UnwrapConfigurationInterfaceConversions(receiver, semanticModel);
        if (semanticModel.GetSymbolInfo(expression).Symbol is not ILocalSymbol local ||
            !visitedLocals.Add(local))
        {
            return false;
        }

        try
        {
            if (local.DeclaringSyntaxReferences.Length != 1 ||
                local.DeclaringSyntaxReferences[0].GetSyntax() is not VariableDeclaratorSyntax declarator ||
                declarator.SyntaxTree != expression.SyntaxTree ||
                declarator.FirstAncestorOrSelf<BlockSyntax>() is not { } declarationBlock ||
                expression.FirstAncestorOrSelf<BlockSyntax>() != declarationBlock)
            {
                return false;
            }

            ExpressionSyntax? definition = declarator.Initializer?.Value;
            var definitionEnd = declarator.Span.End;
            foreach (var statement in declarationBlock.Statements)
            {
                if (statement.SpanStart >= expression.SpanStart)
                {
                    break;
                }

                if (TryGetDirectLocalAssignment(statement, local, semanticModel, out var assignment))
                {
                    definition = assignment.Right;
                    definitionEnd = statement.Span.End;
                }
            }

            if (definition is null ||
                definitionEnd > expression.SpanStart ||
                HasUnsafeConfigurationUse(
                    local,
                    declarationBlock,
                    semanticModel,
                    definitionEnd,
                    expression.SpanStart,
                    expression.SpanStart))
            {
                return false;
            }

            definition = UnwrapConfigurationInterfaceConversions(definition, semanticModel);
            if (definition is InvocationExpressionSyntax or ConditionalAccessExpressionSyntax)
            {
                return TryGetConfigurationSectionPath(
                    definition,
                    semanticModel,
                    out originPath,
                    out _,
                    out _,
                    resolveStoredSectionOrigins: true);
            }

            return TryGetStoredSectionOriginPath(definition, semanticModel, visitedLocals, out originPath);
        }
        finally
        {
            visitedLocals.Remove(local);
        }
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
    /// <c>TryGetConfigurationSectionPath</c> resolves a normal invocation chain, without
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
        out bool sectionExpressionContainsFullPath,
        bool resolveStoredSectionOrigins = false)
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
                    out _,
                    resolveStoredSectionOrigins))
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
                // constant we can see here. Stay quiet unless the caller opted into origin
                // resolution and the stored section's own path is statically visible.
                return resolveStoredSectionOrigins &&
                       TryResolveStoredSectionChainedPath(
                           conditionalReceiver,
                           currentSectionPath,
                           argumentExpression,
                           semanticModel,
                           out sectionPath,
                           out sectionExpression,
                           out sectionExpressionContainsFullPath);
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
                out _,
                resolveStoredSectionOrigins))
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

}
