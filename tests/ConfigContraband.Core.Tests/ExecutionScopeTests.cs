using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConfigContraband.Core.Tests;

public sealed class ExecutionScopeTests
{
    [Fact]
    public void Unconditional_invocation_in_a_statement_is_evaluated()
    {
        Assert.True(IsUnconditional("G();", "G"));
    }

    [Fact]
    public void Ternary_invocation_is_not_unconditional()
    {
        Assert.False(IsUnconditional("_ = true ? G() : H();", "G"));
    }

    [Fact]
    public void Switch_expression_invocation_is_not_unconditional()
    {
        Assert.False(IsUnconditional("_ = true switch { true => G(), _ => H() };", "G"));
    }

    [Fact]
    public void Logical_and_invocation_is_not_unconditional()
    {
        Assert.False(IsUnconditional("_ = true && G();", "G"));
    }

    [Fact]
    public void Logical_or_invocation_is_not_unconditional()
    {
        Assert.False(IsUnconditional("_ = false || G();", "G"));
    }

    [Fact]
    public void Coalesce_invocation_is_not_unconditional()
    {
        Assert.False(IsUnconditional("_ = null ?? G();", "G"));
    }

    [Fact]
    public void Coalesce_assignment_invocation_is_not_unconditional()
    {
        Assert.False(IsUnconditional("object? x = null; x ??= G();", "G"));
    }

    [Fact]
    public void Conditional_access_invocation_is_not_unconditional()
    {
        Assert.False(IsUnconditional("C? c = null; c?.M();", "M"));
    }

    [Fact]
    public void Unrelated_boundary_is_not_unconditional()
    {
        var tree = CSharpSyntaxTree.ParseText(
            """
            class C
            {
                void M()
                {
                    G();
                }

                void Other()
                {
                }

                static object G() => null!;
            }
            """);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(node => node.ToString() == "G()");
        var other = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Other");

        Assert.False(ExecutionScope.IsUnconditionallyEvaluatedWithin(invocation, other));
    }

    private static bool IsUnconditional(string statements, string invocationName)
    {
        var tree = CSharpSyntaxTree.ParseText(
            $$"""
            class C
            {
                void M()
                {
                    {{statements}}
                }

                static object G() => null!;
                static object H() => null!;
                void M2() {}
            }
            """);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(node => NameOf(node) == invocationName);
        var statement = invocation.FirstAncestorOrSelf<StatementSyntax>()!;
        return ExecutionScope.IsUnconditionallyEvaluatedWithin(invocation, statement);
    }

    private static string? NameOf(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.ValueText,
            _ => null
        };
    }
}
