using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{

    [Fact]
    public async Task Cfg006_does_not_report_constructor_bound_record_property()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed record StripeOptions(string ApiKey);
            """);

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
    public async Task Cfg006_does_not_report_constructor_bound_inherited_property()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public abstract class BaseStripeOptions
            {
                protected BaseStripeOptions(string apiKey)
                {
                    ApiKey = apiKey;
                }

                public string ApiKey { get; }
            }

            public sealed class StripeOptions : BaseStripeOptions
            {
                public StripeOptions(string apiKey, string webhookSecret)
                    : base(apiKey)
                {
                    WebhookSecret = webhookSecret;
                }

                public string WebhookSecret { get; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebhookSecret": "hook"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_does_not_report_nested_constructor_bound_record_property()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed record AppOptions(DatabaseOptions Database);

            public sealed record DatabaseOptions(string ConnectionString);
            """);

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
            """));
    }

    [Fact]
    public async Task Cfg006_reports_configuration_key_name_alias_on_constructor_bound_property()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed record StripeOptions([property: ConfigurationKeyName("api_key")] string ApiKey);
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
    public async Task Cfg006_does_not_report_configuration_key_name_alias_on_settable_constructor_bound_property()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                public StripeOptions(string apiKey)
                {
                    ApiKey = apiKey;
                }

                [ConfigurationKeyName("api_key")]
                public string ApiKey { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "constructor",
                "api_key": "setter"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_does_not_report_configuration_key_name_alias_when_constructor_parameter_has_default()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
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

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "api_key": "setter"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_does_not_report_private_set_configuration_key_name_alias_when_bind_non_public_properties_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.BindNonPublicProperties = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                public StripeOptions(string apiKey)
                {
                    ApiKey = apiKey;
                }

                [ConfigurationKeyName("api_key")]
                public string ApiKey { get; private set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "constructor",
                "api_key": "setter"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_reports_private_set_configuration_key_name_alias_when_bind_non_public_properties_disabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                public StripeOptions(string apiKey)
                {
                    ApiKey = apiKey;
                }

                [ConfigurationKeyName("api_key")]
                public string ApiKey { get; private set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 14)
            .WithArguments("Stripe:api_key", "StripeOptions", ". Did you mean \"ApiKey\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "constructor",
                "api_key": "setter"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_get_only_property_when_public_parameterless_constructor_wins()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public StripeOptions()
                {
                    ApiKey = "";
                }

                public StripeOptions(string apiKey)
                {
                    ApiKey = apiKey;
                }

                public string ApiKey { get; }
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
    public async Task Cfg006_reports_get_only_property_when_constructor_binding_is_ambiguous()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public StripeOptions(string apiKey)
                {
                    ApiKey = apiKey;
                    WebhookSecret = "";
                }

                public StripeOptions(string apiKey, string webhookSecret)
                {
                    ApiKey = apiKey;
                    WebhookSecret = webhookSecret;
                }

                public string ApiKey { get; }

                public string WebhookSecret { get; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

        var secondExpected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 20)
            .WithArguments("Stripe:WebhookSecret", "StripeOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebhookSecret": "hook"
              }
            }
            """),
            expected,
            secondExpected);
    }

    [Fact]
    public async Task Cfg006_does_not_report_private_set_property_when_bind_non_public_properties_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.BindNonPublicProperties = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
            """);

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
    public async Task Cfg006_does_not_report_private_set_property_when_bind_non_public_properties_is_enabled_before_return()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.BindNonPublicProperties = true;
                    return;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
            """);

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
    public async Task Cfg006_does_not_report_bind_get_section_private_set_property_when_bind_non_public_properties_enabled()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Stripe"), options => options.BindNonPublicProperties = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
            """);

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
    public async Task Cfg006_does_not_report_private_set_property_when_parenthesized_bind_non_public_properties_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", (options) => options.BindNonPublicProperties = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
            """);

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
    public async Task Cfg006_reports_private_set_property_when_bind_non_public_properties_is_false()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.BindNonPublicProperties = false)
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
    public async Task Cfg006_reports_private_set_property_when_binder_options_block_does_not_enable_non_public_properties()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => { })
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
    public async Task Cfg006_reports_private_set_property_when_bind_non_public_properties_is_not_constant_true()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.BindNonPublicProperties = IsEnabled())
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraMembers: """
            private static bool IsEnabled() => true;
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
    public async Task Cfg006_reports_private_set_property_when_bind_non_public_properties_assignment_is_not_binder_options()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    var unrelated = new BinderOptionLookalike();
                    unrelated.BindNonPublicProperties = true;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraMembers: """
            private sealed class BinderOptionLookalike
            {
                public bool BindNonPublicProperties { get; set; }
            }
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
    public async Task Cfg006_reports_private_set_property_when_unrelated_binder_options_are_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    var unrelated = new BinderOptions();
                    unrelated.BindNonPublicProperties = true;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
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
    public async Task Cfg006_reports_private_set_property_without_bind_non_public_properties()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
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
}
