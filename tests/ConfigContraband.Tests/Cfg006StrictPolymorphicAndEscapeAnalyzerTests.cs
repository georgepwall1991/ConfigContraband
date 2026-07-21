using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{
    [Fact]
    public async Task Cfg006_stays_info_for_initialized_polymorphic_nested_property_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public BaseEndpoint Endpoint { get; set; } = new DerivedEndpoint();
            }

            public abstract class BaseEndpoint
            {
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 7, 4, 12)
            .WithArguments("App:Endpoint:Url", "BaseEndpoint", ".");

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
    public async Task Cfg006_stays_info_for_null_initialized_get_only_nested_property_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public EndpointOptions? Endpoint { get; } = null!;
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
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
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_initialized_polymorphic_nested_property_with_same_simple_type_name_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public Alpha.Endpoint Endpoint { get; set; } = new Beta.Endpoint();
            }

            namespace Alpha
            {
                public class Endpoint
                {
                    public string Url { get; set; } = "";
                }
            }

            namespace Beta
            {
                public class Endpoint : Alpha.Endpoint
                {
                    public string Token { get; set; } = "";
                }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 7, 4, 14)
            .WithArguments("App:Endpoint:Token", "Endpoint", ".");

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
    public async Task Cfg006_stays_info_for_initialized_polymorphic_nested_property_with_type_alias_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Endpoint = Beta.Endpoint;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Alpha.Endpoint Endpoint { get; set; } = new Endpoint();
            }

            namespace Alpha
            {
                public class Endpoint
                {
                    public string Url { get; set; } = "";
                }
            }

            namespace Beta
            {
                public class Endpoint : Alpha.Endpoint
                {
                    public string Token { get; set; } = "";
                }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 7, 4, 14)
            .WithArguments("App:Endpoint:Token", "Endpoint", ".");

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
    public async Task Cfg006_stays_info_for_constructor_initialized_polymorphic_nested_property_when_strict_binding_is_enabled()
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
                    Endpoint = new DerivedEndpoint();
                }

                public BaseEndpoint Endpoint { get; set; }
            }

            public abstract class BaseEndpoint
            {
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 7, 4, 12)
            .WithArguments("App:Endpoint:Url", "BaseEndpoint", ".");

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
    public async Task Cfg006_stays_info_for_inherited_constructor_initialized_polymorphic_nested_property_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions : BaseOptions
            {
                public AppOptions()
                {
                    Pet = new DogOptions();
                }
            }

            public class BaseOptions
            {
                public PetOptions Pet { get; set; } = new();
            }

            public class PetOptions
            {
                public string Name { get; set; } = "";
            }

            public sealed class DogOptions : PetOptions
            {
                public string Breed { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 7, 4, 14)
            .WithArguments("App:Pet:Breed", "PetOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Pet": {
                  "Breed": "Lab"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_derived_constructor_chains_away_from_base_initializer()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions : BaseOptions
            {
                public AppOptions()
                    : base("skip")
                {
                }
            }

            public class BaseOptions
            {
                protected BaseOptions()
                {
                    Endpoint = new EndpointOptions();
                }

                protected BaseOptions(string ignored)
                {
                }

                public EndpointOptions? Endpoint { get; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
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
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_constructor_initialized_get_only_object_is_in_unused_overload()
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
                }

                public AppOptions(string ignored)
                {
                    Endpoint = new EndpointOptions();
                }

                public EndpointOptions? Endpoint { get; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
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
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_constructor_get_only_object_assignment_is_conditional()
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
                    if (System.DateTime.UtcNow.Ticks < 0)
                    {
                        Endpoint = new EndpointOptions();
                    }
                }

                public EndpointOptions? Endpoint { get; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
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
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_constructor_private_set_object_assignment_is_inside_lambda()
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
                    System.Action initialize = () => Endpoint = new EndpointOptions();
                }

                public EndpointOptions? Endpoint { get; private set; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
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
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_constructor_assignment_targets_shadowing_local()
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
                    EndpointOptions Endpoint;
                    Endpoint = new EndpointOptions();
                }

                public EndpointOptions? Endpoint { get; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
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
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_default_initialized_get_only_clr_only_property_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Uri? Endpoint { get; } = default!;
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
                  "Bad": "example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_uninitialized_get_only_reference_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string? ApiKey { get; }
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
                "ApiKey": {
                  "Foo": "x"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_may_be_skipped_by_early_return()
    {
        var source = OptionsSource("""
            var strict = GetStrict();
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    if (!strict)
                    {
                        return;
                    }

                    options.ErrorOnUnknownConfiguration = true;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraMembers: """
            private static bool GetStrict()
            {
                return false;
            }
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
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_to_false()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    options.ErrorOnUnknownConfiguration = false;
                })
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
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_set_false_via_tuple_deconstruction()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    (options.ErrorOnUnknownConfiguration, options.BindNonPublicProperties) = (false, false);
                })
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
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_changed_by_compound_assignment()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    options.ErrorOnUnknownConfiguration &= false;
                })
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
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_binder_options_parameter_is_reassigned_before_strict_assignment()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options = new Microsoft.Extensions.Configuration.BinderOptions();
                    options.ErrorOnUnknownConfiguration = true;
                })
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
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_tuple_deconstruction_aliases_binder_options_to_another_variable()
    {
        var source = OptionsSource("""
            BinderOptions alias = null!;
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    (options.ErrorOnUnknownConfiguration, alias) = (true, options);
                    alias.ErrorOnUnknownConfiguration = false;
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
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_tuple_deconstruction_reassigns_binder_options_parameter()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    (options.ErrorOnUnknownConfiguration, options) = (true, new BinderOptions());
                    options.ErrorOnUnknownConfiguration = false;
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
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_tuple_deconstruction_captures_binder_options_into_helper_argument()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    int unused;
                    (options.ErrorOnUnknownConfiguration, unused) = (true, Capture(options));
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", extraMembers: """
            private static int Capture(BinderOptions options)
            {
                options.ErrorOnUnknownConfiguration = false;
                return 0;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
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
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_parameter_alias()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    var same = options;
                    options.ErrorOnUnknownConfiguration = true;
                    same.ErrorOnUnknownConfiguration = false;
                })
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
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_nested_parameter_alias()
    {
        var source = OptionsSource("""
            var flag = GetFlag();
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    if (flag)
                    {
                        var alias = options;
                        alias.ErrorOnUnknownConfiguration = false;
                    }
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraMembers: """
            private static bool GetFlag()
            {
                return false;
            }
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
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_helper_escape()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    DisableStrict(options);
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", extraMembers: """
            private static void DisableStrict(BinderOptions options)
            {
                options.ErrorOnUnknownConfiguration = false;
            }
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
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_invoked_local_function()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    void DisableStrict() => options.ErrorOnUnknownConfiguration = false;
                    DisableStrict();
                })
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
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_invoked_delegate()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    System.Action disableStrict = () => options.ErrorOnUnknownConfiguration = false;
                    disableStrict();
                })
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
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_delegate_passed_as_argument()
    {
        // The reset delegate is passed as an argument to a helper that invokes it, so the
        // runtime binder options escape the strict-binding proof just as a directly-invoked
        // reset delegate does. CFG007 must stay conservative (CFG006 Info) rather than fire.
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    System.Action disableStrict = () => options.ErrorOnUnknownConfiguration = false;
                    RunNow(disableStrict);
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraMembers: """
            private static void RunNow(System.Action action) => action();
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
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_inline_lambda_argument()
    {
        // The reset lambda is passed inline as an argument to a helper that invokes it.
        // Same escape as the named-delegate case; CFG007 must stay conservative.
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    RunNow(() => options.ErrorOnUnknownConfiguration = false);
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraMembers: """
            private static void RunNow(System.Action action) => action();
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
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_invoked_delegate_invoke_method()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    System.Action disableStrict = () => options.ErrorOnUnknownConfiguration = false;
                    disableStrict.Invoke();
                })
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
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_conditional_delegate_invoke()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    System.Action? disableStrict = () => options.ErrorOnUnknownConfiguration = false;
                    disableStrict?.Invoke();
                })
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
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_reassigned_invoked_delegate()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    System.Action later = () => { };
                    later = () => options.ErrorOnUnknownConfiguration = false;
                    later();
                })
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
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_extension_helper_escape()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    options.DisableStrict();
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";

                public string WebhookSecret { get; set; } = "";
            }

            public static class BinderOptionsExtensions
            {
                public static void DisableStrict(this BinderOptions options)
                {
                    options.ErrorOnUnknownConfiguration = false;
                }
            }
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
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_escapes_to_constructor()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    _ = new StrictDisabler(options);
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", extraMembers: """
            private sealed class StrictDisabler
            {
                public StrictDisabler(BinderOptions options)
                {
                    options.ErrorOnUnknownConfiguration = false;
                }
            }
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
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_escapes_to_static_field_alias()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    BinderOptionsStore.Stored = options;
                    BinderOptionsStore.Stored.ErrorOnUnknownConfiguration = false;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", extraMembers: """
            private static class BinderOptionsStore
            {
                public static BinderOptions Stored = null!;
            }
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
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_escapes_to_static_property_alias()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    BinderOptionsStore.Stored = options;
                    BinderOptionsStore.Stored.ErrorOnUnknownConfiguration = false;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", extraMembers: """
            private static class BinderOptionsStore
            {
                public static BinderOptions Stored { get; set; } = null!;
            }
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
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_to_non_constant_value()
    {
        var source = OptionsSource("""
            var strict = GetStrict();
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    options.ErrorOnUnknownConfiguration = strict;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraMembers: """
            private static bool GetStrict()
            {
                return false;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
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
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_may_be_reset_in_control_flow()
    {
        var source = OptionsSource("""
            var disableStrict = GetStrict();
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    if (disableStrict)
                    {
                        options.ErrorOnUnknownConfiguration = false;
                    }
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraMembers: """
            private static bool GetStrict()
            {
                return false;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
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
    public async Task Cfg006_stays_info_when_tuple_deconstruction_reset_is_nested_in_control_flow()
    {
        var source = OptionsSource("""
            var disableStrict = GetStrict();
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    if (disableStrict)
                    {
                        (options.ErrorOnUnknownConfiguration, options.BindNonPublicProperties) = (false, false);
                    }
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraMembers: """
            private static bool GetStrict()
            {
                return false;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
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
