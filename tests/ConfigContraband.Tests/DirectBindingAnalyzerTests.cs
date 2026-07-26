using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{
    [Fact]
    public async Task Cfg006_reports_unknown_key_from_direct_get()
    {
        var source = DirectReadSource("""
            _ = configuration.GetSection("Server").Get<ServerOptions>();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 10)
            .WithArguments("Server:Prt", "ServerOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(source, UnknownServerKeyAppSettings, expected);
    }

    [Fact]
    public async Task Cfg007_reports_unknown_key_from_strict_direct_get()
    {
        var source = DirectReadSource("""
            _ = configuration.GetSection("Server").Get<ServerOptions>(
                options => options.ErrorOnUnknownConfiguration = true);
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 3, 5, 3, 10)
            .WithArguments("Server:Prt", "ServerOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(source, UnknownServerKeyAppSettings, expected);
    }

    [Fact]
    public async Task Cfg008_reports_conversion_failure_from_direct_get()
    {
        var source = DirectReadSource("""
            _ = configuration.GetSection("Server").Get<ServerOptions>();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 13, 3, 19)
            .WithArguments("Server:Port", "int");

        await Verifier.VerifyAnalyzerAsync(source, InvalidServerValueAppSettings, expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_from_direct_bind()
    {
        var source = DirectReadSource("""
            configuration.GetSection("Server").Bind(new ServerOptions());
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 10)
            .WithArguments("Server:Prt", "ServerOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(source, UnknownServerKeyAppSettings, expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_from_keyed_direct_bind()
    {
        var source = DirectReadSource("""
            configuration.Bind("Server", new ServerOptions());
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 10)
            .WithArguments("Server:Prt", "ServerOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(source, UnknownServerKeyAppSettings, expected);
    }

    [Fact]
    public async Task Cfg007_reports_unknown_key_from_strict_direct_bind()
    {
        var source = DirectReadSource("""
            configuration.GetSection("Server").Bind(
                new ServerOptions(),
                binder => binder.ErrorOnUnknownConfiguration = true);
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 3, 5, 3, 10)
            .WithArguments("Server:Prt", "ServerOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(source, UnknownServerKeyAppSettings, expected);
    }

    [Fact]
    public async Task Strict_direct_get_falls_back_to_cfg006_when_cfg007_is_suppressed()
    {
        var source = DirectReadSource("""
            _ = configuration.GetSection("Server").Get<ServerOptions>(
                options => options.ErrorOnUnknownConfiguration = true);
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 10)
            .WithArguments("Server:Prt", "ServerOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            UnknownServerKeyAppSettings,
            DiagnosticIds.UnknownConfigurationKeyWillThrow,
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_conversion_failure_from_direct_bind()
    {
        var source = DirectReadSource("""
            configuration.GetSection("Server").Bind(new ServerOptions());
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 13, 3, 19)
            .WithArguments("Server:Port", "int");

        await Verifier.VerifyAnalyzerAsync(source, InvalidServerValueAppSettings, expected);
    }

    [Fact]
    public async Task Direct_bind_stays_quiet_when_runtime_target_type_can_be_derived()
    {
        var source = DirectReadSource(
            """
            BaseServerOptions options = new DerivedServerOptions();
            configuration.GetSection("Server").Bind(options);
            """,
            extraTypes: """
            public class BaseServerOptions
            {
                public int Port { get; set; }
            }

            public sealed class DerivedServerOptions : BaseServerOptions
            {
                public string Region { get; set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": 5000,
                "Region": "eu-west"
              }
            }
            """));
    }

    [Fact]
    public async Task Direct_get_stays_quiet_when_binder_callback_can_mutate_configuration()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetSection("Server").Get<ServerOptions>(
                options => Seed(configuration, options));
            """,
            extraMembers: """
            private static void Seed(IConfiguration configuration, BinderOptions options)
            {
                configuration["Server:Prt"] = null;
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, UnknownServerKeyAppSettings);
    }

    [Fact]
    public async Task Direct_bind_stays_quiet_when_target_factory_can_mutate_configuration()
    {
        var source = DirectReadSource(
            """
            configuration.GetSection("Server").Bind(CreateOptions(configuration));
            """,
            extraMembers: """
            private static ServerOptions CreateOptions(IConfiguration configuration)
            {
                configuration["Server:Prt"] = null;
                return new ServerOptions();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, UnknownServerKeyAppSettings);
    }

    [Fact]
    public async Task Direct_binding_stays_quiet_for_locally_built_configuration()
    {
        var source = DirectReadSource("""
            var local = new ConfigurationManager();
            _ = local.GetSection("Server").Get<ServerOptions>();
            local.GetSection("Server").Bind(new ServerOptions());
            """);

        await Verifier.VerifyAnalyzerAsync(source, UnknownServerKeyAppSettings);
    }

    [Fact]
    public async Task Direct_get_stays_quiet_for_collection_root()
    {
        var source = DirectReadSource("""
            _ = configuration.GetSection("Servers").Get<System.Collections.Generic.List<ServerOptions>>();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Servers": {
                "0": {
                  "Port": "nope"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Direct_get_stays_quiet_for_custom_collection_root()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetSection("Servers").Get<ServerList>(
                options => options.ErrorOnUnknownConfiguration = true);
            """,
            extraTypes: """
            public sealed class ServerList : System.Collections.Generic.List<ServerOptions>
            {
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Servers": {
                "0": {
                  "Port": 5000
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Direct_get_stays_quiet_for_custom_dictionary_root()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetSection("Servers").Get<ServerMap>();
            """,
            extraTypes: """
            public sealed class ServerMap : System.Collections.Generic.Dictionary<string, ServerOptions>
            {
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Servers": {
                "primary": {
                  "Port": 5000
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Direct_bind_stays_quiet_for_user_defined_target_conversion()
    {
        var source = DirectReadSource(
            """
            configuration.GetSection("Server").Bind((ActualOptions)new SourceOptions());
            """,
            extraTypes: """
            public sealed class SourceOptions
            {
                public static explicit operator ActualOptions(SourceOptions source) => new();
            }

            public sealed class ActualOptions
            {
                public string Region { get; set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Server": { "Region": "eu-west" } }"""));
    }

    [Fact]
    public async Task Direct_bind_with_user_defined_target_conversion_stays_quiet_for_missing_path()
    {
        var source = DirectReadSource(
            """
            configuration.GetSection("Sevrer").Bind((ActualOptions)new SourceOptions());
            """,
            extraTypes: """
            public sealed class SourceOptions
            {
                public static explicit operator ActualOptions(SourceOptions source) => new();
            }

            public sealed class ActualOptions
            {
                public string Region { get; set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Server": { "Region": "eu-west" } }"""));
    }

    [Fact]
    public async Task Direct_get_stays_quiet_for_user_defined_null_callback_conversion()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetSection("Server").Get<ServerOptions>(
                configureOptions: (CallbackSource?)null);
            """,
            extraTypes: """
            public sealed class CallbackSource
            {
                public static implicit operator System.Action<BinderOptions>?(CallbackSource? source) =>
                    options => options.ErrorOnUnknownConfiguration = true;
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, UnknownServerKeyAppSettings);
    }

    [Fact]
    public async Task Direct_get_stays_quiet_for_abstract_target()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetSection("Server").Get<AbstractServerOptions>();
            """,
            extraTypes: """
            public abstract class AbstractServerOptions
            {
                public int Port { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, UnknownServerKeyAppSettings);
    }

    [Fact]
    public async Task Direct_get_stays_quiet_for_target_without_runtime_constructor()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetSection("Server").Get<UncreatableOptions>();
            """,
            extraTypes: """
            public sealed class UncreatableOptions
            {
                private UncreatableOptions()
                {
                }

                public int Port { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, UnknownServerKeyAppSettings);
    }

    [Fact]
    public async Task Direct_get_stays_quiet_for_target_with_by_ref_constructor()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetSection("Server").Get<RefOptions>();
            """,
            extraTypes: """
            public sealed class RefOptions
            {
                public RefOptions(ref int port)
                {
                    Port = port;
                }

                public int Port { get; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, UnknownServerKeyAppSettings);
    }

    [Fact]
    public async Task Strict_direct_get_falls_back_to_cfg006_when_final_bind_non_public_properties_value_is_false()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetSection("Server").Get<ResetOptions>(options =>
            {
                options.BindNonPublicProperties = true;
                options.BindNonPublicProperties = false;
                options.ErrorOnUnknownConfiguration = true;
            });
            """,
            extraTypes: """
            public sealed class ResetOptions
            {
                public NestedOptions? Nested { get; private set; }
            }

            public sealed class NestedOptions
            {
                public int Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Server:Nested", "ResetOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Nested": {
                  "Prt": 5000
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Direct_get_honors_bind_non_public_properties_from_anonymous_method()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetSection("Server").Get<PrivateOptions>(
                delegate(BinderOptions options)
                {
                    options.BindNonPublicProperties = true;
                });
            """,
            extraTypes: """
            public sealed class PrivateOptions
            {
                public string Host { get; private set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Server": { "Host": "localhost" } }"""));
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_from_constructor_bound_direct_get()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetSection("Server").Get<ConstructorOptions>();
            """,
            extraTypes: """
            public sealed class ConstructorOptions
            {
                public ConstructorOptions(int port)
                {
                    Port = port;
                }

                public int Port { get; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 10)
            .WithArguments("Server:Prt", "ConstructorOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(source, UnknownServerKeyAppSettings, expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_from_struct_direct_get()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetSection("Server").Get<ValueOptions>();
            """,
            extraTypes: """
            public struct ValueOptions
            {
                public int Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 10)
            .WithArguments("Server:Prt", "ValueOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(source, UnknownServerKeyAppSettings, expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_from_direct_bind_with_empty_initializer()
    {
        var source = DirectReadSource("""
            configuration.GetSection("Server").Bind(new ServerOptions { });
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 10)
            .WithArguments("Server:Prt", "ServerOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(source, UnknownServerKeyAppSettings, expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_from_direct_get_with_null_callback()
    {
        var source = DirectReadSource("""
            _ = configuration.GetSection("Server").Get<ServerOptions>(configureOptions: null);
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 10)
            .WithArguments("Server:Prt", "ServerOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(source, UnknownServerKeyAppSettings, expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_from_direct_bind_with_null_callback()
    {
        var source = DirectReadSource("""
            configuration.GetSection("Server").Bind(
                new ServerOptions(),
                configureOptions: null);
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 10)
            .WithArguments("Server:Prt", "ServerOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(source, UnknownServerKeyAppSettings, expected);
    }

    private static (string filename, string content) UnknownServerKeyAppSettings =>
        ("appsettings.json", """
        {
          "Server": {
            "Prt": 5000
          }
        }
        """);

    private static (string filename, string content) InvalidServerValueAppSettings =>
        ("appsettings.json", """
        {
          "Server": {
            "Port": "nope"
          }
        }
        """);
}
