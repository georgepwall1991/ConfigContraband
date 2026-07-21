using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{

    [Fact]
    public async Task Cfg006_and_cfg007_report_when_loose_and_strict_registrations_share_section()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>("loose")
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<StripeOptions>("strict")
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expectedInfo = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");
        var expectedWarning = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
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
            expectedInfo,
            expectedWarning);
    }

    [Fact]
    public async Task Cfg006_and_cfg007_report_when_strict_registration_uses_different_section_casing()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>("loose")
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<StripeOptions>("strict")
                .BindConfiguration("stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expectedInfo = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");
        var expectedWarning = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
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
            expectedInfo,
            expectedWarning);
    }

    [Fact]
    public async Task Cfg006_reports_loose_registration_when_matching_strict_cfg007_is_disabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>("loose")
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<StripeOptions>("strict")
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            DiagnosticIds.UnknownConfigurationKeyWillThrow,
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_loose_registration_when_matching_strict_cfg007_is_disabled_by_analyzer_config()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>("loose")
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<StripeOptions>("strict")
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerWithAnalyzerConfigAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            """
            is_global = true
            dotnet_diagnostic.CFG007.severity = none
            """,
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_strict_registration_when_cfg007_is_disabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            DiagnosticIds.UnknownConfigurationKeyWillThrow,
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_loose_registration_when_strict_twin_uses_different_binder_options()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>("loose")
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<StripeOptions>("strict")
                .BindConfiguration("Stripe", options =>
                {
                    options.BindNonPublicProperties = true;
                    options.ErrorOnUnknownConfiguration = true;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_is_not_suppressed_by_matching_strict_registration_in_generated_code()
    {
        var looseSource = OptionsSource("""
            services.AddOptions<StripeOptions>("loose")
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);
        var generatedSource = """
            using Microsoft.Extensions.DependencyInjection;

            public sealed class GeneratedStartup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>("strict")
                        .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
                }
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            [
                ("Startup.cs", looseSource),
                ("Generated.g.cs", generatedSource)
            ],
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
    public async Task Cfg006_does_not_suggest_strict_rejected_alias_for_clr_property_key_when_error_on_unknown_configuration_is_enabled()
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_initialized_object_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class Endpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class AppOptions
            {
                public object Endpoint { get; } = new Endpoint();
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 15)
            .WithArguments("App:Endpoint", "AppOptions", ".");

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
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_null_get_only_nullable_clr_property_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public int? Port { get; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 11)
            .WithArguments("App:Port", "AppOptions", ".");

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
    public async Task Cfg006_stays_quiet_under_guid_keyed_dictionary_values()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<Guid, ServerOptions> Servers { get; set; } = [];
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
                  "3fa85f64-5717-4562-b3fc-2c963f66afa6": {
                    "Host": "example.test",
                    "Prt": 443
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_stays_info_for_prepopulated_polymorphic_dictionary_value_when_strict_binding_is_enabled()
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
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
    public async Task Cfg006_stays_info_for_opaque_prepopulated_polymorphic_dictionary_value_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, BaseEndpoint> Map { get; } = CreateMap();

                private static Dictionary<string, BaseEndpoint> CreateMap()
                {
                    return new()
                    {
                        ["primary"] = new DerivedEndpoint()
                    };
                }
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
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
    public async Task Cfg006_stays_info_for_case_mismatched_prepopulated_polymorphic_dictionary_value_when_comparer_is_ignore_case()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, BaseEndpoint> Map { get; } = new(StringComparer.OrdinalIgnoreCase)
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
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
    public async Task Cfg006_stays_info_for_nested_prepopulated_polymorphic_dictionary_value_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Dictionary<string, BaseEndpoint>> Map { get; } = new()
                {
                    ["tenant"] = new()
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 6, 11, 6, 18)
            .WithArguments("App:Map:tenant:primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "tenant": {
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
    public async Task Cfg006_stays_info_for_nested_prepopulated_polymorphic_dictionary_value_when_outer_comparer_is_ignore_case()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Dictionary<string, BaseEndpoint>> Map { get; } = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["tenant"] = new()
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
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
    public async Task Cfg006_stays_info_for_nested_prepopulated_polymorphic_dictionary_value_when_inner_comparer_matches_case_mismatch()
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 6, 11, 6, 18)
            .WithArguments("App:Map:tenant:PRIMARY:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "tenant": {
                    "PRIMARY": {
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
    public async Task Cfg006_stays_info_for_nested_prepopulated_polymorphic_dictionary_value_when_inner_comparer_is_assigned_in_constructor()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    Map["tenant"] = new(StringComparer.OrdinalIgnoreCase);
                    Map["tenant"]["primary"] = new DerivedEndpoint();
                }

                public Dictionary<string, Dictionary<string, BaseEndpoint>> Map { get; } = new();
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 6, 11, 6, 18)
            .WithArguments("App:Map:tenant:PRIMARY:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "tenant": {
                    "PRIMARY": {
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
    public async Task Cfg006_stays_info_for_constructor_prepopulated_polymorphic_dictionary_values_when_strict_binding_is_enabled()
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
                    Map["primary"] = new DerivedEndpoint();
                    Map.Add("secondary", new DerivedEndpoint());
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

        var expectedPrimary = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:primary:Token", "BaseEndpoint", ".");
        var expectedSecondary = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
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
    public async Task Cfg006_stays_info_for_case_mismatched_constructor_prepopulated_polymorphic_dictionary_value_when_comparer_is_ignore_case()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

                public AppOptions()
                {
                    Map["primary"] = new DerivedEndpoint();
                    Map.Add("secondary", new DerivedEndpoint());
                }

                public Dictionary<string, BaseEndpoint> Map { get; } = new(Comparer);
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string AccessCode { get; set; } = "";
            }
            """);

        var expectedPrimary = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 5, 9, 5, 21)
            .WithArguments("App:Map:Primary:AccessCode", "BaseEndpoint", ".");
        var expectedSecondary = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 9, 9, 9, 21)
            .WithArguments("App:Map:Secondary:AccessCode", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "Primary": {
                    "AccessCode": "value",
                    "Url": "https://primary.example.test"
                  },
                  "Secondary": {
                    "AccessCode": "value",
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
    public async Task Cfg006_stays_info_for_case_mismatched_constructor_assigned_polymorphic_dictionary_value_when_comparer_is_ignore_case()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    Map = new(StringComparer.OrdinalIgnoreCase);
                    Map["primary"] = new DerivedEndpoint();
                }

                public Dictionary<string, BaseEndpoint> Map { get; }
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
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
                    "Url": "https://primary.example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_case_mismatched_constructor_assigned_polymorphic_dictionary_value_when_iequalitycomparer_local_is_ignore_case()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    IEqualityComparer<string> comparer = StringComparer.OrdinalIgnoreCase;
                    Map = new(comparer);
                    Map["primary"] = new DerivedEndpoint();
                }

                public Dictionary<string, BaseEndpoint> Map { get; }
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
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
                    "Url": "https://primary.example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_case_mismatched_constructor_assigned_polymorphic_dictionary_value_when_iequalitycomparer_field_is_ignore_case()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                private static readonly IEqualityComparer<string> Comparer = StringComparer.OrdinalIgnoreCase;

                public AppOptions()
                {
                    Map = new(Comparer);
                    Map["primary"] = new DerivedEndpoint();
                }

                public Dictionary<string, BaseEndpoint> Map { get; }
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
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
                    "Url": "https://primary.example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_constructor_bound_prepopulated_polymorphic_dictionary_value_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions(string name)
                {
                    Name = name;
                    Map["primary"] = new DerivedEndpoint();
                }

                public string Name { get; }

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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 6, 9, 6, 16)
            .WithArguments("App:Map:primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Name": "primary",
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
    public async Task Cfg006_stays_info_when_unrelated_binder_options_enable_error_on_unknown_configuration()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    var unrelated = new BinderOptions();
                    unrelated.ErrorOnUnknownConfiguration = true;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_strict_binding_ignores_private_set_property_name()
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_strict_binding_ignores_private_property_name()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                private string ApiKey { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_strict_binding_ignores_get_only_scalar_property_name()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret"
              }
            }
            """),
            expected);
    }

}
