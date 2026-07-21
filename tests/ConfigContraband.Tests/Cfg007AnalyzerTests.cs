using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{

    [Fact]
    public async Task Cfg007_reports_unknown_key_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_file_scoped_suppression_does_not_downgrade_other_appsettings_files()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var enabledFileExpected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.Production.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");
        var suppressedFileExpected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerWithAnalyzerConfigAsync(
            source,
            new[]
            {
                ("appsettings.json", """
                {
                  "Stripe": {
                    "ApiKey": "value",
                    "WebookSecret": "typo"
                  }
                }
                """),
                ("appsettings.Production.json", """
                {
                  "Stripe": {
                    "ApiKey": "value",
                    "WebookSecret": "typo"
                  }
                }
                """)
            },
            ("/.editorconfig", """
            root = true

            [appsettings.json]
            dotnet_diagnostic.CFG007.severity = none
            """),
            enabledFileExpected,
            suppressedFileExpected);
    }

    [Fact]
    public async Task Cfg007_reports_unknown_key_from_bind_get_section_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Stripe"), options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_unknown_key_from_configure_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<StripeOptions>(
                configuration.GetSection("Stripe"),
                options => options.ErrorOnUnknownConfiguration = true);
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_unknown_key_under_nested_options_object_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 7, 5, 12)
            .WithArguments("App:Database:Prt", "DatabaseOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Database": {
                  "Host": "example.test",
                  "Prt": 443
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_configuration_key_name_alias_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [ConfigurationKeyName("api_key")]
                public string ApiKey { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 3, 5, 3, 14)
            .WithArguments("Stripe:api_key", "StripeOptions", ".");

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
    public async Task Cfg007_does_not_suggest_configuration_key_name_alias_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [ConfigurationKeyName("api_key")]
                public string ApiKey { get; set; } = "";

                public string WebhookSecret { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:api_ky", "StripeOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "api_ky": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_settable_constructor_bound_alias_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                public StripeOptions(string apiKey = "")
                {
                    ApiKey = apiKey;
                }

                [ConfigurationKeyName("api_key")]
                public string ApiKey { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 3, 5, 3, 14)
            .WithArguments("Stripe:api_key", "StripeOptions", ".");

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
    public async Task Cfg007_reports_child_key_under_scalar_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 12)
            .WithArguments("Stripe:ApiKey:Foo", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": {
                  "Foo": "x"
                },
                "WebhookSecret": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_scalar_property_when_error_on_unknown_configuration_is_enabled_via_tuple_deconstruction()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    (options.ErrorOnUnknownConfiguration, options.BindNonPublicProperties) = (true, false);
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 12)
            .WithArguments("Stripe:ApiKey:Foo", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": {
                  "Foo": "x"
                },
                "WebhookSecret": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_scalar_property_when_error_on_unknown_configuration_is_enabled_via_nested_tuple_deconstruction()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    int unused;
                    ((options.ErrorOnUnknownConfiguration, options.BindNonPublicProperties), unused) = ((true, false), 0);
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 12)
            .WithArguments("Stripe:ApiKey:Foo", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": {
                  "Foo": "x"
                },
                "WebhookSecret": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_scalar_child_key_that_exists_on_string_type()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": {
                  "Length": 5
                },
                "WebhookSecret": "secret"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_known_scalar_child_key_when_string_property_has_no_live_instance()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string? ApiKey { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 15)
            .WithArguments("Stripe:ApiKey:Length", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": {
                  "Length": 5
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_known_scalar_child_key_when_uri_property_has_no_live_instance()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Uri? Endpoint { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 23)
            .WithArguments("App:Endpoint:OriginalString", "Uri", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "OriginalString": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_known_child_under_system_scalar_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Version Version { get; set; } = new(1, 0);
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Version": {
                  "Major": 1
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_does_not_report_known_child_under_creatable_system_scalar_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Version? Version { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Version": {
                  "Major": 1
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_does_not_report_initialized_interface_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public interface IEndpoint
            {
                string Url { get; set; }
            }

            public sealed class Endpoint : IEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class AppOptions
            {
                public IEndpoint Endpoint { get; set; } = new Endpoint();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Url": "https://example.test"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_unknown_grandchild_under_value_type_clr_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public TimeSpan Duration { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 14)
            .WithArguments("App:Duration:Ticks:Foo", "Int64", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Duration": {
                  "Ticks": {
                    "Foo": "x"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_known_child_under_nullable_value_type_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public TimeSpan? Duration { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Duration": {
                  "Ticks": 123
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_nullable_wrapper_child_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public int? Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 17)
            .WithArguments("App:Port:HasValue", "Int32", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Port": {
                  "HasValue": true
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_scalar_child_key_that_exists_as_string_indexer_property()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": {
                  "Chars": {
                    "Foo": "x"
                  }
                },
                "WebhookSecret": "secret"
              }
            }
            """));
    }
}
