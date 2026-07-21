using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace ConfigContraband;

public sealed partial class ConfigContrabandAnalyzer
{
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
        expression = UnwrapConfigurationInterfaceConversions(expression, semanticModel);

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

        if (symbol is IParameterSymbol)
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

        if (symbol is IFieldSymbol or IPropertySymbol)
        {
            var typeClassification = ClassifyConfigurationType(GetSymbolType(symbol));
            if (typeClassification != ConfigurationReceiverProvenance.Contract)
            {
                return typeClassification;
            }

            var declaredOrigin = ClassifyDeclaredConfigurationMemberOrigin(symbol, semanticModel);
            if (declaredOrigin != ConfigurationReceiverProvenance.Contract)
            {
                return declaredOrigin;
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

    private static ConfigurationReceiverProvenance ClassifyDeclaredConfigurationMemberOrigin(
        ISymbol symbol,
        SemanticModel semanticModel)
    {
        ExpressionSyntax? initializer = null;
        if (symbol is IFieldSymbol { DeclaringSyntaxReferences.Length: 1 } field &&
            field.DeclaringSyntaxReferences[0].GetSyntax() is VariableDeclaratorSyntax fieldDeclarator)
        {
            initializer = fieldDeclarator.Initializer?.Value;
        }
        else if (symbol is IPropertySymbol { DeclaringSyntaxReferences.Length: 1 } property &&
                 property.DeclaringSyntaxReferences[0].GetSyntax() is PropertyDeclarationSyntax propertyDeclaration)
        {
            initializer = propertyDeclaration.Initializer?.Value ?? propertyDeclaration.ExpressionBody?.Expression;
        }

        if (initializer is null)
        {
            // A member without a declaration initializer may be assigned by a constructor or
            // computed by an accessor. Its provider origin is not proven at the read site.
            return ConfigurationReceiverProvenance.Ambiguous;
        }

        initializer = UnwrapForSectionChainResolution(initializer);
        if (initializer.IsKind(SyntaxKind.NullLiteralExpression))
        {
            // A null/null-forgiving field initializer is the established DI placeholder
            // shape. Constructor writes can replace that placeholder before any read, while
            // later same-block writes are still rejected by HasUnsafeConfigurationUse.
            return MemberMayBeAssignedInConstructor(symbol, semanticModel)
                ? ConfigurationReceiverProvenance.Ambiguous
                : ConfigurationReceiverProvenance.Contract;
        }

        if (initializer.SyntaxTree != semanticModel.SyntaxTree)
        {
            // Analyzer callbacks provide the invocation tree's semantic model. Requesting a
            // second model here is discouraged (RS1030), so a non-null cross-file initializer
            // stays on the conservative side instead of risking AD0001 or a false positive.
            return ConfigurationReceiverProvenance.Ambiguous;
        }

        var operation = semanticModel.GetOperation(initializer);
        if (operation?.ConstantValue is { HasValue: true, Value: null })
        {
            return ConfigurationReceiverProvenance.Contract;
        }

        if (IsConfigurationBuilderBuildInvocation(initializer, semanticModel))
        {
            return ConfigurationReceiverProvenance.Local;
        }

        if (initializer is ObjectCreationExpressionSyntax)
        {
            var createdType = semanticModel.GetTypeInfo(initializer).Type;
            return IsFrameworkConfigurationImplementation(createdType)
                ? ConfigurationReceiverProvenance.Local
                : ClassifyConfigurationType(createdType);
        }

        // Accessors and other member initializers can compute or return any provider.
        return ConfigurationReceiverProvenance.Ambiguous;
    }

    private static bool MemberMayBeAssignedInConstructor(ISymbol symbol, SemanticModel semanticModel)
    {
        foreach (var typeReference in symbol.ContainingType.DeclaringSyntaxReferences)
        {
            if (typeReference.GetSyntax() is not TypeDeclarationSyntax typeDeclaration)
            {
                continue;
            }

            foreach (var constructor in typeDeclaration.Members.OfType<ConstructorDeclarationSyntax>())
            {
                foreach (var assignment in constructor.DescendantNodes(ExecutionScope.ShouldDescend)
                             .OfType<AssignmentExpressionSyntax>())
                {
                    foreach (var identifier in assignment.Left.DescendantNodesAndSelf()
                                 .OfType<IdentifierNameSyntax>())
                    {
                        if (!string.Equals(identifier.Identifier.ValueText, symbol.Name, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (identifier.SyntaxTree != semanticModel.SyntaxTree ||
                            SymbolEqualityComparer.Default.Equals(
                                semanticModel.GetSymbolInfo(identifier).Symbol,
                                symbol))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
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

        // One traversal instead of seven: each node kind below was previously scanned in its own
        // full DescendantNodes pass over the block. The checks are a pure OR with no shared state,
        // so folding them into a single document-order walk yields the same result while visiting
        // the tree once — the dominant cost when a minimal-hosting Program.cs is one large block.
        foreach (var node in block.DescendantNodes())
        {
            if (node.SpanStart < startPosition || node.Span.End > safetyUntilPosition)
            {
                continue;
            }

            switch (node)
            {
                case AssignmentExpressionSyntax assignment
                    when IsUnsafeConfigurationAssignment(assignment, symbol, semanticModel, block, resolutionPosition):
                case InvocationExpressionSyntax invocation
                    when IsUnsafeConfigurationInvocation(invocation, symbol, semanticModel):
                case ObjectCreationExpressionSyntax creation
                    when creation.ArgumentList?.Arguments.Any(argument =>
                        ReferencesSymbol(argument.Expression, symbol, semanticModel)) == true:
                case VariableDeclaratorSyntax declarator
                    when declarator.Initializer?.Value is { } initializer &&
                        initializer.SpanStart != resolutionPosition &&
                        ReferencesSymbol(initializer, symbol, semanticModel):
                case AnonymousFunctionExpressionSyntax anonymousFunction
                    when ReferencesSymbol(anonymousFunction, symbol, semanticModel):
                case LocalFunctionStatementSyntax localFunction
                    when ReferencesSymbol(localFunction, symbol, semanticModel):
                case ReturnStatementSyntax { Expression: { } returned }
                    when ReferencesSymbol(returned, symbol, semanticModel):
                    return true;
            }
        }

        return false;
    }

    private static bool IsUnsafeConfigurationAssignment(
        AssignmentExpressionSyntax assignment,
        ISymbol symbol,
        SemanticModel semanticModel,
        BlockSyntax block,
        int resolutionPosition)
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
                return false;
            }

            return true;
        }

        if (IsExpressionRootedInSymbol(assignment.Left, symbol, semanticModel) ||
            assignment.Right.SpanStart != resolutionPosition &&
            ReferencesSymbol(assignment.Right, symbol, semanticModel))
        {
            if (assignment.Left is IdentifierNameSyntax { Identifier.ValueText: "_" } &&
                semanticModel.GetSymbolInfo(assignment.Left).Symbol is null or IDiscardSymbol &&
                IsReadOnlyFrameworkConfigurationExpression(assignment.Right, semanticModel))
            {
                return false;
            }

            return true;
        }

        return false;
    }

    private static bool IsUnsafeConfigurationInvocation(
        InvocationExpressionSyntax invocation,
        ISymbol symbol,
        SemanticModel semanticModel)
    {
        if (IsReadOnlyFrameworkConfigurationInvocation(invocation, semanticModel))
        {
            return false;
        }

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            IsExpressionRootedInSymbol(memberAccess.Expression, symbol, semanticModel))
        {
            return true;
        }

        if (invocation.Ancestors().OfType<ConditionalAccessExpressionSyntax>()
            .Any(conditionalAccess =>
                IsExpressionRootedInSymbol(
                    GetConfigurationChainRoot(conditionalAccess.Expression, semanticModel),
                    symbol,
                    semanticModel)))
        {
            return true;
        }

        return invocation.ArgumentList.Arguments.Any(argument =>
            ReferencesSymbol(argument.Expression, symbol, semanticModel));
    }

    private static bool IsExpressionRootedInSymbol(
        ExpressionSyntax expression,
        ISymbol symbol,
        SemanticModel semanticModel)
    {
        expression = UnwrapNonUserDefinedConversions(expression, semanticModel);
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

    private static ExpressionSyntax UnwrapConfigurationInterfaceConversions(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        expression = UnwrapForSectionChainResolution(expression);
        while (semanticModel.GetOperation(expression) is IConversionOperation conversion &&
               !conversion.Conversion.IsUserDefined &&
               conversion.Type?.TypeKind == TypeKind.Interface &&
               IsConfigurationType(conversion.Type) &&
               conversion.Operand.Syntax is ExpressionSyntax operand)
        {
            expression = UnwrapForSectionChainResolution(operand);
        }

        return expression;
    }

    private static ExpressionSyntax UnwrapNonUserDefinedConversions(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        expression = UnwrapForSectionChainResolution(expression);
        while (semanticModel.GetOperation(expression) is IConversionOperation conversion &&
               !conversion.Conversion.IsUserDefined &&
               conversion.Operand.Syntax is ExpressionSyntax operand)
        {
            expression = UnwrapForSectionChainResolution(operand);
        }

        return expression;
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
        if (semanticModel.GetOperation(invocation)?.Kind == OperationKind.NameOf)
        {
            return true;
        }

        if (IsFrameworkConfigurationGetSectionInvocation(invocation, semanticModel))
        {
            return true;
        }

        if (semanticModel.GetOperation(invocation) is not IInvocationOperation operation)
        {
            return false;
        }

        if (TryNormalizeDirectConfigurationInvocation(operation, out var directInvocation))
        {
            return directInvocation.ArgumentsAreProvablySafe &&
                   !BindTargetReferencesConfigurationRoot(
                       directInvocation,
                       operation,
                       invocation,
                       semanticModel);
        }

        var method = operation.TargetMethod.ReducedFrom ?? operation.TargetMethod;
        return string.Equals(
                   method.ContainingType?.ToDisplayString(),
                   "Microsoft.Extensions.Configuration.ConfigurationExtensions",
                   StringComparison.Ordinal) &&
               string.Equals(method.Name, "Exists", StringComparison.Ordinal);
    }

    private static bool IsReadOnlyFrameworkConfigurationExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        expression = UnwrapForSectionChainResolution(expression);
        if (expression is InvocationExpressionSyntax invocation)
        {
            return IsReadOnlyFrameworkConfigurationInvocation(invocation, semanticModel);
        }

        if (expression is not ConditionalAccessExpressionSyntax conditionalAccess)
        {
            return false;
        }

        var invocations = conditionalAccess.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Where(candidate =>
                candidate.Expression is not IdentifierNameSyntax { Identifier.Text: "nameof" } &&
                semanticModel.GetOperation(candidate)?.Kind != OperationKind.NameOf)
            .ToArray();
        var rootExpression = UnwrapConfigurationInterfaceConversions(
            GetConfigurationChainRoot(conditionalAccess.Expression, semanticModel),
            semanticModel);
        var rootSymbol = semanticModel.GetSymbolInfo(rootExpression).Symbol;
        return invocations.Length > 0 &&
               invocations.All(candidate =>
                   IsReadOnlyFrameworkConfigurationInvocation(candidate, semanticModel) &&
                   (rootSymbol is null || !BindTargetReferencesSymbol(candidate, rootSymbol, semanticModel)));
    }

    private static bool BindTargetReferencesSymbol(
        InvocationExpressionSyntax invocation,
        ISymbol rootSymbol,
        SemanticModel semanticModel)
    {
        if (semanticModel.GetOperation(invocation) is not IInvocationOperation operation ||
            !TryNormalizeDirectConfigurationInvocation(operation, out var directInvocation) ||
            directInvocation.Kind != DirectConfigurationApiKind.Bind)
        {
            return false;
        }

        var instanceArgument = operation.Arguments.FirstOrDefault(argument =>
            string.Equals(argument.Parameter?.Name, "instance", StringComparison.Ordinal));
        return instanceArgument?.Value.Syntax is ExpressionSyntax instanceExpression &&
               ReferencesSymbol(instanceExpression, rootSymbol, semanticModel);
    }

    private static bool BindTargetReferencesConfigurationRoot(
        DirectConfigurationInvocation directInvocation,
        IInvocationOperation operation,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (directInvocation.Kind != DirectConfigurationApiKind.Bind)
        {
            return false;
        }

        var instanceArgument = operation.Arguments.FirstOrDefault(argument =>
            string.Equals(argument.Parameter?.Name, "instance", StringComparison.Ordinal));
        if (instanceArgument?.Value.Syntax is not ExpressionSyntax instanceExpression)
        {
            return false;
        }

        var enclosingConditional = invocation.Ancestors().OfType<ConditionalAccessExpressionSyntax>().LastOrDefault();
        var receiverExpression = enclosingConditional?.Expression ?? directInvocation.Receiver;
        var rootExpression = UnwrapConfigurationInterfaceConversions(
            GetConfigurationChainRoot(receiverExpression, semanticModel),
            semanticModel);
        var rootSymbol = semanticModel.GetSymbolInfo(rootExpression).Symbol;
        if (rootSymbol is null)
        {
            rootExpression = UnwrapConfigurationInterfaceConversions(
                GetConfigurationChainRoot(directInvocation.Receiver, semanticModel),
                semanticModel);
            rootSymbol = semanticModel.GetSymbolInfo(rootExpression).Symbol;
        }

        if (rootSymbol is null)
        {
            return false;
        }

        var instanceOperation = instanceArgument.Value;
        while (instanceOperation is IConversionOperation conversion)
        {
            instanceOperation = conversion.Operand;
        }

        ISymbol? instanceSymbol = instanceOperation switch
        {
            IParameterReferenceOperation parameter => parameter.Parameter,
            ILocalReferenceOperation local => local.Local,
            IFieldReferenceOperation field => field.Field,
            IPropertyReferenceOperation property => property.Property,
            _ => null,
        };
        return SymbolEqualityComparer.Default.Equals(instanceSymbol, rootSymbol) ||
               ReferencesSymbol(instanceExpression, rootSymbol, semanticModel);
    }

}
