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

    [Fact]
    public async Task Cfg007_reports_child_key_under_scalar_collection_item_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<string> Values { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 14)
            .WithArguments("App:Values:0:Foo", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Values": [
                  {
                    "Foo": "x"
                  }
                ]
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_clr_member_shaped_child_key_under_string_collection_item_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<string> Values { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 17)
            .WithArguments("App:Values:0:Length", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Values": [
                  {
                    "Length": 5
                  }
                ]
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_known_child_under_value_type_collection_item_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<TimeSpan> Durations { get; set; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Durations": [
                  {
                    "Ticks": 123
                  }
                ]
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_scalar_reference_collection_item_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<Uri> Endpoints { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 22)
            .WithArguments("App:Endpoints:0:AbsoluteUri", "Uri", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoints": [
                  {
                    "AbsoluteUri": "https://example.test"
                  }
                ]
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_known_child_under_creatable_reference_collection_item_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<Version> Versions { get; set; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Versions": [
                  {
                    "Major": 1
                  }
                ]
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_does_not_report_dictionary_entries_inside_collection_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<Dictionary<string, string>> Values { get; set; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Values": [
                  {
                    "primary": "x"
                  }
                ]
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_does_not_report_known_child_under_value_type_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Point> Points { get; set; } = [];
            }

            public struct Point
            {
                public int X { get; set; }

                public int Y { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Points": {
                  "origin": {
                    "X": 1,
                    "Y": 2
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_does_not_report_known_child_under_creatable_reference_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Version> Versions { get; set; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Versions": {
                  "stable": {
                    "Major": 1
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_scalar_reference_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Uri> Endpoints { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 22)
            .WithArguments("App:Endpoints:primary:AbsoluteUri", "Uri", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoints": {
                  "primary": {
                    "AbsoluteUri": "https://example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_nested_dictionary_values_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Dictionary<string, FeatureOptions>> Map { get; set; } = [];
            }

            public sealed class FeatureOptions
            {
                public bool Enabled { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "Tenant": {
                    "Feature": {
                      "Enabled": true
                    }
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_nested_object_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Dictionary<string, FeatureOptions>> Map { get; set; } = [];
            }

            public sealed class FeatureOptions
            {
                public bool Enabled { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 6, 11, 6, 20)
            .WithArguments("App:Map:Tenant:Feature:Enabeld", "FeatureOptions", ". Did you mean \"Enabled\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "Tenant": {
                    "Feature": {
                      "Enabeld": true
                    }
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_nested_dictionary_object_collection_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Dictionary<string, List<FeatureOptions>>> Map { get; set; } = [];
            }

            public sealed class FeatureOptions
            {
                public bool Enabled { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 7, 13, 7, 22)
            .WithArguments("App:Map:Tenant:Features:0:Enabeld", "FeatureOptions", ". Did you mean \"Enabled\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "Tenant": {
                    "Features": [
                      {
                        "Enabeld": true
                      }
                    ]
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_nested_scalar_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Dictionary<string, int>> Ports { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 6, 11, 6, 16)
            .WithArguments("App:Ports:tenant:https:Foo", "Int32", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Ports": {
                  "tenant": {
                    "https": {
                      "Foo": 443
                    }
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_scalar_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, string> Labels { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 14)
            .WithArguments("App:Labels:primary:Foo", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Labels": {
                  "primary": {
                    "Foo": "x"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_open_dictionary_value_shape_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class Endpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class AppOptions
            {
                public Dictionary<string, object> Map { get; } = new()
                {
                    ["main"] = new Endpoint()
                };
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "main": {
                    "Url": "https://example.test"
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_stays_quiet_for_guid_keyed_dictionary_nested_object_values_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<Guid, EndpointOptions> Endpoints { get; set; } = [];
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoints": {
                  "3fa85f64-5717-4562-b3fc-2c963f66afa6": {
                    "Url": "https://example.test",
                    "Timout": 5
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_stays_quiet_for_guid_keyed_dictionary_object_shaped_scalar_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<Guid, string> Labels { get; set; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Labels": {
                  "3fa85f64-5717-4562-b3fc-2c963f66afa6": {
                    "Foo": "x"
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_unknown_key_under_enum_keyed_dictionary_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public enum Region
            {
                East,
                West
            }

            public sealed class AppOptions
            {
                public Dictionary<Region, EndpointOptions> Endpoints { get; set; } = [];
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 6, 9, 6, 17)
            .WithArguments("App:Endpoints:East:Timout", "EndpointOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoints": {
                  "East": {
                    "Url": "https://example.test",
                    "Timout": 5
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_unknown_key_under_int_keyed_dictionary_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<int, EndpointOptions> Endpoints { get; set; } = [];
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 6, 9, 6, 17)
            .WithArguments("App:Endpoints:1:Timout", "EndpointOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoints": {
                  "1": {
                    "Url": "https://example.test",
                    "Timout": 5
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_property_name_typo_but_stays_quiet_inside_guid_keyed_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<Guid, EndpointOptions> Endpoints { get; set; } = [];
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 9, 5, 9, 15)
            .WithArguments("App:Endpints", "AppOptions", ". Did you mean \"Endpoints\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoints": {
                  "3fa85f64-5717-4562-b3fc-2c963f66afa6": {
                    "Url": "https://example.test",
                    "Timout": 5
                  }
                },
                "Endpints": {
                  "Foo": "bar"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_new_dictionary_entry_when_another_entry_is_prepopulated_polymorphic()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, BaseEndpoint> Map { get; set; } = new()
                {
                    ["primary"] = new DerivedEndpoint()
                };
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expectedPrimary = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:primary:Token", "BaseEndpoint", ".");
        var expectedSecondary = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 9, 9, 9, 16)
            .WithArguments("App:Map:secondary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "primary": {
                    "Token": "secret",
                    "Url": "https://primary.example.test"
                  },
                  "secondary": {
                    "Token": "secret",
                    "Url": "https://secondary.example.test"
                  }
                }
              }
            }
            """),
            expectedPrimary,
            expectedSecondary);
    }

    [Fact]
    public async Task Cfg007_reports_case_mismatched_dictionary_entry_when_default_dictionary_is_prepopulated_polymorphic()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, BaseEndpoint> Map { get; } = new()
                {
                    ["primary"] = new DerivedEndpoint()
                };
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:Primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "Primary": {
                    "Token": "secret",
                    "Url": "https://example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_polymorphic_dictionary_value_when_collection_expression_initializer_is_empty()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, BaseEndpoint> Map { get; } = [];
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "primary": {
                    "Token": "secret",
                    "Url": "https://example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_dictionary_value_when_empty_comparer_constructor_has_no_prepopulated_polymorphic_value()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, BaseEndpoint> Map { get; } = new(StringComparer.OrdinalIgnoreCase);
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "primary": {
                    "Token": "secret",
                    "Url": "https://example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_nested_prepopulated_polymorphic_dictionary_value_when_only_inner_comparer_is_ignore_case()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Dictionary<string, BaseEndpoint>> Map { get; } = new()
                {
                    ["tenant"] = new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["primary"] = new DerivedEndpoint()
                    }
                };
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 6, 11, 6, 18)
            .WithArguments("App:Map:TENANT:primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "TENANT": {
                    "primary": {
                      "Token": "secret",
                      "Url": "https://primary.example.test"
                    }
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_dictionary_value_when_constructor_prepopulation_is_inside_uninvoked_lambda()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    System.Action later = () => Map["primary"] = new DerivedEndpoint();
                }

                public Dictionary<string, BaseEndpoint> Map { get; } = new();
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "primary": {
                    "Token": "secret",
                    "Url": "https://example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_dictionary_value_when_polymorphic_prepopulation_is_in_unused_constructor_overload()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    Map["primary"] = new BaseEndpoint();
                }

                public AppOptions(string ignored)
                {
                    Map["primary"] = new DerivedEndpoint();
                }

                public Dictionary<string, BaseEndpoint> Map { get; } = new();
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "primary": {
                    "Token": "secret",
                    "Url": "https://primary.example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_clr_member_shaped_child_key_under_string_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, string> Labels { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 17)
            .WithArguments("App:Labels:primary:Length", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Labels": {
                  "primary": {
                    "Length": 5
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_unknown_child_under_null_initialized_settable_nested_property_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public EndpointOptions? Endpoint { get; set; } = null!;
            }

            public class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 13)
            .WithArguments("App:Endpoint:Urll", "EndpointOptions", ". Did you mean \"Url\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_protected_base_constructor_initialized_get_only_object_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions : BaseOptions
            {
            }

            public class BaseOptions
            {
                protected BaseOptions()
                {
                    Endpoint = new EndpointOptions();
                }

                public EndpointOptions Endpoint { get; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 13)
            .WithArguments("App:Endpoint:Urll", "EndpointOptions", ". Did you mean \"Url\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_constructor_initialized_get_only_object_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    Endpoint = new EndpointOptions();
                }

                public EndpointOptions Endpoint { get; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 13)
            .WithArguments("App:Endpoint:Urll", "EndpointOptions", ". Did you mean \"Url\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_this_chained_constructor_initialized_get_only_object_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                    : this("primary")
                {
                }

                public AppOptions(string name)
                {
                    Name = name;
                    Endpoint = new EndpointOptions();
                }

                public string Name { get; }

                public EndpointOptions Endpoint { get; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 13)
            .WithArguments("App:Endpoint:Urll", "EndpointOptions", ". Did you mean \"Url\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_when_constructor_polymorphic_assignment_is_inside_uninvoked_lambda()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    Endpoint = new BaseEndpoint();
                    System.Action later = () => Endpoint = new DerivedEndpoint();
                }

                public BaseEndpoint Endpoint { get; set; }
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 14)
            .WithArguments("App:Endpoint:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Token": "secret",
                  "Url": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_when_polymorphic_constructor_assignment_is_in_unused_overload()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    Endpoint = new BaseEndpoint();
                }

                public AppOptions(string ignored)
                {
                    Endpoint = new DerivedEndpoint();
                }

                public BaseEndpoint Endpoint { get; }
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 14)
            .WithArguments("App:Endpoint:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Token": "secret",
                  "Url": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_when_constructor_polymorphic_assignment_targets_another_instance()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    Endpoint = new BaseEndpoint();
                    var other = new AppOptions("other");
                    other.Endpoint = new DerivedEndpoint();
                }

                private AppOptions(string ignored)
                {
                    Endpoint = new BaseEndpoint();
                }

                public BaseEndpoint Endpoint { get; set; }
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 14)
            .WithArguments("App:Endpoint:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Token": "secret",
                  "Url": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_when_nested_lambda_return_precedes_constructor_initializer()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    System.Action noop = () => { return; };
                    Endpoint = new EndpointOptions();
                }

                public EndpointOptions Endpoint { get; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 13)
            .WithArguments("App:Endpoint:Urll", "EndpointOptions", ". Did you mean \"Url\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_constructor_bound_initialized_get_only_object_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions(string name)
                {
                    Name = name;
                    Endpoint = new EndpointOptions();
                }

                public string Name { get; }

                public EndpointOptions Endpoint { get; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 7, 5, 13)
            .WithArguments("App:Endpoint:Urll", "EndpointOptions", ". Did you mean \"Url\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Name": "primary",
                "Endpoint": {
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_constructor_initialized_get_only_collection_or_dictionary_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    Items = new();
                    Labels = new();
                }

                public List<string> Items { get; }

                public Dictionary<string, string> Labels { get; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Items": [
                  "alpha"
                ],
                "Labels": {
                  "primary": "value"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_get_only_value_type_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public int Port { get; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 12)
            .WithArguments("App:Port:Foo", "Int32", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Port": {
                  "Foo": 443
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_initialized_private_set_scalar_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
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
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_initialized_static_scalar_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public static string ApiKey { get; set; } = "";
            }
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
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_when_strict_assignment_precedes_binder_options_parameter_reassignment()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    options = new Microsoft.Extensions.Configuration.BinderOptions();
                })
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
    public async Task Cfg007_reports_when_nested_binder_options_lambda_assignment_follows_strict_assignment()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    System.Action later = () => options.ErrorOnUnknownConfiguration = false;
                })
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
}
