using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{

    [Fact]
    public async Task Cfg003_reports_validation_without_validate_on_start()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()|};
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_named_options_builder_validation_without_validate_on_start()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>("tenant")
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()|};
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_honors_add_options_with_validate_on_start()
    {
        var source = OptionsSource("""
            services.AddOptionsWithValidateOnStart<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_honors_validate_on_start_before_bind_configuration()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .ValidateOnStart()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_does_not_treat_custom_validate_on_start_extension_as_startup_validation()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart("noop")|};
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }

            public static class CustomOptionsBuilderExtensions
            {
                public static OptionsBuilder<TOptions> ValidateOnStart<TOptions>(
                    this OptionsBuilder<TOptions> builder,
                    string marker)
                    where TOptions : class
                {
                    return builder;
                }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_validation_before_bind_configuration_without_validate_on_start()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .ValidateDataAnnotations()
                .BindConfiguration("Stripe")|};
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_factory_created_builder_without_validate_on_start()
    {
        var source = OptionsSource("""
            {|#0:new BuilderFactory()
                .Create<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()|};
            """, extraUsings: "using Microsoft.Extensions.Options;\n", extraMembers: """
            private sealed class BuilderFactory
            {
                public OptionsBuilder<TOptions> Create<TOptions>()
                    where TOptions : class
                {
                    throw new System.NotImplementedException();
                }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_bind_get_section_validation_without_validate_on_start()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            {|#0:services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Stripe"))
                .ValidateDataAnnotations()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_does_not_treat_custom_validate_extension_as_options_validation()
    {
        var source = OptionsSource("""
            services.AddOptions<PlainOptions>()
                .BindConfiguration("Plain")
                .Validate("noop");
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class PlainOptions
            {
                public string Value { get; set; } = "";
            }

            public static class CustomOptionsBuilderExtensions
            {
                public static OptionsBuilder<TOptions> Validate<TOptions>(
                    this OptionsBuilder<TOptions> builder,
                    string marker)
                    where TOptions : class
                {
                    return builder;
                }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_honors_chained_split_local_registration_chain()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<PlainOptions>()
                .BindConfiguration("Plain");
            optionsBuilder.Validate(options => true).ValidateOnStart();
            """, optionsTypes: """
            public sealed class PlainOptions
            {
                public string Value { get; set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_stops_split_local_scan_at_unrelated_invocation()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<PlainOptions>()
                .BindConfiguration("Plain");
            Validate(optionsBuilder);
            optionsBuilder.ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", extraMembers: """
            private static void Validate(OptionsBuilder<PlainOptions> optionsBuilder)
            {
            }
            """, optionsTypes: """
            public sealed class PlainOptions
            {
                public string Value { get; set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_does_not_report_when_validate_on_start_follows_unrelated_statement()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            optionsBuilder.ValidateDataAnnotations();
            services.AddSingleton<Startup>();
            optionsBuilder.ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_reports_when_builder_local_reassigned_before_validate_on_start()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            optionsBuilder.ValidateDataAnnotations();
            optionsBuilder = services.AddOptions<StripeOptions>();
            optionsBuilder.ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_when_builder_local_retargeted_by_deconstruction_before_validate_on_start()
    {
        // The builder is retargeted through a tuple-deconstruction assignment before
        // ValidateOnStart(), so the later call applies to a different builder. The scan
        // must recognize the local as a deconstruction target and stop, not treat the
        // statement as inert.
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            optionsBuilder.ValidateDataAnnotations();
            (optionsBuilder, _) = (services.AddOptions<StripeOptions>(), 0);
            optionsBuilder.ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_does_not_report_when_deferred_lambda_reassigns_builder_after_validate_on_start()
    {
        // The only reassignment of the builder lives inside a deferred lambda that is
        // not invoked until after ValidateOnStart(), so the builder is not retargeted at
        // the validation point. The retarget scan must not descend into the lambda body,
        // otherwise it would treat the deferred assignment as immediate and false-fire.
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            optionsBuilder.ValidateDataAnnotations();
            System.Action reset = () => optionsBuilder = services.AddOptions<StripeOptions>();
            optionsBuilder.ValidateOnStart();
            reset();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_reports_when_builder_local_passed_by_ref_before_validate_on_start()
    {
        // Passing the builder local by ref lets the callee repoint it, so a later
        // ValidateOnStart() may apply to a different builder. The forward scan must stop
        // at the ref call rather than treat it as an inert intervening statement.
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            optionsBuilder.ValidateDataAnnotations();
            Reset(ref optionsBuilder);
            optionsBuilder.ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", extraMembers: """
            private static void Reset(ref OptionsBuilder<StripeOptions> optionsBuilder)
            {
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_when_validate_on_start_is_behind_conditional_return()
    {
        // ValidateOnStart() sits behind a conditional early return, so it does not run
        // on every path. The forward split-local scan must stop at control flow rather
        // than skip it, otherwise a genuine missing-startup-validation case is hidden.
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            optionsBuilder.ValidateDataAnnotations();
            if (services is null) return;
            optionsBuilder.ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_and_cfg004_honor_split_local_registration_chain()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            optionsBuilder.ValidateDataAnnotations();
            optionsBuilder.ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_and_cfg004_honor_later_local_bind_statement_chain()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            optionsBuilder.BindConfiguration("Stripe");
            optionsBuilder.ValidateDataAnnotations();
            optionsBuilder.ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_reports_later_local_bind_statement_validation_without_validate_on_start()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            optionsBuilder.ValidateDataAnnotations();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_and_cfg004_honor_validation_before_bind_across_unrelated_statement()
    {
        // The backward mirror of the forward split-local fix: a validation call placed *before* the
        // bind, separated from it by an unrelated statement. The prior-scan must skip the inert
        // statement and still collect the earlier ValidateDataAnnotations(), so neither CFG003 nor
        // CFG004 fires (all validation and startup registration are present).
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            optionsBuilder.ValidateDataAnnotations();
            services.AddSingleton<Startup>();
            optionsBuilder.BindConfiguration("Stripe");
            optionsBuilder.ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_and_cfg004_honor_validation_before_bind_across_control_flow()
    {
        // The prior validation is a top-level unconditional statement, then a control-flow statement,
        // then the bind. The earlier validation always runs before the bind is reached, so the
        // backward scan must continue past the control-flow statement and collect it — control flow
        // does not stop the backward scan (only a retarget or the builder's declaration does).
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            optionsBuilder.ValidateDataAnnotations();
            if (services.Count > 0)
            {
                services.AddSingleton<Startup>();
            }
            optionsBuilder.BindConfiguration("Stripe");
            optionsBuilder.ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_reports_parameter_typed_builder_split_validation_without_validate_on_start()
    {
        // The builder is a method parameter and its bind/validation calls are split across separate
        // statements (not a single fluent chain). Validation is present without ValidateOnStart, so
        // CFG003 must fire — the split-statement scan must track a parameter receiver, not only a
        // local variable.
        var source = OptionsSource("", extraUsings: "using Microsoft.Extensions.Options;\n", extraMembers: """
            private static void ConfigureBuilder(OptionsBuilder<StripeOptions> builder)
            {
                {|#0:builder.BindConfiguration("Stripe")|};
                builder.ValidateDataAnnotations();
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_does_not_report_parameter_typed_builder_split_validation_with_validate_on_start()
    {
        // Same parameter-typed split shape but with ValidateOnStart present — the scan must collect
        // the later ValidateOnStart() call on the parameter receiver and stay quiet.
        var source = OptionsSource("", extraUsings: "using Microsoft.Extensions.Options;\n", extraMembers: """
            private static void ConfigureBuilder(OptionsBuilder<StripeOptions> builder)
            {
                builder.BindConfiguration("Stripe");
                builder.ValidateDataAnnotations();
                builder.ValidateOnStart();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_and_cfg004_honor_validation_before_later_local_bind_statement()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            optionsBuilder.ValidateDataAnnotations();
            optionsBuilder.BindConfiguration("Stripe");
            optionsBuilder.ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_reports_validation_before_later_local_bind_statement_without_validate_on_start()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            optionsBuilder.ValidateDataAnnotations();
            {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_honors_validate_on_start_from_local_builder_initializer_before_later_bind_statement()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptionsWithValidateOnStart<StripeOptions>();
            optionsBuilder.BindConfiguration("Stripe");
            optionsBuilder.ValidateDataAnnotations();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_reports_local_builder_initializer_validation_without_validate_on_start()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>()
                .ValidateDataAnnotations();
            {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_split_custom_validation_without_validate_on_start()
    {
        var source = OptionsSource("""
            var optionsBuilder = {|#0:services.AddOptions<PlainOptions>()
                .BindConfiguration("Plain")|};
            optionsBuilder.Validate(options => true);
            """, optionsTypes: """
            public sealed class PlainOptions
            {
                public string Value { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("PlainOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }
}
