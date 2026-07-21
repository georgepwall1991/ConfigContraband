using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{

    [Fact]
    public async Task Cfg009_reports_missing_get_required_section_with_suggestion()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Strpie"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_static_get_required_section_call()
    {
        var source = DirectReadSource("""
            _ = ConfigurationExtensions.GetRequiredSection(configuration, {|#0:"Strpie"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_static_get_connection_string_with_named_arguments()
    {
        var source = DirectReadSource("""
            _ = ConfigurationExtensions.GetConnectionString(name: {|#0:"Databsae"|}, configuration: configuration);
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("ConnectionStrings:Databsae", ". Did you mean \"ConnectionStrings:Database\"?");

        await Verifier.VerifyAnalyzerAsync(source, DatabaseConnectionAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_static_get_call()
    {
        var source = DirectReadSource("""
            _ = ConfigurationBinder.Get<ServerOptions>(configuration.GetSection({|#0:"Strpie"|}));
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_static_bind_call()
    {
        var source = DirectReadSource("""
            ConfigurationBinder.Bind(configuration.GetSection({|#0:"Strpie"|}), new ServerOptions());
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_get_required_section_on_injected_field()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            private readonly IConfiguration _configuration = null!;

            public void ReadField()
            {
                _ = _configuration.GetRequiredSection({|#0:"Missing"|});
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_get_required_section_on_configuration_root()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            public void ReadRoot(IConfigurationRoot root)
            {
                _ = root.GetRequiredSection({|#0:"Missing"|});
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_ignores_missing_section_bound_through_get_without_typo_evidence()
    {
        var source = DirectReadSource("""
            _ = configuration.GetSection("Missing").Get<ServerOptions>();
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_missing_section_bound_through_bind_without_typo_evidence()
    {
        var source = DirectReadSource("""
            configuration.GetSection("Missing").Bind(new ServerOptions());
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_reports_missing_chained_child_section_with_suggestion()
    {
        var source = DirectReadSource("""
            _ = configuration.GetSection("Parent").GetRequiredSection({|#0:"Chlid"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Parent:Chlid", ". Did you mean \"Parent:Child\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Parent": {
                "Child": {
                  "Name": "value"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_colon_delimited_path()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Foo:Bar"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Foo:Bar", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_section_from_constant_key()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetRequiredSection({|#0:SectionKey|});
            """,
            extraMembers: """
            private const string SectionKey = "Missing";
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_section_from_nameof_key()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:nameof(ServerOptions)|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("ServerOptions", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_get_required_section_through_conditional_access()
    {
        var source = DirectReadSource("""
            _ = configuration?.GetRequiredSection({|#0:"Missing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_bound_section_through_deep_conditional_access()
    {
        var source = DirectReadSource("""
            _ = configuration?.GetSection("Parent")?.GetSection({|#0:"Chlid"|})?.Get<ServerOptions>();
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
    public async Task Cfg009_reports_missing_bound_root_section_through_conditional_access()
    {
        var source = DirectReadSource("""
            _ = configuration?.GetSection({|#0:"Chlid"|})?.Get<ServerOptions>();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Chlid", ". Did you mean \"Child\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Child": { "Value": "present" } }"""),
            expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_bind_section_through_conditional_access()
    {
        var source = DirectReadSource("""
            configuration?.GetSection({|#0:"Chlid"|})?.Bind(new ServerOptions());
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Chlid", ". Did you mean \"Child\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Child": { "Value": "present" } }"""),
            expected);
    }

    [Fact]
    public async Task Cfg009_deep_conditional_access_preserves_conservative_receiver_and_key_gates()
    {
        var source = DirectReadSource(
            """
            var dynamicKey = System.DateTime.UtcNow.Ticks.ToString();
            _ = configuration?.GetSection("Parent")?.GetSection(dynamicKey)?.Get<ServerOptions>();

            var local = new ConfigurationManager();
            _ = local?.GetSection("Parent")?.GetSection("Chlid")?.Get<ServerOptions>();
            """,
            extraMembers: """
            public void ReadStored(IConfigurationSection stored)
            {
                _ = stored?.GetSection("Parent")?.GetSection("Chlid")?.Get<ServerOptions>();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Parent": { "Child": { "Value": "present" } } }"""));
    }

    [Fact]
    public async Task Cfg009_deep_conditional_read_does_not_taint_later_receiver()
    {
        var source = DirectReadSource("""
            _ = configuration?.GetSection("Parent")?.GetSection("Child")?.Get<ServerOptions>();
            _ = configuration.GetRequiredSection({|#0:"Missing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Parent": { "Child": { "Value": "present" } } }"""),
            expected);
    }

    [Fact]
    public async Task Cfg009_conditional_bind_with_unproven_instance_taints_later_receiver()
    {
        var source = DirectReadSource(
            """
            configuration?.GetSection("Child")?.Bind(BindingTarget);
            _ = configuration.GetRequiredSection("Missing");
            """,
            extraMembers: """
            private ServerOptions BindingTarget => new();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Child": { "Value": "present" } }"""));
    }

    [Fact]
    public async Task Cfg009_conditional_get_with_unproven_callback_stays_quiet()
    {
        var source = DirectReadSource(
            """
            _ = configuration?.GetSection("Chlid")?.Get<ServerOptions>(options => Seed(configuration));
            """,
            extraMembers: """
            private static void Seed(IConfiguration configuration)
            {
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Child": { "Value": "present" } }"""));
    }

    [Fact]
    public async Task Cfg009_conditional_bind_into_receiver_taints_later_read()
    {
        var source = DirectReadSource("""
            configuration?.GetSection("Child")?.Bind(configuration);
            _ = configuration.GetRequiredSection("Missing");
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Child": { "Value": "present" } }"""));
    }

    [Fact]
    public async Task Cfg009_conditional_bind_into_cast_receiver_taints_later_read()
    {
        var source = DirectReadSource("""
            ((IConfiguration)configuration)?.GetSection("Child")?.Bind(configuration);
            _ = configuration.GetRequiredSection("Missing");
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Child": { "Value": "present" } }"""));
    }

    [Fact]
    public async Task Cfg009_conditional_bind_into_cast_target_taints_later_read()
    {
        var source = DirectReadSource("""
            configuration?.GetSection("Child")?.Bind((IConfiguration)configuration);
            _ = configuration.GetRequiredSection("Missing");
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Child": { "Value": "present" } }"""));
    }

    [Fact]
    public async Task Cfg009_reports_keyed_bind_typo_after_conditional_section_prefix()
    {
        var source = DirectReadSource("""
            configuration?.GetSection("Features")?.Bind({|#0:"Strpie"|}, new ServerOptions());
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Features:Strpie", ". Did you mean \"Features:Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Features": { "Stripe": { "Value": "present" } } }"""),
            expected);
    }

    [Fact]
    public async Task Cfg009_reports_conditional_typo_after_ordinary_section_prefix()
    {
        var source = DirectReadSource("""
            _ = configuration.GetSection("Parent")?.GetSection({|#0:"Chlid"|})?.Get<ServerOptions>();
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
    public async Task Cfg009_wrapped_conditional_nameof_read_does_not_taint_later_receiver()
    {
        var source = DirectReadSource("""
            _ = (configuration?.GetSection(nameof(ServerOptions))?.Get<ServerOptions>())!;
            _ = configuration.GetRequiredSection({|#0:"Missing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "ServerOptions": { "Value": "present" } }"""),
            expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_get_required_section_on_null_forgiving_receiver()
    {
        var source = DirectReadSource("""
            _ = (configuration!).GetRequiredSection({|#0:"Missing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_connection_string_typo()
    {
        var source = DirectReadSource("""
            _ = configuration.GetConnectionString({|#0:"Databsae"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("ConnectionStrings:Databsae", ". Did you mean \"ConnectionStrings:Database\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "ConnectionStrings": {
                "Database": "Server=localhost"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg009_reports_connection_string_typo_on_known_section_chain()
    {
        var source = DirectReadSource("""
            _ = configuration.GetSection("Tenant").GetConnectionString({|#0:"Databsae"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments(
                "Tenant:ConnectionStrings:Databsae",
                ". Did you mean \"Tenant:ConnectionStrings:Database\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Tenant": {
                "ConnectionStrings": {
                  "Database": "Server=localhost"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg009_reports_static_named_connection_string_typo_on_known_section_chain()
    {
        var source = DirectReadSource("""
            _ = ConfigurationExtensions.GetConnectionString(
                name: {|#0:"Databsae"|},
                configuration: configuration.GetSection("Tenant"));
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments(
                "Tenant:ConnectionStrings:Databsae",
                ". Did you mean \"Tenant:ConnectionStrings:Database\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Tenant": {
                "ConnectionStrings": {
                  "Database": "Server=localhost"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg009_ignores_connection_string_typo_on_stored_section_receiver()
    {
        var source = DirectReadSource("""
            var tenant = configuration.GetSection("Tenant");
            _ = tenant.GetConnectionString("Databsae");
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Tenant": {
                "ConnectionStrings": {
                  "Database": "Server=localhost"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg009_reports_missing_section_with_composite_key_sibling_suggestion()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Foo:Brr"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Foo:Brr", ". Did you mean \"Foo:Bar\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Foo:Bar": {
                "X": "1"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg009_ignores_concrete_custom_configuration_implementation()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            public void ReadCustom(CustomConfiguration custom)
            {
                _ = custom.GetRequiredSection("Missing");
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
    public async Task Cfg009_reports_single_diagnostic_for_missing_required_parent_chain()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Missing"|}).GetRequiredSection("Child");
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }
}
