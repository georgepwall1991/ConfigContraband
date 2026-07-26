using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{
    [Fact]
    public void Diagnostics_link_to_their_exact_rule_documentation()
    {
        const string HelpLinkBase = "https://github.com/georgepwall1991/ConfigContraband#";
        var expectedDocumentation = new Dictionary<string, (string Anchor, string Heading)>(StringComparer.Ordinal)
        {
            [DiagnosticIds.MissingConfigurationSection] =
                ("cfg001-the-section-must-exist", "### `CFG001`: The Section Must Exist"),
            [DiagnosticIds.MissingRequiredConfigurationKey] =
                ("cfg002-required-configuration-keys-must-be-present", "### `CFG002`: Required Configuration Keys Must Be Present"),
            [DiagnosticIds.ValidationNotOnStart] =
                ("cfg003-validation-should-run-when-the-app-starts", "### `CFG003`: Validation Should Run When The App Starts"),
            [DiagnosticIds.DataAnnotationsNotEnabled] =
                ("cfg004-dataannotations-must-be-switched-on", "### `CFG004`: DataAnnotations Must Be Switched On"),
            [DiagnosticIds.NestedValidationNotRecursive] =
                ("cfg005-nested-options-need-recursive-validation", "### `CFG005`: Nested Options Need Recursive Validation"),
            [DiagnosticIds.UnknownConfigurationKey] =
                ("cfg006-config-keys-should-match-options-properties", "### `CFG006`: Config Keys Should Match Options Properties"),
            [DiagnosticIds.UnknownConfigurationKeyWillThrow] =
                ("cfg007-strict-binding-turns-unknown-keys-into-failures", "### `CFG007`: Strict Binding Turns Unknown Keys Into Failures"),
            [DiagnosticIds.ConfigurationValueTypeMismatch] =
                ("cfg008-configuration-values-that-cannot-bind-to-their-target-type", "### `CFG008`: Configuration Values That Cannot Bind To Their Target Type"),
            [DiagnosticIds.ConfigurationKeyNotFound] =
                ("cfg009-direct-configuration-paths-unavailable-from-visible-appsettings-files", "### `CFG009`: Direct Configuration Paths Unavailable from Visible Appsettings Files"),
        };
        var supportedDiagnostics = new ConfigContrabandAnalyzer().SupportedDiagnostics;
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var readme = File.ReadAllText(Path.Combine(repositoryRoot, "README.md"));

        Assert.Equal(expectedDocumentation.Count, supportedDiagnostics.Length);
        foreach (var descriptor in supportedDiagnostics)
        {
            var documentation = Assert.Contains(descriptor.Id, expectedDocumentation);
            Assert.Equal(HelpLinkBase + documentation.Anchor, descriptor.HelpLinkUri);
            Assert.Contains(documentation.Heading, readme, StringComparison.Ordinal);
        }

        Assert.Equal(
            supportedDiagnostics.Length,
            supportedDiagnostics.Select(descriptor => descriptor.HelpLinkUri).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void CI_and_publish_enforce_the_showcase_diagnostic_contract()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var ciWorkflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml"));
        var publishWorkflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "publish.yml"));
        var verifier = Path.Combine(repositoryRoot, "scripts", "verify-showcase.sh");

        Assert.Contains("bash scripts/verify-showcase.sh", ciWorkflow, StringComparison.Ordinal);
        Assert.Contains("bash scripts/verify-showcase.sh", publishWorkflow, StringComparison.Ordinal);
        Assert.True(File.Exists(verifier), "The CI showcase verifier script must exist.");

        var verifierContents = File.ReadAllText(verifier);
        Assert.Contains("-tl:off", verifierContents, StringComparison.Ordinal);
        foreach (var ruleId in Enumerable.Range(1, 9).Select(number => $"CFG{number:000}"))
        {
            Assert.Contains(ruleId, verifierContents, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Packaged_analyzer_references_net8_compatible_roslyn()
    {
        var supportedRoslynVersion = new Version(4, 8, 0, 0);
        var packagedAssemblies = new[]
        {
            typeof(ConfigContrabandAnalyzer).Assembly,
            typeof(ConfigurationSnapshot).Assembly,
        };

        var roslynReferences = packagedAssemblies
            .SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Where(reference => reference.Name?.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(roslynReferences);

        var mismatchedReferences = roslynReferences
            .Where(reference => reference.Version != supportedRoslynVersion)
            .Select(reference => $"{reference.Name}, Version={reference.Version}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(reference => reference)
            .ToArray();

        Assert.True(
            mismatchedReferences.Length == 0,
            $"The shipped analyzer assemblies must reference the exact .NET 8 / Visual Studio 17.8 "
            + $"Roslyn baseline {supportedRoslynVersion}. Mismatches: {string.Join(", ", mismatchedReferences)}");
    }

    [Fact]
    public void Net8_host_verifier_loads_the_exact_packed_candidate()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var verifier = File.ReadAllText(
            Path.Combine(repositoryRoot, "scripts", "verify-net8-analyzer-host.sh"));
        var consumerProject = File.ReadAllText(
            Path.Combine(repositoryRoot, "tests", "Compatibility", "Net8Consumer", "Net8Consumer.csproj"));
        var ciWorkflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "ci.yml"));
        var publishWorkflow = File.ReadAllText(
            Path.Combine(repositoryRoot, ".github", "workflows", "publish.yml"));

        Assert.Contains("ConfigContraband.$analyzer_version.nupkg", verifier, StringComparison.Ordinal);
        Assert.Contains("NUGET_PACKAGES=", verifier, StringComparison.Ordinal);
        Assert.Contains("-p:ConfigContrabandVersion=$analyzer_version", verifier, StringComparison.Ordinal);
        Assert.Contains("--source \"$package_dir\"", verifier, StringComparison.Ordinal);
        Assert.Contains("--source https://api.nuget.org/v3/index.json", verifier, StringComparison.Ordinal);
        var consumerDirectoryChange = verifier.IndexOf("cd \"$consumer_dir\"", StringComparison.Ordinal);
        var restoreInvocation = verifier.IndexOf("dotnet restore", StringComparison.Ordinal);
        var buildInvocation = verifier.IndexOf("dotnet build", StringComparison.Ordinal);
        Assert.InRange(consumerDirectoryChange, 0, restoreInvocation - 1);
        Assert.InRange(consumerDirectoryChange, 0, buildInvocation - 1);
        Assert.Contains(".nupkg.metadata", verifier, StringComparison.Ordinal);
        Assert.Contains("metadata.get(\"source\")", verifier, StringComparison.Ordinal);
        Assert.Contains(
            "<ConfigContrabandVersion Condition=\"'$(ConfigContrabandVersion)' == ''\">0.7.24</ConfigContrabandVersion>",
            consumerProject,
            StringComparison.Ordinal);
        Assert.Contains(
            "<PackageReference Include=\"ConfigContraband\" Version=\"$(ConfigContrabandVersion)\"",
            consumerProject,
            StringComparison.Ordinal);
        Assert.Contains(
            "bash scripts/verify-net8-analyzer-host.sh artifacts/packages",
            ciWorkflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "bash scripts/verify-net8-analyzer-host.sh artifacts/packages",
            publishWorkflow,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyzer_ignores_generated_code()
    {
        var source = """
            // <auto-generated/>
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Strpie")
                        .ValidateDataAnnotations();
                }
            }

            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }
            """;

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret"
              }
            }
            """));
    }

    [Fact]
    public async Task Analyzer_ignores_generated_file_names()
    {
        var source = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Strpie")
                        .ValidateDataAnnotations();
                }
            }

            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }
            """;

        await Verifier.VerifyAnalyzerAsync(
            [("GeneratedOptions.g.cs", source)],
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret"
              }
            }
            """));
    }

    [Fact]
    public void Runtime_validation_rejects_default_struct_that_skips_member_initializer()
    {
        var value = default(RuntimeDefaultStructOptions);
        object instance = value;
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            instance,
            new System.ComponentModel.DataAnnotations.ValidationContext(instance),
            results,
            validateAllProperties: true);

        Assert.False(isValid);
        Assert.Null(value.ConnectionString);
    }

    [Fact]
    public void Required_attribute_accepts_every_non_null_array_initializer_shape()
    {
        var defaults = new RuntimeRequiredArrayDefaults();
        var required = new System.ComponentModel.DataAnnotations.RequiredAttribute();

        Assert.All(
            new[]
            {
                defaults.CollectionExpression,
                defaults.ImplicitArray,
                defaults.ExplicitSizedArray,
                defaults.ExplicitArray,
            },
            value =>
            {
                Assert.NotNull(value);
                Assert.True(required.IsValid(value));
            });
    }

#pragma warning disable CA1825 // Exact new string[0] syntax is part of the runtime-parity matrix.
    private sealed class RuntimeRequiredArrayDefaults
    {
        public string[] CollectionExpression { get; } = [];

        public string[] ImplicitArray { get; } = new[] { "configured" };

        public string[] ExplicitSizedArray { get; } = new string[0];

        public string[] ExplicitArray { get; } = new string[] { "configured" };
    }
#pragma warning restore CA1825
}
