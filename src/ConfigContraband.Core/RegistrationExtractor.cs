using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConfigContraband;

/// <summary>
/// Discovers the configuration-section bindings in a compilation so the schema generator knows which
/// <c>appsettings.json</c> sections map to which options types.
/// </summary>
/// <remarks>
/// This is a focused scanner for the common, statically provable shapes the schema generator needs:
/// <c>AddOptions&lt;T&gt;().BindConfiguration("Section")</c>, <c>OptionsBuilder&lt;T&gt;.Bind(config.GetSection("Section"))</c>,
/// and <c>services.Configure&lt;T&gt;(config.GetSection("Section"))</c>, including fluent chains. It deliberately
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
                return TryExtractBindConfiguration(invocation, model, out section);
            case "Bind":
                return TryExtractBind(invocation, model, out section);
            case "Configure":
                return TryExtractConfigure(invocation, method, model, out section);
            default:
                return false;
        }
    }

    private static bool TryExtractBindConfiguration(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        out SchemaSection section)
    {
        section = null!;

        if (GetOptionsBuilderTypeArgument(invocation, model) is not { } optionsType)
        {
            return false;
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count == 0 ||
            model.GetConstantValue(arguments[0].Expression).Value is not string sectionPath ||
            sectionPath.Length == 0)
        {
            return false;
        }

        DetectBinderFlags(invocation, model, out var strict, out var bindsNonPublic);
        section = new SchemaSection(sectionPath, optionsType, strict, bindsNonPublic, ChainEnablesDataAnnotations(invocation));
        return true;
    }

    private static bool TryExtractBind(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        out SchemaSection section)
    {
        section = null!;

        if (GetOptionsBuilderTypeArgument(invocation, model) is not { } optionsType)
        {
            return false;
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count == 0 ||
            !TryGetSectionPath(arguments[0].Expression, model, out var sectionPath))
        {
            return false;
        }

        DetectBinderFlags(invocation, model, out var strict, out var bindsNonPublic);
        section = new SchemaSection(sectionPath, optionsType, strict, bindsNonPublic, ChainEnablesDataAnnotations(invocation));
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

        // Direct Configure<T>(GetSection(...)) needs a separate AddOptions<T>().ValidateDataAnnotations()
        // registration to enforce [Required] (CFG002); stay conservative and do not mark required here.
        DetectBinderFlags(invocation, model, out var strict, out var bindsNonPublic);
        section = new SchemaSection(sectionPath, optionsType, strict, bindsNonPublic, validatesDataAnnotations: false);
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

    private static bool ChainEnablesDataAnnotations(InvocationExpressionSyntax invocation)
    {
        // Look for a ValidateDataAnnotations() call in the same fluent statement as the binding.
        var statement = invocation.FirstAncestorOrSelf<StatementSyntax>();
        if (statement is null)
        {
            return false;
        }

        foreach (var candidate in statement.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (candidate.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "ValidateDataAnnotations")
            {
                return true;
            }
        }

        return false;
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
            if (argument.Expression is not (SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax or AnonymousMethodExpressionSyntax))
            {
                continue;
            }

            foreach (var assignment in argument.Expression.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.Left is not MemberAccessExpressionSyntax target)
                {
                    continue;
                }

                var isTrue = model.GetConstantValue(assignment.Right) is { HasValue: true, Value: true };
                switch (target.Name.Identifier.Text)
                {
                    case "ErrorOnUnknownConfiguration":
                        strict = isTrue;
                        break;
                    case "BindNonPublicProperties":
                        bindsNonPublicProperties = isTrue;
                        break;
                }
            }
        }
    }
}
