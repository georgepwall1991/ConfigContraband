using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{

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
