using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{

    [Fact]
    public async Task Cfg004_reports_data_annotations_without_validate_data_annotations()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_reports_named_options_builder_data_annotations_without_validate_data_annotations()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>("tenant")
                .BindConfiguration("Stripe")
                .ValidateOnStart()|};
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_reports_constructor_bound_data_annotations_without_validate_data_annotations()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateOnStart()|};
            """, optionsTypes: """
            public sealed record StripeOptions([property: Required] string ApiKey);
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_does_not_report_ambiguous_constructor_bound_data_annotations()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
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

                [Required]
                public string ApiKey { get; }

                public string WebhookSecret { get; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg004_does_not_treat_custom_validate_data_annotations_extension_as_data_annotations_validation()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations("noop")
                .ValidateOnStart()|};
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }

            public static class CustomOptionsBuilderExtensions
            {
                public static OptionsBuilder<TOptions> ValidateDataAnnotations<TOptions>(
                    this OptionsBuilder<TOptions> builder,
                    string marker)
                    where TOptions : class
                {
                    return builder;
                }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_honors_validate_data_annotations_before_bind_configuration()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .ValidateDataAnnotations()
                .BindConfiguration("Stripe")
                .ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg004_reports_bind_get_section_data_annotations_without_validate_data_annotations()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            {|#0:services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Stripe"))|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_reports_inherited_data_annotations_without_validate_data_annotations()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateOnStart()|};
            """, optionsTypes: """
            public class BaseStripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }

            public sealed class StripeOptions : BaseStripeOptions
            {
                public string WebhookSecret { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_reports_constructor_bound_inherited_data_annotations_without_validate_data_annotations()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateOnStart()|};
            """, optionsTypes: """
            public abstract class BaseStripeOptions
            {
                protected BaseStripeOptions(string apiKey)
                {
                    ApiKey = apiKey;
                }

                [Required]
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_does_not_report_inherited_data_annotations_when_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public class BaseStripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }

            public sealed class StripeOptions : BaseStripeOptions
            {
                public string WebhookSecret { get; set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg004_reports_validatable_object_without_validate_data_annotations()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateOnStart()|};
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class StripeOptions : IValidatableObject
            {
                public string ApiKey { get; set; } = "";

                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                {
                    yield break;
                }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_reports_type_level_validation_attribute_without_validate_data_annotations()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateOnStart()|};
            """, extraUsings: "using System;\n", optionsTypes: """
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class ValidStripeOptionsAttribute : ValidationAttribute
            {
                protected override ValidationResult IsValid(object value, ValidationContext validationContext)
                {
                    return ValidationResult.Success!;
                }
            }

            [ValidStripeOptions]
            public sealed class StripeOptions
            {
                public string ApiKey { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_reports_type_level_validation_attribute_declared_on_base_type_without_validate_data_annotations()
    {
        // Validator.TryValidateObject evaluates inherited class-level attributes by default
        // (AttributeUsageAttribute.Inherited defaults to true), so a type-level attribute
        // declared only on a base class still needs ValidateDataAnnotations() enabled.
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateOnStart()|};
            """, extraUsings: "using System;\n", optionsTypes: """
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class ValidStripeOptionsAttribute : ValidationAttribute
            {
                protected override ValidationResult IsValid(object value, ValidationContext validationContext)
                {
                    return ValidationResult.Success!;
                }
            }

            [ValidStripeOptions]
            public class StripeOptionsBase
            {
            }

            public sealed class StripeOptions : StripeOptionsBase
            {
                public string ApiKey { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_reports_nested_data_annotations_without_validate_data_annotations()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateOnStart()|};
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class AppOptions
            {
                [ValidateObjectMembers]
                public DatabaseOptions Database { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_reports_initialized_get_only_nested_data_annotations_without_validate_data_annotations()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateOnStart()|};
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class AppOptions
            {
                [ValidateObjectMembers]
                public DatabaseOptions Database { get; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_reports_constructor_initialized_get_only_nested_data_annotations_without_validate_data_annotations()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateOnStart()|};
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    Database = new();
                }

                [ValidateObjectMembers]
                public DatabaseOptions Database { get; }
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_ignores_uninitialized_get_only_nested_data_annotations()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class AppOptions
            {
                [ValidateObjectMembers]
                public DatabaseOptions Database { get; }
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg004_reports_private_set_data_annotations_when_bind_non_public_properties_enabled()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.BindNonPublicProperties = true)
                .ValidateOnStart()|};
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; private set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_reports_private_set_data_annotations_when_bind_non_public_properties_is_enabled_before_return()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.BindNonPublicProperties = true;
                    return;
                })
                .ValidateOnStart()|};
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; private set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_reports_bind_get_section_private_set_data_annotations_when_bind_non_public_properties_enabled()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            {|#0:services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Stripe"), options => options.BindNonPublicProperties = true)
                .ValidateOnStart()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; private set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_ignores_private_set_data_annotations_without_bind_non_public_properties()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; private set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg004_ignores_private_set_data_annotations_when_unrelated_binder_options_are_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    var unrelated = new BinderOptions();
                    unrelated.BindNonPublicProperties = true;
                })
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; private set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg004_does_not_report_validatable_object_when_data_annotations_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class StripeOptions : IValidatableObject
            {
                public string ApiKey { get; set; } = "";

                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                {
                    yield break;
                }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg004_reports_later_local_bind_statement_validate_on_start_without_data_annotations()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            optionsBuilder.ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_honors_later_local_bind_statement_validation_after_unrelated_statement()
    {
        // ValidateDataAnnotations() is genuinely called on the same builder after an
        // unrelated statement, so DataAnnotations validation is enabled at runtime and
        // CFG004 must stay quiet. The forward split-local scan (shared with CFG003) now
        // skips the intervening statement instead of stopping and reporting a false
        // positive.
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            optionsBuilder.BindConfiguration("Stripe");
            Validate(optionsBuilder);
            optionsBuilder.ValidateDataAnnotations();
            optionsBuilder.ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", extraMembers: """
            private static void Validate(OptionsBuilder<StripeOptions> optionsBuilder)
            {
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg004_reports_when_prior_validation_is_skippable_via_goto_label_before_bind()
    {
        // A `goto` can jump over the validation straight to a label before the bind, so reaching the
        // bind does not prove the earlier ValidateDataAnnotations() ran. The backward scan must stop
        // at the labelled statement rather than collect the pre-label validation, so CFG004 fires.
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            if (services.Count > 0)
            {
                goto Bind;
            }
            optionsBuilder.ValidateDataAnnotations();
            Bind:
            {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            optionsBuilder.ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_reports_when_builder_retargeted_before_bind_drops_prior_validation()
    {
        // ValidateDataAnnotations() is called on the first builder, then the local is reassigned to a
        // new builder, then the bind. The prior validation belongs to the discarded first builder, so
        // the backward scan must stop at the reassignment and not attribute it to the bound builder —
        // CFG004 must fire because the bound builder has no DataAnnotations validation.
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            optionsBuilder.ValidateDataAnnotations();
            optionsBuilder = services.AddOptions<StripeOptions>();
            {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            optionsBuilder.ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_honors_prior_local_validation_across_unrelated_statement()
    {
        // ValidateDataAnnotations() is genuinely called on the builder before the bind, separated
        // by an unrelated statement (a call passing the builder by value, which cannot retarget it).
        // The backward scan now skips the inert statement and collects the earlier validation, so
        // CFG004 must stay quiet — it previously mis-fired (the backward mirror of the forward
        // split-local false positive).
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            optionsBuilder.ValidateDataAnnotations();
            Validate(optionsBuilder);
            optionsBuilder.BindConfiguration("Stripe");
            optionsBuilder.ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", extraMembers: """
            private static void Validate(OptionsBuilder<StripeOptions> optionsBuilder)
            {
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg004_honors_data_annotations_from_local_builder_initializer_before_later_bind_statement()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>()
                .ValidateDataAnnotations();
            optionsBuilder.BindConfiguration("Stripe");
            optionsBuilder.ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg004_honors_local_builder_initializer_validation_across_unrelated_statement()
    {
        // ValidateDataAnnotations() is in the builder's declaration initializer, then an unrelated
        // statement, then the bind. The backward scan now skips the inert statement, reaches the
        // builder's declaration, and collects the initializer chain, so CFG004 stays quiet — it
        // previously mis-fired (the backward mirror of the forward split-local false positive).
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>()
                .ValidateDataAnnotations();
            Validate(optionsBuilder);
            optionsBuilder.BindConfiguration("Stripe");
            optionsBuilder.ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", extraMembers: """
            private static void Validate(OptionsBuilder<StripeOptions> optionsBuilder)
            {
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg004_reports_split_validate_on_start_without_data_annotations()
    {
        var source = OptionsSource("""
            var optionsBuilder = {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            optionsBuilder.ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }
}
