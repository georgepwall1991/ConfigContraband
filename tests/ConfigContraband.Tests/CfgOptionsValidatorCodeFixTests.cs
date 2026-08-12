using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandCodeFixTests
{
    [Fact]
    public async Task Cfg003_fix_appends_validate_on_start_when_options_validator_uses_try_add_enumerable()
    {
        const string validatorTypes = """
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

        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>());
            """, extraUsings: "using Microsoft.Extensions.DependencyInjection.Extensions;\nusing Microsoft.Extensions.Options;\n", optionsTypes: validatorTypes);

        var fixedSource = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>());
            """, extraUsings: "using Microsoft.Extensions.DependencyInjection.Extensions;\nusing Microsoft.Extensions.Options;\n", optionsTypes: validatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg003_fix_appends_validate_on_start_when_options_validator_is_chained_into_bind()
    {
        const string validatorTypes = """
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

        var source = OptionsSource("""
            {|#0:services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>()
                .AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: validatorTypes);

        var fixedSource = OptionsSource("""
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>()
                .AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: validatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg003_fix_appends_validate_on_start_for_bind_get_section_when_options_validator_is_registered()
    {
        var source = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    IConfiguration configuration = null!;
                    {|#0:services.AddOptions<StripeOptions>()
                        .Bind(configuration.GetSection("Stripe"))|};
                    services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
                }
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

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    IConfiguration configuration = null!;
                    services.AddOptions<StripeOptions>()
                        .Bind(configuration.GetSection("Stripe"))
                        .ValidateOnStart();
                    services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
                }
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

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }
}
