using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{
    [Fact]
    public async Task Cfg003_reports_when_options_validator_is_chained_in_a_field_initializer()
    {
        var source = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                private static readonly IServiceCollection Services = new ServiceCollection();

                public static readonly OptionsBuilder<StripeOptions> Builder =
                    {|#0:Services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>()
                        .AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe")|};
            }

            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }

            [OptionsValidator]
            public sealed class ValidateStripeOptions : IValidateOptions<StripeOptions>
            {
                public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg003_reports_when_options_validator_is_chained_in_an_expression_bodied_member()
    {
        var source = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public OptionsBuilder<StripeOptions> Add(IServiceCollection services) =>
                    {|#0:services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>()
                        .AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe")|};
            }

            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }

            [OptionsValidator]
            public sealed class ValidateStripeOptions : IValidateOptions<StripeOptions>
            {
                public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_when_options_validator_registration_is_short_circuited_with_or()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            _ = false || services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>() is not null;
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_when_options_validator_registration_is_short_circuited()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            _ = true && services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>() is not null;
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_for_constructor_initializer_bind_without_a_chained_validator()
    {
        var source = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public abstract class StartupBase
            {
                protected StartupBase(OptionsBuilder<StripeOptions> builder)
                {
                }
            }

            public sealed class Startup : StartupBase
            {
                public Startup(IServiceCollection services)
                    : base({|#0:services.AddOptions<StripeOptions>().BindConfiguration("Stripe")|})
                {
                }
            }

            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_when_options_validator_is_registered_before_bind()
    {
        var source = OptionsSource("""
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg003_reports_when_options_validator_is_chained_into_add_options_bind()
    {
        var source = OptionsSource("""
            {|#0:services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>()
                .AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg003_reports_when_options_validator_is_registered_for_bind_get_required_section()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            {|#0:services.AddOptions<StripeOptions>()
                .Bind(configuration.GetRequiredSection("Stripe"))|};
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg003_reports_empty_string_named_options_when_options_validator_is_registered()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>("")
                .BindConfiguration("Stripe")|};
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg003_reports_default_name_options_when_options_validator_is_registered()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>(Options.DefaultName)
                .BindConfiguration("Stripe")|};
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg003_reports_each_bind_when_two_options_types_share_a_block_and_only_one_has_a_validator()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            {|#1:services.AddOptions<BillingOptions>()
                .BindConfiguration("Billing")|};
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }

            public sealed class BillingOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";
            }

            [OptionsValidator]
            public sealed class ValidateStripeOptions : IValidateOptions<StripeOptions>
            {
                public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
            }
            """);

        var stripe = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");
        var billing = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(1)
            .WithArguments("BillingOptions");

        await Verifier.VerifyAnalyzerAsync(source, stripe, billing);
    }

    [Fact]
    public async Task Cfg003_reports_bindless_validate_when_options_validator_is_registered_without_validate_on_start()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<StripeOptions>(configuration.GetSection("Stripe"));
            {|#0:services.AddOptions<StripeOptions>()
                .Validate(_ => true)|};
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg003_and_cfg004_stay_quiet_for_named_add_options_with_validate_on_start()
    {
        var source = OptionsSource("""
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>()
                .AddOptionsWithValidateOnStart<StripeOptions>("tenant")
                .BindConfiguration("Stripe");
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings);
    }

    [Fact]
    public async Task Cfg004_stays_quiet_for_partial_options_validator_class()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }

            [OptionsValidator]
            public partial class ValidateStripeOptions : IValidateOptions<StripeOptions>
            {
                public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg004_stays_quiet_for_nested_options_validator_class()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.AddSingleton<IValidateOptions<StripeOptions>, Validators.ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }

            public static class Validators
            {
                [OptionsValidator]
                public sealed class ValidateStripeOptions : IValidateOptions<StripeOptions>
                {
                    public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
                }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg004_stays_quiet_when_validator_inherits_ivalidate_options()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }

            public abstract class ValidateStripeOptionsBase : IValidateOptions<StripeOptions>
            {
                public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
            }

            [OptionsValidator]
            public sealed class ValidateStripeOptions : ValidateStripeOptionsBase
            {
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg004_stays_quiet_for_constructed_generic_options_validator()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateOptions<StripeOptions>>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }

            [OptionsValidator]
            public sealed class ValidateOptions<T> : IValidateOptions<T> where T : class
            {
                public ValidateOptionsResult Validate(string? name, T options) => ValidateOptionsResult.Success;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg004_stays_quiet_for_parenthesized_service_collection_receiver()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            (services).AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg004_stays_quiet_for_null_forgiving_service_collection_receiver()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services!.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_when_options_validator_attribute_is_only_on_the_base_type()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }

            [OptionsValidator]
            public abstract class ValidateStripeOptionsBase : IValidateOptions<StripeOptions>
            {
                public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
            }

            public sealed class ValidateStripeOptions : ValidateStripeOptionsBase
            {
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_for_add_transient_options_validator()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.AddTransient<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_for_add_scoped_options_validator()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.AddScoped<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_for_instance_add_singleton_options_validator()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.AddSingleton<IValidateOptions<StripeOptions>>(new ValidateStripeOptions());
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_for_non_generic_add_singleton_options_validator()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.AddSingleton(typeof(IValidateOptions<StripeOptions>), typeof(ValidateStripeOptions));
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_for_try_add_enumerable_transient_options_validator()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.TryAddEnumerable(ServiceDescriptor.Transient<IValidateOptions<StripeOptions>, ValidateStripeOptions>());
            """, extraUsings: "using Microsoft.Extensions.DependencyInjection.Extensions;\nusing Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_for_try_add_enumerable_factory_options_validator()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<StripeOptions>>(_ => new ValidateStripeOptions()));
            """, extraUsings: "using Microsoft.Extensions.DependencyInjection.Extensions;\nusing Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_for_try_add_enumerable_local_descriptor()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            var descriptor = ServiceDescriptor.Singleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            services.TryAddEnumerable(descriptor);
            """, extraUsings: "using Microsoft.Extensions.DependencyInjection.Extensions;\nusing Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_for_try_add_enumerable_describe()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.TryAddEnumerable(ServiceDescriptor.Describe(
                typeof(IValidateOptions<StripeOptions>),
                typeof(ValidateStripeOptions),
                ServiceLifetime.Singleton));
            """, extraUsings: "using Microsoft.Extensions.DependencyInjection.Extensions;\nusing Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_when_options_validator_registration_is_in_try_catch()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            try
            {
                services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            }
            catch (System.Exception)
            {
            }
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_when_options_validator_registration_is_in_a_switch_expression()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            _ = true switch
            {
                true => services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>(),
                _ => services
            };
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_when_options_validator_registration_is_in_a_ternary()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            _ = true
                ? services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>()
                : services;
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_when_options_validator_registration_is_coalesced()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            _ = null ?? services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_when_options_validator_is_registered_on_an_unproven_field_receiver()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            other.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", extraMembers: """
            private readonly IServiceCollection other = new ServiceCollection();
            """, optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_when_dual_interface_validator_is_registered_for_the_other_type()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.AddSingleton<IValidateOptions<BillingOptions>, ValidateBothOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }

            public sealed class BillingOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";
            }

            [OptionsValidator]
            public sealed class ValidateBothOptions : IValidateOptions<StripeOptions>, IValidateOptions<BillingOptions>
            {
                public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;

                public ValidateOptionsResult Validate(string? name, BillingOptions options) => ValidateOptionsResult.Success;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg002_reports_named_options_when_options_validator_is_registered()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>("tenant")
                .BindConfiguration({|#0:"Stripe"|})
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Stripe");

        await Verifier.VerifyAnalyzerAsync(source, MissingApiKeyAppsettings, expected);
    }

    [Fact]
    public async Task Cfg002_reports_when_options_validator_uses_try_add_enumerable()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration({|#0:"Stripe"|})
                .ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>());
            """, extraUsings: "using Microsoft.Extensions.DependencyInjection.Extensions;\nusing Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Stripe");

        await Verifier.VerifyAnalyzerAsync(source, MissingApiKeyAppsettings, expected);
    }

    [Fact]
    public async Task Cfg002_reports_direct_configure_default_name_when_options_validator_is_registered()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<StripeOptions>(Options.DefaultName, configuration.GetSection({|#0:"Stripe"|}));
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Stripe");

        await Verifier.VerifyAnalyzerAsync(source, MissingApiKeyAppsettings, expected);
    }

    [Fact]
    public async Task Cfg002_reports_direct_configure_empty_string_name_when_options_validator_is_registered()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<StripeOptions>(string.Empty, configuration.GetSection({|#0:"Stripe"|}));
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Stripe");

        await Verifier.VerifyAnalyzerAsync(source, MissingApiKeyAppsettings, expected);
    }

    [Fact]
    public async Task Cfg002_reports_bind_get_required_section_when_options_validator_is_registered()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetRequiredSection({|#0:"Stripe"|}))
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Stripe");

        await Verifier.VerifyAnalyzerAsync(source, MissingApiKeyAppsettings, expected);
    }

    [Fact]
    public async Task Cfg010_reports_out_of_range_for_configure_when_options_validator_is_registered()
    {
        var source = OptionsSource(
            """
            IConfiguration configuration = null!;
            services.Configure<ServerOptions>(configuration.GetSection("Server"));
            services.AddSingleton<IValidateOptions<ServerOptions>, ValidateServerOptions>();
            """,
            extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n",
            optionsTypes: ServerValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueFailsValidation)
            .WithSpan("appsettings.json", 3, 13, 3, 14)
            .WithArguments("Server:Port", "Range", "ServerOptions");

        await Verifier.VerifyAnalyzerAsync(source, ServerOutOfRangeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg010_reports_out_of_range_when_options_validator_uses_try_add_enumerable()
    {
        var source = OptionsSource(
            """
            services.AddOptions<ServerOptions>()
                .BindConfiguration("Server")
                .ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ServerOptions>, ValidateServerOptions>());
            """,
            extraUsings: "using Microsoft.Extensions.DependencyInjection.Extensions;\nusing Microsoft.Extensions.Options;\n",
            optionsTypes: ServerValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueFailsValidation)
            .WithSpan("appsettings.json", 3, 13, 3, 14)
            .WithArguments("Server:Port", "Range", "ServerOptions");

        await Verifier.VerifyAnalyzerAsync(source, ServerOutOfRangeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg010_stays_quiet_for_in_range_value_when_options_validator_is_registered()
    {
        var source = OptionsSource(
            """
            services.AddOptions<ServerOptions>()
                .BindConfiguration("Server")
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<ServerOptions>, ValidateServerOptions>();
            """,
            extraUsings: "using Microsoft.Extensions.Options;\n",
            optionsTypes: ServerValidatorTypes);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": 8080
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg005_reports_nested_object_when_options_validator_is_registered()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateOnStart()|};
            services.AddSingleton<IValidateOptions<AppOptions>, ValidateAppOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions {|#1:Database|} { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";
            }

            [OptionsValidator]
            public sealed class ValidateAppOptions : IValidateOptions<AppOptions>
            {
                public ValidateOptionsResult Validate(string? name, AppOptions options) => ValidateOptionsResult.Success;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg005_reports_nested_object_for_configure_when_options_validator_is_registered()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            {|#0:services.Configure<AppOptions>(configuration.GetSection("App"))|};
            services.AddSingleton<IValidateOptions<AppOptions>, ValidateAppOptions>();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions {|#1:Database|} { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";
            }

            [OptionsValidator]
            public sealed class ValidateAppOptions : IValidateOptions<AppOptions>
            {
                public ValidateOptionsResult Validate(string? name, AppOptions options) => ValidateOptionsResult.Success;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_when_options_validator_registration_is_in_a_switch_statement()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            switch (true)
            {
                case true:
                    services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
                    break;
            }
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_when_options_validator_registration_is_coalesce_assigned()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            IServiceCollection? maybe = null;
            maybe ??= services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_when_options_validator_registration_uses_conditional_access()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            IServiceCollection? maybe = services;
            maybe?.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_when_options_validator_registration_is_in_a_lambda()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            Action register = () => services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            register();
            """, extraUsings: "using System;\nusing Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_when_options_validator_is_registered_via_parenthesized_receiver()
    {
        var source = OptionsSource("""
            {|#0:(services).AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            (services).AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg003_reports_when_options_validator_is_registered_via_static_unreduced_add_singleton()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            ServiceCollectionServiceExtensions.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>(services);
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg003_reports_named_bindless_validate_when_options_validator_is_registered()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<StripeOptions>("tenant", configuration.GetSection("Stripe"));
            {|#0:services.AddOptions<StripeOptions>("tenant")
                .Validate(_ => true)|};
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg004_stays_quiet_for_record_options_when_options_validator_is_registered()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed record StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }

            [OptionsValidator]
            public sealed class ValidateStripeOptions : IValidateOptions<StripeOptions>
            {
                public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppsettings, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_for_unresolved_options_validator_attribute()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }

            [DoesNotExist]
            public sealed class ValidateStripeOptions : IValidateOptions<StripeOptions>
            {
                public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAllowingCompilerErrorsAsync(source, StripeAppsettings, expected);
    }

    private static readonly (string filename, string content) StripeAppsettings =
        ("appsettings.json", """
        {
          "Stripe": {
            "ApiKey": "secret"
          }
        }
        """);

    private static readonly (string filename, string content) MissingApiKeyAppsettings =
        ("appsettings.json", """
        {
          "Stripe": {
            "WebhookSecret": "secret"
          }
        }
        """);

    private static readonly (string filename, string content) ServerOutOfRangeAppsettings =
        ("appsettings.json", """
        {
          "Server": {
            "Port": 0
          }
        }
        """);

    private const string ServerValidatorTypes = """
        public sealed class ServerOptions
        {
            [Range(1, 65535)]
            public int Port { get; set; }
        }

        [OptionsValidator]
        public sealed class ValidateServerOptions : IValidateOptions<ServerOptions>
        {
            public ValidateOptionsResult Validate(string? name, ServerOptions options) => ValidateOptionsResult.Success;
        }
        """;
}
