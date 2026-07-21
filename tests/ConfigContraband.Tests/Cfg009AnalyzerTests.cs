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

    [Fact]
    public async Task Cfg009_reports_after_earlier_read_only_configuration_calls()
    {
        var source = DirectReadSource("""
            configuration.GetSection("Stripe");
            _ = configuration.GetRequiredSection({|#0:"Missing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_after_earlier_read_only_required_section_call()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection("Stripe");
            _ = configuration.GetRequiredSection({|#0:"Missing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_after_earlier_read_only_connection_string_call()
    {
        var source = DirectReadSource("""
            _ = configuration.GetConnectionString("Database");
            _ = configuration.GetRequiredSection({|#0:"Missing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, DatabaseConnectionAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_stays_quiet_after_nested_configuration_section_mutation()
    {
        var source = DirectReadSource("""
            configuration.GetSection("Dynamic").Value = "value";
            _ = configuration.GetRequiredSection("Dynamic");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_stays_quiet_after_contract_parameter_is_reassigned_to_local_configuration()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            public void ReadReassigned(IConfiguration configuration)
            {
                configuration = new ConfigurationBuilder().Build();
                _ = configuration.GetRequiredSection("Missing");
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_stays_quiet_after_contract_members_are_reassigned_to_local_configuration()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            private IConfiguration _configuration = null!;
            private IConfiguration Configuration { get; set; } = null!;

            public void ReadReassignedMembers()
            {
                _configuration = new ConfigurationBuilder().Build();
                _ = _configuration.GetRequiredSection("FieldMissing");

                Configuration = new ConfigurationBuilder().Build();
                _ = Configuration.GetRequiredSection("PropertyMissing");
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_tracks_straight_line_configuration_aliases()
    {
        var source = DirectReadSource("""
            var localSource = new ConfigurationBuilder().Build();
            IConfiguration localAlias = localSource;
            _ = localAlias.GetRequiredSection("LocalMissing");

            IConfiguration hostSource = configuration;
            var hostAlias = hostSource;
            _ = hostAlias.GetRequiredSection({|#0:"HostMissing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("HostMissing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_stays_quiet_after_conditional_receiver_reassignment()
    {
        var source = DirectReadSource("""
            IConfiguration local = configuration;
            if (System.DateTime.UtcNow.Ticks > 0)
            {
                local = new ConfigurationBuilder().Build();
            }

            _ = local.GetRequiredSection("Missing");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_stays_quiet_after_configuration_manager_mutation()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            public void ReadMutatedManager(ConfigurationManager manager)
            {
                manager["Dynamic"] = "value";
                _ = manager.GetRequiredSection("Missing");
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_stays_quiet_after_receiver_escapes_or_is_captured()
    {
        var source = DirectReadSource(
            """
            IConfiguration byValue = configuration;
            Escape(byValue);
            _ = byValue.GetRequiredSection("ByValueMissing");

            IConfiguration byReference = configuration;
            EscapeByReference(ref byReference);
            _ = byReference.GetRequiredSection("ByReferenceMissing");

            IConfiguration captured = configuration;
            System.Action action = () => _ = captured["Dynamic"];
            _ = captured.GetRequiredSection("CapturedMissing");
            """,
            extraMembers: """
            private static void Escape(IConfiguration value) { }
            private static void EscapeByReference(ref IConfiguration value) { }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_connection_string_without_near_match()
    {
        var source = DirectReadSource("""
            _ = configuration.GetConnectionString("Redis");
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "ConnectionStrings": {
                "Database": "Server=localhost"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg009_ignores_connection_string_without_connection_strings_section()
    {
        var source = DirectReadSource("""
            _ = configuration.GetConnectionString("Databsae");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_get_on_configuration_root()
    {
        var source = DirectReadSource("""
            _ = configuration.Get<ServerOptions>();
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_reports_keyed_bind_typo()
    {
        var source = DirectReadSource("""
            configuration.Bind({|#0:"Strpie"|}, new ServerOptions());
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_static_named_keyed_bind_typo()
    {
        var source = DirectReadSource("""
            ConfigurationBinder.Bind(
                instance: new ServerOptions(),
                key: {|#0:"Strpie"|},
                configuration: configuration);
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_keyed_bind_typo_on_known_section_chain()
    {
        var source = DirectReadSource("""
            configuration.GetSection("Features").Bind({|#0:"Strpie"|}, new ServerOptions());
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Features:Strpie", ". Did you mean \"Features:Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Features": {
                "Stripe": {
                  "Name": "value"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg009_ignores_section_bind_when_instance_argument_can_seed_configuration()
    {
        var source = DirectReadSource(
            """
            configuration.GetSection("Strpie").Bind(Seed(configuration));
            """,
            extraMembers: """
            private static ServerOptions Seed(IConfiguration configuration)
            {
                configuration["Strpie:Host"] = "value";
                return new ServerOptions();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_section_bind_with_static_field_instance()
    {
        var source = DirectReadSource(
            """
            configuration.GetSection("Strpie").Bind(Holder.Instance);
            """,
            extraTypes: """
            public static class Holder
            {
                public static readonly ServerOptions Instance = Create();

                private static ServerOptions Create() => new();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_section_bind_with_unprovable_instance_initializer()
    {
        var source = DirectReadSource(
            """
            configuration.GetSection("Strpie").Bind(new EffectfulOptions());
            """,
            extraTypes: """
            public sealed class EffectfulOptions
            {
                public string Host { get; set; } = Create();

                private static string Create() => "value";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_section_bind_with_user_defined_initializer_cast()
    {
        var source = DirectReadSource(
            """
            configuration.GetSection("Strpie").Bind(new CastOptions());
            """,
            extraTypes: """
            public sealed class CastOptions
            {
                public Converted Value { get; set; } = (Converted)1;
            }

            public readonly struct Converted
            {
                public static implicit operator Converted(int value) => new();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_section_bind_with_user_defined_initializer_after_unary_expression()
    {
        var source = DirectReadSource(
            """
            configuration.GetSection("Strpie").Bind(new UnaryOptions());
            """,
            extraTypes: """
            public sealed class UnaryOptions
            {
                public Converted Value { get; set; } = -1;
            }

            public readonly struct Converted
            {
                public static implicit operator Converted(int value) => new();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_section_bind_with_escaped_nameof_initializer_call()
    {
        var source = DirectReadSource(
            """
            configuration.GetSection("Strpie").Bind(new EscapedNameofOptions());
            """,
            extraTypes: """
            public sealed class EscapedNameofOptions
            {
                public string Value { get; set; } = @nameof(Seed());

                private static string @nameof(string value) => value;

                private static string Seed() => "value";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_reports_section_bind_with_safe_instance_field()
    {
        var source = DirectReadSource(
            """
            configuration.GetSection({|#0:"Strpie"|}).Bind(_instance);
            """,
            extraMembers: """
            private readonly ServerOptions _instance = new();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_section_bind_with_safe_parameter_instance()
    {
        var source = DirectReadSource("""
            configuration.GetSection({|#0:"Strpie"|}).Bind(configuration);
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_section_bind_with_safe_implicit_initializers()
    {
        var source = DirectReadSource(
            """
            configuration.GetSection({|#0:"Strpie"|}).Bind(new SafeInitializerOptions());
            """,
            extraTypes: """
            public sealed class SafeInitializerOptions
            {
                public string? NullValue { get; set; } = null;
                public int? NullableValue { get; set; } = null;
                public object RuntimeType { get; set; } = typeof(string);
                public System.Type TypeValue { get; set; } = typeof(string);
                public string Name { get; set; } = nameof(SafeInitializerOptions);
                public int Parenthesized { get; set; } = (1);
                public int Negative { get; set; } = -1;
                public string Suppressed { get; set; } = null!;
                public int DefaultValue { get; set; } = default;
                public int Field = 1;
                public event System.Action? Changed;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_proves_only_inline_converted_binder_options_callbacks()
    {
        var safeSource = DirectReadSource("""
            configuration.GetSection({|#0:"Strpie"|}).Bind(
                new ServerOptions(),
                (System.Action<BinderOptions>)(options => options.BindNonPublicProperties = true));
            """);
        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");
        await Verifier.VerifyAnalyzerAsync(safeSource, StripeAppSettings, expected);

        var escapedSource = DirectReadSource("""
            System.Action<BinderOptions> configure = options => options.BindNonPublicProperties = true;
            configuration.GetSection("Strpie").Bind(new ServerOptions(), configure);
            """);
        await Verifier.VerifyAnalyzerAsync(escapedSource, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_section_bind_when_binder_options_callback_can_seed_configuration()
    {
        var source = DirectReadSource("""
            configuration.GetSection("Strpie").Bind(
                new ServerOptions(),
                options => configuration["Strpie:Host"] = "value");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_reports_section_bind_with_safe_binder_options_callback()
    {
        var source = DirectReadSource("""
            configuration.GetSection({|#0:"Strpie"|}).Bind(
                new ServerOptions(),
                options => options.BindNonPublicProperties = true);
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_ignores_keyed_bind_when_instance_argument_can_seed_configuration()
    {
        var source = DirectReadSource(
            """
            configuration.Bind("Strpie", Seed(configuration));
            """,
            extraMembers: """
            private static ServerOptions Seed(IConfiguration configuration)
            {
                configuration["Strpie:Host"] = "value";
                return new ServerOptions();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_static_named_keyed_bind_when_instance_argument_can_seed_configuration()
    {
        var source = DirectReadSource(
            """
            ConfigurationBinder.Bind(
                instance: Seed(configuration),
                key: "Strpie",
                configuration: configuration);
            """,
            extraMembers: """
            private static ServerOptions Seed(IConfiguration configuration)
            {
                configuration["Strpie:Host"] = "value";
                return new ServerOptions();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_keyed_bind_when_instance_field_receiver_can_seed_configuration()
    {
        var source = DirectReadSource(
            """
            configuration.Bind("Strpie", Seed(configuration).InstanceField);
            """,
            extraMembers: """
            private static Holder Seed(IConfiguration configuration)
            {
                configuration["Strpie:Host"] = "value";
                return new Holder();
            }
            """,
            extraTypes: """
            public sealed class Holder
            {
                public ServerOptions InstanceField = new();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_same_fully_qualified_name_keyed_bind_shadow()
    {
        var source = DirectReadSource(
            """
            Microsoft.Extensions.Configuration.ConfigurationBinder.Bind(
                configuration,
                "Strpie",
                new ServerOptions());
            """,
            extraTypes: """
            #pragma warning disable CS0436
            namespace Microsoft.Extensions.Configuration
            {
                public static class ConfigurationBinder
                {
                    public static void Bind(
                        IConfiguration configuration,
                        string key,
                        object instance) { }
                }
            }
            #pragma warning restore CS0436
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_bind_with_section_key_overload()
    {
        var source = DirectReadSource("""
            configuration.Bind("Missing", new ServerOptions());
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_get_value_reads()
    {
        var source = DirectReadSource("""
            _ = configuration.GetValue<int>("Missing:Port");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_custom_get_section_overload_inside_real_binder_call()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetSection("Strpie", optional: true).Get<ServerOptions>();
            """,
            extraTypes: """
            public static class CustomConfigurationExtensions
            {
                public static IConfigurationSection GetSection(
                    this IConfiguration configuration,
                    string key,
                    bool optional) => configuration.GetSection(key);
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }
}
