using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{

    [Fact]
    public async Task Cfg009_reports_single_diagnostic_for_missing_required_parent_before_get()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Missing"|}).GetSection("Sub").Get<ServerOptions>();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_single_diagnostic_for_static_get_after_missing_required_parent()
    {
        var source = DirectReadSource("""
            _ = ConfigurationBinder.Get<ServerOptions>(configuration.GetRequiredSection({|#0:"Strpie"|}));
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_child_in_static_required_section_chain()
    {
        var source = DirectReadSource("""
            _ = ConfigurationExtensions.GetRequiredSection(configuration, "Parent")
                .GetRequiredSection({|#0:"Chlid"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Parent:Chlid", ". Did you mean \"Parent:Child\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Parent": { "Child": { "Value": "present" } } }"""),
            expected);
    }

    [Fact]
    public async Task Cfg009_does_not_report_when_no_appsettings_files()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection("Anything");
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg009_does_not_report_existing_section()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection("Stripe");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_does_not_report_section_from_any_appsettings_file()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection("Stripe");
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            new[]
            {
                ("appsettings.json", """
                {
                  "Logging": {
                    "LogLevel": {
                      "Default": "Information"
                    }
                  }
                }
                """),
                ("appsettings.Production.json", """
                {
                  "Stripe": {
                    "ApiKey": "secret"
                  }
                }
                """)
            });
    }

    [Fact]
    public async Task Cfg009_ignores_non_constant_section_keys()
    {
        var source = DirectReadSource("""
            var name = "Missing";
            _ = configuration.GetRequiredSection(name);
            _ = configuration.GetRequiredSection($"{name}Section");
            _ = configuration.GetRequiredSection("");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_does_not_report_nameof_key_matching_existing_section()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection(nameof(ServerOptions));
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "ServerOptions": {
                "Host": "localhost"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg009_ignores_stored_configuration_section_receiver()
    {
        var source = DirectReadSource("""
            IConfigurationSection section = configuration.GetSection("Stripe");
            _ = section.GetRequiredSection("Missing");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_configuration_section_parameter_receiver()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            public void ReadSection(IConfigurationSection section)
            {
                _ = section.GetRequiredSection("Missing");
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_bare_get_section_probe()
    {
        var source = DirectReadSource("""
            var section = configuration.GetSection("Missing");
            if (configuration.GetSection("AlsoMissing").Exists())
            {
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_same_named_method_on_non_configuration_type()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            public void ReadOther(NotConfiguration other)
            {
                _ = other.GetRequiredSection("Missing");
            }
            """,
            extraTypes: """
            public sealed class NotConfiguration
            {
                public NotConfiguration GetRequiredSection(string key) => this;
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_user_defined_get_required_section_extension()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetRequiredSection("Missing");
            """,
            extraTypes: """
            public static class CustomConfigurationExtensions
            {
                public static IConfigurationSection GetRequiredSection(this IConfiguration configuration, string key) => configuration.GetSection(key);
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_same_fully_qualified_name_configuration_extensions_shadow()
    {
        var source = DirectReadSource(
            """
            _ = Microsoft.Extensions.Configuration.ConfigurationExtensions.GetRequiredSection(
                configuration,
                "Strpie");
            """,
            extraTypes: """
            #pragma warning disable CS0436
            namespace Microsoft.Extensions.Configuration
            {
                public static class ConfigurationExtensions
                {
                    public static IConfigurationSection GetRequiredSection(
                        IConfiguration configuration,
                        string key) => configuration.GetSection(key);
                }
            }
            #pragma warning restore CS0436
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_user_defined_required_section_inside_real_binder_call()
    {
        var source = DirectReadSource(
            """
            _ = configuration.CustomGetRequiredSection("Missing").Get<ServerOptions>();
            """,
            extraTypes: """
            public static class CustomConfigurationExtensions
            {
                public static IConfigurationSection CustomGetRequiredSection(
                    this IConfiguration configuration,
                    string key) => configuration.GetSection(key);
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_user_defined_static_get_required_section_inside_real_binder_call()
    {
        var source = DirectReadSource(
            """
            _ = CustomConfigurationExtensions.GetRequiredSection(configuration, "Missing").Get<ServerOptions>();
            """,
            extraTypes: """
            public static class CustomConfigurationExtensions
            {
                public static IConfigurationSection GetRequiredSection(
                    this IConfiguration configuration,
                    string key) => configuration.GetSection(key);
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_does_not_report_section_read_feeding_options_registration()
    {
        var source = """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    services.Configure<ServerOptions>(configuration.GetRequiredSection({|#0:"Missing"|}));
                }
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_does_not_duplicate_cfg001_for_expression_bodied_registration()
    {
        var source = """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services, IConfiguration configuration) =>
                    services.Configure<ServerOptions>(configuration.GetRequiredSection({|#0:"Missing"|}));
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_does_not_duplicate_cfg001_for_nested_registration_section_chain()
    {
        var source = """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    services.Configure<ServerOptions>(
                        configuration.GetRequiredSection("Missing").GetSection({|#0:"Child"|}));
                }
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Missing:Child", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_only_missing_required_parent_before_conditional_child()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Missing"|})?.GetRequiredSection("Child");
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_throwing_chain_when_parent_existence_depends_on_provider_version()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection("Parent").GetRequiredSection({|#0:"Child"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Parent:Child", ".");

        await Verifier.VerifyAnalyzerWithReferencesAsync(
            source,
            ("appsettings.json", """{ "Parent": null }"""),
            Verifier.ConfigurationAbstractionsReferences,
            expected);
    }

    [Fact]
    public async Task Cfg009_reports_net10_empty_object_as_missing_required_section()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Empty"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Empty", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Empty": {} }"""),
            expected);
    }

    [Fact]
    public async Task Cfg009_reports_net10_null_as_missing_required_section()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Empty"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Empty", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Empty": null }"""),
            expected);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"value\"")]
    [InlineData("{ \"Value\": \"present\" }")]
    public async Task Cfg009_accepts_net10_shapes_that_create_a_runtime_section(string jsonValue)
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection("Present");
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", $$"""{ "Present": {{jsonValue}} }"""));
    }

    [Fact]
    public async Task Cfg009_does_not_report_section_read_feeding_options_builder_bind()
    {
        var source = """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    services.AddOptions<ServerOptions>().Bind(configuration.GetRequiredSection({|#0:"Missing"|}));
                }
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_ignores_locally_built_configuration()
    {
        var source = DirectReadSource("""
            var config = new ConfigurationBuilder().Build();
            _ = config.GetRequiredSection("Missing");
            _ = new ConfigurationBuilder().Build().GetRequiredSection("AlsoMissing");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_direct_and_local_configuration_manager_instances()
    {
        var source = DirectReadSource("""
            var local = new ConfigurationManager();
            _ = local.GetRequiredSection("Missing");
            _ = new ConfigurationManager().GetRequiredSection("AlsoMissing");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_reports_configuration_manager_parameter_as_host_contract()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            public void ReadManager(ConfigurationManager manager)
            {
                _ = manager.GetRequiredSection({|#0:"Missing"|});
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_tracks_latest_straight_line_configuration_assignment()
    {
        var source = DirectReadSource("""
            IConfiguration host = new ConfigurationBuilder().Build();
            host = configuration;
            _ = host.GetRequiredSection({|#0:"HostMissing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("HostMissing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_after_contract_reassignment_through_explicit_interface_cast()
    {
        var source = DirectReadSource("""
            IConfiguration current = new ConfigurationBuilder().Build();
            current = (IConfiguration)configuration;
            _ = current.GetRequiredSection({|#0:"Missing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_ignores_local_configuration_manager_through_explicit_interface_cast()
    {
        var source = DirectReadSource("""
            IConfiguration local = (IConfiguration)new ConfigurationManager();
            _ = local.GetRequiredSection("Missing");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_custom_configuration_through_explicit_interface_cast()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            public void ReadCustom(CustomConfiguration custom)
            {
                IConfiguration contract = (IConfiguration)custom;
                _ = contract.GetRequiredSection("Missing");
            }
            """,
            extraTypes: """
            public sealed class CustomConfiguration : IConfiguration
            {
                public string? this[string key] { get => null; set { } }
                public System.Collections.Generic.IEnumerable<IConfigurationSection> GetChildren() => System.Linq.Enumerable.Empty<IConfigurationSection>();
                public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => null!;
                public IConfigurationSection GetSection(string key) => null!;
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_downcast_from_contract_to_custom_configuration()
    {
        var source = DirectReadSource(
            """
            var custom = (CustomConfiguration)configuration;
            _ = custom.GetRequiredSection("Missing");
            """,
            extraTypes: """
            public sealed class CustomConfiguration : IConfiguration
            {
                public string? this[string key] { get => null; set { } }
                public System.Collections.Generic.IEnumerable<IConfigurationSection> GetChildren() => System.Linq.Enumerable.Empty<IConfigurationSection>();
                public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => null!;
                public IConfigurationSection GetSection(string key) => null!;
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_stays_quiet_after_mutation_through_interface_cast()
    {
        var source = DirectReadSource("""
            IConfiguration current = (IConfiguration)configuration;
            ((IConfiguration)current)["Missing"] = "value";
            _ = current.GetRequiredSection("Missing");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_stays_quiet_after_mutation_through_concrete_cast()
    {
        var source = DirectReadSource("""
            IConfiguration current = (IConfiguration)configuration;
            ((ConfigurationManager)current)["Missing"] = "value";
            _ = current.GetRequiredSection("Missing");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_uses_latest_local_assignment_when_host_is_replaced_by_local_configuration()
    {
        var source = DirectReadSource("""
            IConfiguration local = configuration;
            local = new ConfigurationBuilder().Build();
            _ = local.GetRequiredSection("LocalMissing");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }
}
