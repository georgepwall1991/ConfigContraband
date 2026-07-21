using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandCodeFixTests
{
    [Fact]
    public async Task Cfg009_fix_replaces_section_literal()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Strpie"|});
            """);

        var fixedSource = DirectReadSource("""
            _ = configuration.GetRequiredSection("Stripe");
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyCodeFixAsync(
            source,
            fixedSource,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg009_fix_replaces_static_call_key_argument()
    {
        var source = DirectReadSource("""
            _ = ConfigurationExtensions.GetRequiredSection(configuration, {|#0:"Strpie"|});
            """);
        var fixedSource = DirectReadSource("""
            _ = ConfigurationExtensions.GetRequiredSection(configuration, "Stripe");
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyCodeFixAsync(
            source,
            fixedSource,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg009_fix_preserves_chained_leaf_segments()
    {
        var source = DirectReadSource("""
            _ = configuration.GetSection("Features").GetRequiredSection({|#0:"Sub:Strpie"|});
            """);

        var fixedSource = DirectReadSource("""
            _ = configuration.GetSection("Features").GetRequiredSection("Sub:Stripe");
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Features:Sub:Strpie", ". Did you mean \"Features:Sub:Stripe\"?");

        await Verifier.VerifyCodeFixAsync(
            source,
            fixedSource,
            ("appsettings.json", """
            {
              "Features": {
                "Sub": {
                  "Stripe": {
                    "ApiKey": "secret"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg009_fix_not_offered_without_suggestion()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Missing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyCodeFixAsync(
            source,
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret"
              }
            }
            """),
            expected);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task Cfg009_fix_not_offered_for_exact_declared_runtime_empty_section(string jsonValue)
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Empty"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Empty", ".");

        await Verifier.VerifyCodeFixAsync(
            source,
            source,
            ("appsettings.json", $$"""
            {
              "Empty": {{jsonValue}},
              "Empt": {
                "Value": "present"
              }
            }
            """),
            expected);
    }
}
