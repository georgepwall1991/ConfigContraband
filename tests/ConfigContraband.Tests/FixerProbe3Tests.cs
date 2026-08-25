using ConfigContraband.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace ConfigContraband.Tests;

public sealed class FixerProbe3Tests
{
    private const string Header =
        "using System.ComponentModel.DataAnnotations;\n" +
        "using Microsoft.Extensions.Configuration;\n" +
        "using Microsoft.Extensions.DependencyInjection;\n\n" +
        "public sealed class Startup\n{\n" +
        "    public void Configure(IServiceCollection services, IConfiguration configuration)\n" +
        "    {\n";

    private const string Footer =
        "    }\n}\n\n" +
        "public sealed class StripeOptions\n" +
        "{\n" +
        "    [Required]\n" +
        "    public string ApiKey { get; set; } = \"\";\n" +
        "}\n";

    private static string OptionsSource(string body) => Header + body + Footer;

    private static readonly (string Key, string Json) Appsettings = ("appsettings.json",
        "{\n  \"Stripe\": {\n    \"ApiKey\": \"secret\"\n  }\n}");

    [Fact]
    public async Task Probe_const_local_section_anchor_fix()
    {
        var source = OptionsSource(
            "        const string Section = \"Strpie\";\n" +
            "        services.AddOptions<StripeOptions>()\n" +
            "            .BindConfiguration({|#0:Section|})\n" +
            "            .ValidateDataAnnotations()\n" +
            "            .ValidateOnStart();\n");

        var fixedSource = OptionsSource(
            "        const string Section = \"Strpie\";\n" +
            "        services.AddOptions<StripeOptions>()\n" +
            "            .BindConfiguration(\"Stripe\")\n" +
            "            .ValidateDataAnnotations()\n" +
            "            .ValidateOnStart();\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, Appsettings, expected);
    }
}
