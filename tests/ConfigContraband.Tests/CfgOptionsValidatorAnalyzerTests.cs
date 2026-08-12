using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{
    [Fact]
    public async Task Cfg003_reports_when_options_validator_is_registered_without_validate_on_start()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

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
    public async Task Cfg003_reports_when_options_validator_is_registered_for_bind_get_section()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            {|#0:services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Stripe"))|};
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

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
    public async Task Cfg003_reports_named_options_when_options_validator_is_registered()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>("tenant")
                .BindConfiguration("Stripe")|};
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

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
    public async Task Cfg003_reports_when_options_validator_is_registered_via_try_add_enumerable()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>());
            """, extraUsings: "using Microsoft.Extensions.DependencyInjection.Extensions;\nusing Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

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
    public async Task Cfg003_and_cfg004_stay_quiet_when_options_validator_has_validate_on_start()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

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
    public async Task Cfg003_and_cfg004_stay_quiet_when_options_validator_uses_add_options_with_validate_on_start()
    {
        var source = OptionsSource("""
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>()
                .AddOptionsWithValidateOnStart<StripeOptions>()
                .BindConfiguration("Stripe");
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

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
    public async Task Cfg004_stays_quiet_when_options_validator_is_registered()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

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
    public async Task Cfg004_still_reports_for_handwritten_ivalidate_options_without_options_validator()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.AddSingleton<IValidateOptions<StripeOptions>, AppValidator>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }

            public sealed class AppValidator : IValidateOptions<StripeOptions>
            {
                public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_when_options_validator_class_is_not_registered()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_when_options_validator_is_for_a_different_options_type()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.AddSingleton<IValidateOptions<BillingOptions>, ValidateBillingOptions>();
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
            public sealed class ValidateBillingOptions : IValidateOptions<BillingOptions>
            {
                public ValidateOptionsResult Validate(string? name, BillingOptions options) => ValidateOptionsResult.Success;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_for_lookalike_options_validator_attribute()
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

            [Contoso.OptionsValidator]
            public sealed class ValidateStripeOptions : IValidateOptions<StripeOptions>
            {
                public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
            }
            """);

        var extra = """
            namespace Contoso
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class OptionsValidatorAttribute : System.Attribute
                {
                }
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(
            [("Startup.cs", source), ("Lookalike.cs", extra)],
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
    public async Task Cfg004_still_reports_when_options_validator_registration_is_in_a_nested_local_function()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            void Register()
            {
                services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            }
            Register();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_when_options_validator_registration_is_conditional()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            if (true)
            {
                services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            }
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_when_options_validator_is_registered_on_a_different_service_collection()
    {
        var source = OptionsSource("""
            IServiceCollection other = new ServiceCollection();
            other.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_for_options_validator_factory_registration()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.AddSingleton<IValidateOptions<StripeOptions>>(_ => new ValidateStripeOptions());
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg002_reports_missing_required_key_when_options_validator_is_registered()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration({|#0:"Stripe"|})
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Stripe");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "WebhookSecret": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_missing_required_key_for_configure_when_options_validator_is_registered()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<StripeOptions>(configuration.GetSection({|#0:"Stripe"|}));
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Stripe");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "WebhookSecret": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_when_options_validator_pairs_with_add_options_with_validate_on_start()
    {
        var source = OptionsSource("""
            services.AddOptionsWithValidateOnStart<StripeOptions>()
                .BindConfiguration({|#0:"Stripe"|});
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Stripe");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "WebhookSecret": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg010_reports_out_of_range_when_options_validator_is_registered()
    {
        var source = OptionsSource(
            """
            services.AddOptions<ServerOptions>()
                .BindConfiguration("Server")
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<ServerOptions>, ValidateServerOptions>();
            """,
            extraUsings: "using Microsoft.Extensions.Options;\n",
            optionsTypes: """
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
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueFailsValidation)
            .WithSpan("appsettings.json", 3, 13, 3, 14)
            .WithArguments("Server:Port", "Range", "ServerOptions");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": 0
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg004_still_reports_bindless_custom_validate_without_options_validator()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<StripeOptions>(configuration.GetSection("Stripe"));
            {|#0:services.AddOptions<StripeOptions>()
                .Validate(_ => true)
                .ValidateOnStart()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_stays_quiet_for_bindless_custom_validate_when_options_validator_is_registered()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<StripeOptions>(configuration.GetSection("Stripe"));
            services.AddOptions<StripeOptions>()
                .Validate(_ => true)
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", optionsTypes: OptionsValidatorTypes);

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

    private const string OptionsValidatorTypes = """
        public sealed class StripeOptions
        {
            [Required]
            public string ApiKey { get; set; } = "";

            public string WebhookSecret { get; set; } = "";
        }

        [OptionsValidator]
        public sealed class ValidateStripeOptions : IValidateOptions<StripeOptions>
        {
            public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
        }
        """;
}
