using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{
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
