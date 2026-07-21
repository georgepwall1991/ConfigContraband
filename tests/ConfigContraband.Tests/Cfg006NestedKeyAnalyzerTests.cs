using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{

    [Fact]
    public async Task Cfg006_reports_unknown_key_under_nested_options_object()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions Database { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                public string ConnectionString { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 7, 4, 24)
            .WithArguments("App:Database:ConnetionString", "DatabaseOptions", ". Did you mean \"ConnectionString\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Database": {
                  "ConnetionString": "Server=.;"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_under_nested_struct_options_object()
    {
        // The nested options property is a struct. The real ConfigurationBinder binds and
        // recurses into struct-typed properties, so an unknown key under it must be flagged
        // just as it is under a class-typed nested object.
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions Database { get; set; }
            }

            public struct DatabaseOptions
            {
                public string ConnectionString { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 7, 4, 24)
            .WithArguments("App:Database:ConnetionString", "DatabaseOptions", ". Did you mean \"ConnectionString\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Database": {
                  "ConnetionString": "Server=.;"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_treats_get_only_struct_nested_object_as_non_bindable()
    {
        // A get-only struct property cannot be bound in place — the binder mutates a copy it
        // cannot write back through the read-only property — so it is treated as non-bindable
        // (the same as before struct nested objects were recognized at all). The analyzer must
        // NOT recurse into it (no deep `App:Database:ConnetionString` typo report); the whole
        // `App:Database` section is instead the unmatched key, since nothing binds it. This is
        // the conservative outcome that avoids claiming a required member is satisfied when the
        // runtime binder never populates the struct.
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions Database { get; } = new();
            }

            public struct DatabaseOptions
            {
                public string ConnectionString { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 15)
            .WithArguments("App:Database", "AppOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Database": {
                  "ConnetionString": "Server=.;"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_checks_sibling_colon_delimited_nested_keys_under_one_projected_object()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions Database { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                public string Host { get; set; } = "";

                public int Port { get; set; }
            }
            """);

        var expectedHost = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 2, 3, 2, 22)
            .WithArguments("App:Database:Hots", "DatabaseOptions", ". Did you mean \"Host\"?");

        var expectedPort = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 3, 3, 21)
            .WithArguments("App:Database:Prt", "DatabaseOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App:Database:Hots": "api",
              "App:Database:Prt": 443
            }
            """),
            expectedHost,
            expectedPort);
    }

    [Fact]
    public async Task Cfg006_keeps_projected_object_when_later_colon_delimited_scalar_uses_same_path()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions Database { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                public string Host { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 2, 3, 2, 22)
            .WithArguments("App:Database:Hots", "DatabaseOptions", ". Did you mean \"Host\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App:Database:Hots": "api",
              "App:Database": "ignored scalar"
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_under_nested_object_in_user_namespace_starting_with_system()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Systematic.Options;\n", optionsTypes: """
            namespace Systematic.Options
            {
                public sealed class AppOptions
                {
                    public DatabaseOptions Database { get; set; } = new();
                }

                public sealed class DatabaseOptions
                {
                    public string ConnectionString { get; set; } = "";
                }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 7, 4, 24)
            .WithArguments("App:Database:ConnetionString", "DatabaseOptions", ". Did you mean \"ConnectionString\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Database": {
                  "ConnetionString": "Server=.;"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_honors_configuration_key_name_alias_under_nested_options_object()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions Database { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [ConfigurationKeyName("connection_string")]
                public string ConnectionString { get; set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Database": {
                  "connection_string": "Server=.;"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_reports_alias_declared_only_on_overridden_virtual_property()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class BaseOptions
            {
                public virtual string ApiKey { get; set; } = "";
            }

            public sealed class StripeOptions : BaseOptions
            {
                [ConfigurationKeyName("api_key")]
                public override string ApiKey { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 14)
            .WithArguments("Stripe:api_key", "StripeOptions", ". Did you mean \"ApiKey\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "api_key": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_property_name_when_nested_configuration_key_name_overrides_binding_name()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions Database { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [ConfigurationKeyName("connection_string")]
                public string ConnectionString { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 7, 4, 25)
            .WithArguments("App:Database:ConnectionString", "DatabaseOptions", ". Did you mean \"connection_string\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Database": {
                  "ConnectionString": "Server=.;"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_does_not_recurse_into_scalar_property_with_object_value()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": {
                  "Value": "secret"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_does_not_recurse_into_system_namespace_object_property()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public System.Uri Endpoint { get; set; } = new("https://example.test");
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Hots": "example.test"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_under_collection_item_object()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<ServerOptions> Servers { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";

                public int Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 6, 9, 6, 14)
            .WithArguments("App:Servers:0:Prt", "ServerOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Servers": [
                  {
                    "Host": "api",
                    "Prt": 443
                  }
                ]
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_under_initialized_get_only_collection_item_object()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<ServerOptions> Servers { get; } = [];
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";

                public int Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 6, 9, 6, 14)
            .WithArguments("App:Servers:0:Prt", "ServerOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Servers": [
                  {
                    "Host": "api",
                    "Prt": 443
                  }
                ]
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_does_not_report_scalar_array_items()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public string[] AllowedHosts { get; set; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "AllowedHosts": [
                  "api.example.com",
                  "admin.example.com"
                ]
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_does_not_report_initialized_get_only_scalar_collection_items()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<string> AllowedHosts { get; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "AllowedHosts": [
                  "api.example.com",
                  "admin.example.com"
                ]
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_does_not_report_dictionary_entries_as_unknown_properties()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, string> Labels { get; set; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Labels": {
                  "tier": "gold",
                  "region": "eu"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_under_dictionary_value_object()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, ServerOptions> Servers { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";

                public int Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 6, 9, 6, 14)
            .WithArguments("App:Servers:primary:Prt", "ServerOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Servers": {
                  "primary": {
                    "Host": "example.test",
                    "Prt": 443
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_under_initialized_get_only_dictionary_value_object()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, ServerOptions> Servers { get; } = [];
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";

                public int Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 6, 9, 6, 14)
            .WithArguments("App:Servers:primary:Prt", "ServerOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Servers": {
                  "primary": {
                    "Host": "example.test",
                    "Prt": 443
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_does_not_report_valid_dictionary_value_object_keys()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, ServerOptions> Servers { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";

                public int Port { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Servers": {
                  "primary": {
                    "Host": "example.test",
                    "Port": 443
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_under_dictionary_value_object_collection()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, List<ServerOptions>> ServersByRegion { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";

                public int Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 7, 11, 7, 16)
            .WithArguments("App:ServersByRegion:eu:0:Prt", "ServerOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "ServersByRegion": {
                  "eu": [
                    {
                      "Host": "example.test",
                      "Prt": 443
                    }
                  ]
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_does_not_report_valid_dictionary_value_object_collection_keys()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, List<ServerOptions>> ServersByRegion { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";

                public int Port { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "ServersByRegion": {
                  "eu": [
                    {
                      "Host": "example.test",
                      "Port": 443
                    }
                  ]
                }
              }
            }
            """));
    }
}
