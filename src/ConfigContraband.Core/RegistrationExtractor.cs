using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace ConfigContraband;

/// <summary>
/// Discovers the configuration-section bindings in a compilation so the schema generator knows which
/// <c>appsettings.json</c> sections map to which options types.
/// </summary>
/// <remarks>
/// This is a focused scanner for the common, statically provable shapes the schema generator needs:
/// <c>AddOptions&lt;T&gt;().BindConfiguration("Section")</c>, <c>OptionsBuilder&lt;T&gt;.Bind(config.GetSection("Section"))</c>,
/// <c>OptionsBuilder&lt;T&gt;.Bind(config.GetRequiredSection("Section"))</c>, and the matching direct
/// <c>services.Configure&lt;T&gt;(...)</c> section-binding overloads, including fluent chains. It deliberately
/// does not reproduce the analyzer's full strict-binding proof; for schema generation, being conservative about
/// <c>additionalProperties</c> degrades gracefully (a missed strict flag just leaves a section open). The options
/// type model itself is shared with the analyzer via <see cref="OptionsTypeMetadata"/>.
/// </remarks>
internal static class RegistrationExtractor
{
    public static IReadOnlyList<SchemaSection> ExtractAll(Compilation compilation, CancellationToken cancellationToken = default)
    {
        var sections = new List<SchemaSection>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = compilation.GetSemanticModel(tree);

            foreach (var invocation in tree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (TryExtract(invocation, model, cancellationToken, out var section))
                {
                    sections.Add(section);
                }
            }
        }

        return sections;
    }

    private static bool TryExtract(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        out SchemaSection section)
    {
        section = null!;

        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        switch (method.Name)
        {
            case "BindConfiguration":
                return TryExtractBindConfiguration(invocation, method, model, out section);
            case "Bind":
                return TryExtractBind(invocation, method, model, out section);
            case "Configure":
                return TryExtractConfigure(invocation, method, model, out section);
            default:
                return false;
        }
    }

    private static bool TryExtractBindConfiguration(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel model,
        out SchemaSection section)
    {
        section = null!;

        if (!IsFrameworkOptionsBuilderConfigurationMethod(method) ||
            GetOptionsBuilderTypeArgument(invocation, model) is not { } optionsType)
        {
            return false;
        }

        var sectionExpression = TryGetArgumentExpression(invocation, model, "configSectionPath");
        if (sectionExpression is null ||
            model.GetConstantValue(sectionExpression).Value is not string sectionPath ||
            sectionPath.Length == 0)
        {
            return false;
        }

        DetectBinderFlags(invocation, model, out var strict, out var bindsNonPublic);
        var optionsName = GetOptionsBuilderName(invocation, model);
        section = new SchemaSection(sectionPath, optionsType, strict, bindsNonPublic, OptionsInstanceIsValidatedInScope(invocation, optionsType, optionsName, model));
        return true;
    }

    private static bool TryExtractBind(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel model,
        out SchemaSection section)
    {
        section = null!;

        if (!IsFrameworkOptionsBuilderConfigurationMethod(method) ||
            GetOptionsBuilderTypeArgument(invocation, model) is not { } optionsType)
        {
            return false;
        }

        var configurationExpression = TryGetArgumentExpression(invocation, model, "config");
        if (configurationExpression is null ||
            !TryGetSectionPath(configurationExpression, model, out var sectionPath))
        {
            return false;
        }

        DetectBinderFlags(invocation, model, out var strict, out var bindsNonPublic);
        var optionsName = GetOptionsBuilderName(invocation, model);
        section = new SchemaSection(sectionPath, optionsType, strict, bindsNonPublic, OptionsInstanceIsValidatedInScope(invocation, optionsType, optionsName, model));
        return true;
    }

    private static bool TryExtractConfigure(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SemanticModel model,
        out SchemaSection section)
    {
        section = null!;

        if (!method.IsGenericMethod ||
            method.TypeArguments.Length != 1 ||
            method.TypeArguments[0] is not INamedTypeSymbol optionsType)
        {
            return false;
        }

        // Only the framework Configure<T> overload binds configuration; ignore unrelated Configure<T> helpers.
        if (!IsFrameworkMethod(
                method,
                "Microsoft.Extensions.DependencyInjection.OptionsConfigurationServiceCollectionExtensions",
                "Microsoft.Extensions.Options.ConfigurationExtensions"))
        {
            return false;
        }

        // Find the IConfiguration argument and the section it points at. This also excludes the
        // Configure<T>(Action<T>) overload, which has no configuration argument.
        string? sectionPath = null;
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (TryGetSectionPath(argument.Expression, model, out var candidate))
            {
                sectionPath = candidate;
                break;
            }
        }

        if (sectionPath is null)
        {
            return false;
        }

        // Direct Configure<T>(GetSection(...)) enforces [Required] only when a matching
        // AddOptions<T>().ValidateDataAnnotations() is registered in the same scope (CFG002).
        DetectBinderFlags(invocation, model, out var strict, out var bindsNonPublic);
        var optionsName = GetConfigureName(invocation, model);
        section = new SchemaSection(sectionPath, optionsType, strict, bindsNonPublic, OptionsInstanceIsValidatedInScope(invocation, optionsType, optionsName, model));
        return true;
    }

    private static INamedTypeSymbol? GetOptionsBuilderTypeArgument(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return null;
        }

        if (model.GetTypeInfo(memberAccess.Expression).Type is INamedTypeSymbol receiverType &&
            receiverType.Name == "OptionsBuilder" &&
            receiverType.Arity == 1 &&
            receiverType.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.Options")
        {
            return receiverType.TypeArguments[0] as INamedTypeSymbol;
        }

        return null;
    }

    private static bool IsFrameworkOptionsBuilderConfigurationMethod(IMethodSymbol method)
    {
        return IsFrameworkMethod(
            method,
            "Microsoft.Extensions.DependencyInjection.OptionsBuilderConfigurationExtensions",
            "Microsoft.Extensions.Options.ConfigurationExtensions");
    }

    private static bool IsFrameworkMethod(
        IMethodSymbol method,
        string containingType,
        string containingAssembly)
    {
        method = method.ReducedFrom ?? method;
        return method.ContainingType?.ToDisplayString() == containingType &&
               method.ContainingAssembly?.Name == containingAssembly;
    }

    private static ExpressionSyntax? TryGetArgumentExpression(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        string parameterName)
    {
        return (model.GetOperation(invocation) as IInvocationOperation)?
            .Arguments
            .FirstOrDefault(argument => argument.Parameter?.Name == parameterName)?
            .Value.Syntax as ExpressionSyntax;
    }

    private static bool TryGetSectionPath(ExpressionSyntax expression, SemanticModel model, out string sectionPath)
    {
        sectionPath = string.Empty;

        if (expression is not InvocationExpressionSyntax invocation ||
            invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        var methodName = memberAccess.Name.Identifier.Text;
        if (methodName != "GetSection" && methodName != "GetRequiredSection")
        {
            return false;
        }

        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method ||
            !IsFrameworkConfigurationSectionMethod(method, methodName))
        {
            return false;
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 1 ||
            model.GetConstantValue(arguments[0].Expression).Value is not string segment ||
            segment.Length == 0)
        {
            return false;
        }

        sectionPath = TryGetSectionPath(memberAccess.Expression, model, out var prefix)
            ? prefix + ":" + segment
            : segment;
        return true;
    }

    private static bool IsFrameworkConfigurationSectionMethod(IMethodSymbol method, string methodName)
    {
        if (methodName == "GetSection")
        {
            return IsFrameworkMethod(
                method,
                "Microsoft.Extensions.Configuration.IConfiguration",
                "Microsoft.Extensions.Configuration.Abstractions");
        }

        return methodName == "GetRequiredSection" &&
               IsFrameworkMethod(
                   method,
                   "Microsoft.Extensions.Configuration.ConfigurationExtensions",
                   "Microsoft.Extensions.Configuration.Abstractions");
    }

    private static bool OptionsInstanceIsValidatedInScope(
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol optionsType,
        string optionsName,
        SemanticModel model)
    {
        // Look for an OptionsBuilder<T>.ValidateDataAnnotations() for the same options instance (type and
        // name) within the enclosing method body (or top-level statements), or a same-scope
        // [OptionsValidator] IValidateOptions<T> DI registration. Matching the name avoids marking a
        // "B" registration validated just because "A" of the same type is validated, while still
        // covering the split "Configure<T>(...); AddOptions<T>().ValidateDataAnnotations();" pattern.
        // IValidateOptions<T> applies to every name of T, so the OptionsValidator path matches on type.
        SyntaxNode? scope = invocation.FirstAncestorOrSelf<BlockSyntax>();
        scope ??= invocation.FirstAncestorOrSelf<GlobalStatementSyntax>()?.Parent;
        scope ??= invocation.FirstAncestorOrSelf<ArrowExpressionClauseSyntax>();
        scope ??= invocation.FirstAncestorOrSelf<EqualsValueClauseSyntax>();
        if (scope is null)
        {
            return false;
        }

        foreach (var candidate in scope.DescendantNodes(ExecutionScope.ShouldDescend).OfType<InvocationExpressionSyntax>())
        {
            if (!IsDirectlyExecutedInScope(candidate, scope))
            {
                continue;
            }

            if (OptionsValidatorRegistration.TryGetValidatedOptionsType(candidate, model, out var validatorOptionsType) &&
                SymbolEqualityComparer.Default.Equals(validatorOptionsType, optionsType))
            {
                return true;
            }

            if (candidate.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "ValidateDataAnnotations" &&
                IsFrameworkDataAnnotationsValidation(candidate, model) &&
                GetOptionsBuilderTypeArgument(candidate, model) is { } validatedType &&
                SymbolEqualityComparer.Default.Equals(validatedType, optionsType) &&
                string.Equals(GetOptionsBuilderName(candidate, model), optionsName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDirectlyExecutedInScope(InvocationExpressionSyntax invocation, SyntaxNode scope)
    {
        if (scope is BlockSyntax block)
        {
            return invocation.FirstAncestorOrSelf<StatementSyntax>() is { } statement &&
                   statement.Parent is BlockSyntax parentBlock &&
                   parentBlock.Span == block.Span &&
                   IsUnconditionallyEvaluatedWithin(invocation, statement);
        }

        if (scope is ArrowExpressionClauseSyntax arrowExpression)
        {
            return invocation.FirstAncestorOrSelf<ArrowExpressionClauseSyntax>()?.Span ==
                   arrowExpression.Span &&
                   IsUnconditionallyEvaluatedWithin(invocation, arrowExpression);
        }

        if (scope is EqualsValueClauseSyntax equalsValue)
        {
            return invocation.FirstAncestorOrSelf<EqualsValueClauseSyntax>()?.Span ==
                   equalsValue.Span &&
                   IsUnconditionallyEvaluatedWithin(invocation, equalsValue);
        }

        return invocation.FirstAncestorOrSelf<StatementSyntax>() is { } topLevelStatement &&
               topLevelStatement.Parent is GlobalStatementSyntax globalStatement &&
               globalStatement.Parent?.RawKind == scope.RawKind &&
               globalStatement.Parent.Span == scope.Span &&
               IsUnconditionallyEvaluatedWithin(invocation, topLevelStatement);
    }

    private static bool IsUnconditionallyEvaluatedWithin(SyntaxNode candidate, SyntaxNode boundary)
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

    private static bool IsFrameworkDataAnnotationsValidation(
        InvocationExpressionSyntax invocation,
        SemanticModel model)
    {
        var symbolInfo = model.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol method)
        {
            return false;
        }

        method = method.ReducedFrom ?? method;
        return method.ContainingType?.Name == "OptionsBuilderDataAnnotationsExtensions" &&
               method.ContainingAssembly?.Name == "Microsoft.Extensions.Options.DataAnnotations";
    }

    private static string GetOptionsBuilderName(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        // Walk down the fluent receiver chain to the AddOptions[WithValidateOnStart]<T>(name?) call and
        // return its name literal, or the default (empty) name when unnamed or undeterminable.
        InvocationExpressionSyntax? current = invocation;
        while (current is not null)
        {
            if (current.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                break;
            }

            if (memberAccess.Name is GenericNameSyntax generic &&
                (generic.Identifier.Text == "AddOptions" || generic.Identifier.Text == "AddOptionsWithValidateOnStart"))
            {
                foreach (var argument in current.ArgumentList.Arguments)
                {
                    if (model.GetConstantValue(argument.Expression).Value is string name)
                    {
                        return name;
                    }
                }

                return string.Empty;
            }

            current = memberAccess.Expression as InvocationExpressionSyntax;
        }

        return string.Empty;
    }

    private static string GetConfigureName(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        // The framework Configure<T> name overload passes the name as a string literal; the IConfiguration
        // argument is a GetSection(...) call, not a constant string.
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (model.GetConstantValue(argument.Expression).Value is string name)
            {
                return name;
            }
        }

        return string.Empty;
    }

    private static void DetectBinderFlags(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        out bool strict,
        out bool bindsNonPublicProperties)
    {
        strict = false;
        bindsNonPublicProperties = false;

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (!TryGetBinderOptionsParameter(argument.Expression, model, out var parameter))
            {
                continue;
            }

            AnalyzeBinderOptionsCallback(
                argument.Expression,
                model,
                parameter,
                out var callbackStrict,
                out var callbackBindsNonPublicProperties);
            strict |= callbackStrict;
            bindsNonPublicProperties |= callbackBindsNonPublicProperties;
        }
    }

    private static void AnalyzeBinderOptionsCallback(
        ExpressionSyntax expression,
        SemanticModel model,
        IParameterSymbol parameter,
        out bool strict,
        out bool bindsNonPublicProperties)
    {
        strict = false;
        bindsNonPublicProperties = false;

        var aliases = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        var parameterTargetsRuntimeOptions = true;
        var runtimeOptionsEscaped = false;
        if (GetCallbackBody(expression) is not { } body)
        {
            return;
        }

        if (body is ExpressionSyntax expressionBody)
        {
            ApplyDirectBinderOptionsAssignment(
                expressionBody,
                model,
                parameter,
                aliases,
                parameterTargetsRuntimeOptions,
                ref strict,
                ref bindsNonPublicProperties);
            return;
        }

        foreach (var statement in ((BlockSyntax)body).Statements)
        {
            var statementEscapes = ContainsRuntimeBinderOptionsEscape(
                statement,
                model,
                parameter,
                aliases,
                parameterTargetsRuntimeOptions);
            if (statementEscapes)
            {
                runtimeOptionsEscaped = true;
                aliases.Clear();
                strict = false;
                bindsNonPublicProperties = false;
            }

            if (statement is LocalDeclarationStatementSyntax localDeclaration)
            {
                foreach (var variable in localDeclaration.Declaration.Variables)
                {
                    if (model.GetDeclaredSymbol(variable) is not ILocalSymbol local)
                    {
                        continue;
                    }

                    if (variable.Initializer?.Value is { } initializer &&
                        IsRuntimeBinderOptionsReference(
                            initializer,
                            model,
                            parameter,
                            aliases,
                            parameterTargetsRuntimeOptions && !runtimeOptionsEscaped))
                    {
                        aliases.Add(local);
                    }
                    else
                    {
                        aliases.Remove(local);
                    }
                }

                continue;
            }

            if (statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment })
            {
                var assignmentTarget = model.GetSymbolInfo(assignment.Left).Symbol;
                if (SymbolEqualityComparer.Default.Equals(assignmentTarget, parameter))
                {
                    parameterTargetsRuntimeOptions = false;
                    continue;
                }

                if (assignmentTarget is ILocalSymbol local)
                {
                    if (IsRuntimeBinderOptionsReference(
                            assignment.Right,
                            model,
                            parameter,
                            aliases,
                            parameterTargetsRuntimeOptions && !runtimeOptionsEscaped))
                    {
                        aliases.Add(local);
                    }
                    else
                    {
                        aliases.Remove(local);
                    }

                    continue;
                }

                if (ApplyDirectBinderOptionsAssignment(
                        assignment,
                        model,
                        parameter,
                        aliases,
                        parameterTargetsRuntimeOptions && !runtimeOptionsEscaped,
                        ref strict,
                        ref bindsNonPublicProperties))
                {
                    continue;
                }
            }

            if (ContainsRuntimeBinderOptionsAliasDeclaration(
                    statement,
                    model,
                    parameter,
                    aliases,
                    parameterTargetsRuntimeOptions))
            {
                runtimeOptionsEscaped = true;
                aliases.Clear();
                strict = false;
                bindsNonPublicProperties = false;
            }

            if (!runtimeOptionsEscaped)
            {
                ApplyUnconditionalNestedBinderOptionsAssignments(
                    statement,
                    model,
                    parameter,
                    aliases,
                    parameterTargetsRuntimeOptions,
                    ref strict,
                    ref bindsNonPublicProperties);
            }

            InvalidateConditionalBinderOptionsAssignments(
                statement,
                model,
                parameter,
                aliases,
                parameterTargetsRuntimeOptions,
                ref strict,
                ref bindsNonPublicProperties);

            InvalidateConditionallyAssignedAliases(statement, model, aliases);

            if (ContainsBinderOptionsParameterAssignment(statement, model, parameter))
            {
                parameterTargetsRuntimeOptions = false;
                strict = false;
                bindsNonPublicProperties = false;
            }
        }
    }

    private static void InvalidateConditionallyAssignedAliases(
        SyntaxNode node,
        SemanticModel model,
        HashSet<ILocalSymbol> aliases)
    {
        foreach (var assignment in node
                     .DescendantNodes(ExecutionScope.ShouldDescend)
                     .OfType<AssignmentExpressionSyntax>())
        {
            if (model.GetSymbolInfo(assignment.Left).Symbol is ILocalSymbol local)
            {
                aliases.Remove(local);
            }
        }
    }

    private static void ApplyUnconditionalNestedBinderOptionsAssignments(
        SyntaxNode node,
        SemanticModel model,
        IParameterSymbol parameter,
        HashSet<ILocalSymbol> aliases,
        bool parameterTargetsRuntimeOptions,
        ref bool strict,
        ref bool bindsNonPublicProperties)
    {
        foreach (var assignment in node
                     .DescendantNodes(ExecutionScope.ShouldDescend)
                     .OfType<AssignmentExpressionSyntax>()
                     .Where(candidate => IsUnconditionallyExecutedWithin(candidate, node)))
        {
            ApplyDirectBinderOptionsAssignment(
                assignment,
                model,
                parameter,
                aliases,
                parameterTargetsRuntimeOptions,
                ref strict,
                ref bindsNonPublicProperties);
        }
    }

    private static CSharpSyntaxNode? GetCallbackBody(ExpressionSyntax expression)
    {
        return expression switch
        {
            SimpleLambdaExpressionSyntax simpleLambda => simpleLambda.Body,
            ParenthesizedLambdaExpressionSyntax parenthesizedLambda => parenthesizedLambda.Body,
            AnonymousMethodExpressionSyntax anonymousMethod => anonymousMethod.Block,
            _ => null,
        };
    }

    private static bool ApplyDirectBinderOptionsAssignment(
        ExpressionSyntax expression,
        SemanticModel model,
        IParameterSymbol parameter,
        HashSet<ILocalSymbol> aliases,
        bool parameterTargetsRuntimeOptions,
        ref bool strict,
        ref bool bindsNonPublicProperties)
    {
        if (expression is not AssignmentExpressionSyntax assignment ||
            !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            assignment.Left is not MemberAccessExpressionSyntax target ||
            !IsRuntimeBinderOptionsReference(
                target.Expression,
                model,
                parameter,
                aliases,
                parameterTargetsRuntimeOptions))
        {
            return false;
        }

        var property = model.GetSymbolInfo(target).Symbol as IPropertySymbol;
        if (property?.ContainingType.ToDisplayString() != "Microsoft.Extensions.Configuration.BinderOptions")
        {
            return false;
        }

        var isTrue = model.GetConstantValue(assignment.Right) is { HasValue: true, Value: true };
        switch (property.Name)
        {
            case "ErrorOnUnknownConfiguration":
                strict = isTrue;
                return true;
            case "BindNonPublicProperties":
                bindsNonPublicProperties = isTrue;
                return true;
            default:
                return false;
        }
    }

    private static void InvalidateConditionalBinderOptionsAssignments(
        SyntaxNode node,
        SemanticModel model,
        IParameterSymbol parameter,
        HashSet<ILocalSymbol> aliases,
        bool parameterTargetsRuntimeOptions,
        ref bool strict,
        ref bool bindsNonPublicProperties)
    {
        foreach (var assignment in node
                     .DescendantNodes(ExecutionScope.ShouldDescend)
                     .OfType<AssignmentExpressionSyntax>())
        {
            if (IsUnconditionallyExecutedWithin(assignment, node) ||
                assignment.Left is not MemberAccessExpressionSyntax target ||
                !IsRuntimeBinderOptionsReference(
                    target.Expression,
                    model,
                    parameter,
                    aliases,
                    parameterTargetsRuntimeOptions) ||
                model.GetSymbolInfo(target).Symbol is not IPropertySymbol property ||
                property.ContainingType.ToDisplayString() != "Microsoft.Extensions.Configuration.BinderOptions")
            {
                continue;
            }

            switch (property.Name)
            {
                case "ErrorOnUnknownConfiguration":
                    strict = false;
                    break;
                case "BindNonPublicProperties":
                    bindsNonPublicProperties = false;
                    break;
            }
        }
    }

    private static bool IsUnconditionallyExecutedWithin(SyntaxNode candidate, SyntaxNode container)
    {
        if (container is not BlockSyntax and
            not UsingStatementSyntax and
            not TryStatementSyntax { Catches.Count: 0 })
        {
            return false;
        }

        foreach (var ancestor in candidate.Ancestors())
        {
            if (ancestor.Span == container.Span && ancestor.RawKind == container.RawKind)
            {
                return true;
            }

            if (ancestor is ExpressionStatementSyntax or BlockSyntax or FinallyClauseSyntax or UsingStatementSyntax)
            {
                continue;
            }

            if (ancestor is TryStatementSyntax { Catches.Count: 0 })
            {
                continue;
            }

            return false;
        }

        return false;
    }

    private static bool ContainsRuntimeBinderOptionsAliasDeclaration(
        SyntaxNode node,
        SemanticModel model,
        IParameterSymbol parameter,
        HashSet<ILocalSymbol> aliases,
        bool parameterTargetsRuntimeOptions)
    {
        return node.DescendantNodes(ExecutionScope.ShouldDescend)
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable =>
                variable.Initializer?.Value is { } initializer &&
                IsRuntimeBinderOptionsReference(
                    initializer,
                    model,
                    parameter,
                    aliases,
                    parameterTargetsRuntimeOptions));
    }

    private static bool ContainsRuntimeBinderOptionsEscape(
        SyntaxNode node,
        SemanticModel model,
        IParameterSymbol parameter,
        HashSet<ILocalSymbol> aliases,
        bool parameterTargetsRuntimeOptions)
    {
        foreach (var anonymousFunction in node
                     .DescendantNodesAndSelf()
                     .OfType<AnonymousFunctionExpressionSyntax>())
        {
            SyntaxNode? body = anonymousFunction.ExpressionBody ?? (SyntaxNode?)anonymousFunction.Block;
            if (ReferencesRuntimeBinderOptions(
                    body,
                    model,
                    parameter,
                    aliases,
                    parameterTargetsRuntimeOptions))
            {
                return true;
            }
        }

        foreach (var localFunction in node
                     .DescendantNodesAndSelf()
                     .OfType<LocalFunctionStatementSyntax>())
        {
            SyntaxNode? body = localFunction.ExpressionBody?.Expression ?? (SyntaxNode?)localFunction.Body;
            if (ReferencesRuntimeBinderOptions(
                    body,
                    model,
                    parameter,
                    aliases,
                    parameterTargetsRuntimeOptions))
            {
                return true;
            }
        }

        foreach (var assignment in node
                     .DescendantNodesAndSelf(ExecutionScope.ShouldDescend)
                     .OfType<AssignmentExpressionSyntax>())
        {
            if (IsRuntimeBinderOptionsReference(
                    assignment.Right,
                    model,
                    parameter,
                    aliases,
                    parameterTargetsRuntimeOptions) &&
                model.GetSymbolInfo(assignment.Left).Symbol is not ILocalSymbol and not IParameterSymbol)
            {
                return true;
            }
        }

        foreach (var invocation in node
                     .DescendantNodesAndSelf(ExecutionScope.ShouldDescend)
                     .OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                IsRuntimeBinderOptionsReference(
                    memberAccess.Expression,
                    model,
                    parameter,
                    aliases,
                    parameterTargetsRuntimeOptions))
            {
                return true;
            }

            if (invocation.ArgumentList.Arguments.Any(argument =>
                    IsRuntimeBinderOptionsReference(
                        argument.Expression,
                        model,
                        parameter,
                        aliases,
                        parameterTargetsRuntimeOptions)))
            {
                return true;
            }
        }

        return node.DescendantNodesAndSelf(ExecutionScope.ShouldDescend)
            .OfType<BaseObjectCreationExpressionSyntax>()
            .Any(creation =>
                creation.ArgumentList?.Arguments.Any(argument =>
                    IsRuntimeBinderOptionsReference(
                        argument.Expression,
                        model,
                        parameter,
                        aliases,
                        parameterTargetsRuntimeOptions)) == true);
    }

    private static bool ReferencesRuntimeBinderOptions(
        SyntaxNode? node,
        SemanticModel model,
        IParameterSymbol parameter,
        HashSet<ILocalSymbol> aliases,
        bool parameterTargetsRuntimeOptions)
    {
        return node?.DescendantNodesAndSelf(ExecutionScope.ShouldDescend)
            .OfType<ExpressionSyntax>()
            .Any(expression =>
                IsRuntimeBinderOptionsReference(
                    expression,
                    model,
                    parameter,
                    aliases,
                    parameterTargetsRuntimeOptions)) == true;
    }

    private static bool ContainsBinderOptionsParameterAssignment(
        SyntaxNode node,
        SemanticModel model,
        IParameterSymbol parameter)
    {
        return node.DescendantNodes(ExecutionScope.ShouldDescend)
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment =>
                SymbolEqualityComparer.Default.Equals(
                    model.GetSymbolInfo(assignment.Left).Symbol,
                    parameter));
    }

    private static bool IsRuntimeBinderOptionsReference(
        ExpressionSyntax expression,
        SemanticModel model,
        IParameterSymbol parameter,
        HashSet<ILocalSymbol> aliases,
        bool parameterTargetsRuntimeOptions)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax postfix
                    when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = postfix.Operand;
                    continue;
                case CheckedExpressionSyntax checkedExpression:
                    expression = checkedExpression.Expression;
                    continue;
                case BinaryExpressionSyntax asExpression
                    when asExpression.IsKind(SyntaxKind.AsExpression):
                    expression = asExpression.Left;
                    continue;
                default:
                    break;
            }

            break;
        }

        var symbol = model.GetSymbolInfo(expression).Symbol;
        return parameterTargetsRuntimeOptions &&
               SymbolEqualityComparer.Default.Equals(symbol, parameter) ||
               symbol is ILocalSymbol local && aliases.Contains(local);
    }

    private static bool TryGetBinderOptionsParameter(
        ExpressionSyntax expression,
        SemanticModel model,
        out IParameterSymbol parameter)
    {
        ParameterSyntax? syntax = expression switch
        {
            SimpleLambdaExpressionSyntax simpleLambda => simpleLambda.Parameter,
            ParenthesizedLambdaExpressionSyntax parenthesizedLambda
                when parenthesizedLambda.ParameterList.Parameters.Count == 1 =>
                parenthesizedLambda.ParameterList.Parameters[0],
            AnonymousMethodExpressionSyntax anonymousMethod
                when anonymousMethod.ParameterList?.Parameters.Count == 1 =>
                anonymousMethod.ParameterList.Parameters[0],
            _ => null,
        };

        if (syntax is null ||
            model.GetDeclaredSymbol(syntax) is not IParameterSymbol candidate ||
            candidate.Type.ToDisplayString() != "Microsoft.Extensions.Configuration.BinderOptions")
        {
            parameter = null!;
            return false;
        }

        parameter = candidate;
        return true;
    }

}
