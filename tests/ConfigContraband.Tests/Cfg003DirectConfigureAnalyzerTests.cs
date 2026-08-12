using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{
    [Fact]
    public async Task Cfg003_reports_direct_configure_when_same_block_enables_validation_without_validate_on_start()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            {|#0:services.AddOptions<AppOptions>()
                .ValidateDataAnnotations()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_direct_configure_required_section_when_same_block_validation_lacks_validate_on_start()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetRequiredSection("App"));
            {|#0:services.AddOptions<AppOptions>()
                .ValidateDataAnnotations()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_direct_configure_when_same_block_validate_predicate_lacks_validate_on_start()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<PlainOptions>(configuration.GetSection("Plain"));
            {|#0:services.AddOptions<PlainOptions>()
                .Validate(_ => true)|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
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

    [Fact]
    public async Task Cfg003_reports_once_for_split_local_direct_configure_validation()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            var builder = services.AddOptions<AppOptions>();
            {|#0:builder.ValidateDataAnnotations()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_stays_quiet_for_direct_configure_when_same_block_uses_ivalidate_options()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            services.AddSingleton<IValidateOptions<AppOptions>, AppValidator>();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }

            public sealed class AppValidator : IValidateOptions<AppOptions>
            {
                public ValidateOptionsResult Validate(string? name, AppOptions options) => ValidateOptionsResult.Success;
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_does_not_duplicate_when_same_block_bind_already_reports()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            {|#0:services.AddOptions<AppOptions>()
                .Bind(configuration.GetSection("App"))
                .ValidateDataAnnotations()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_stays_quiet_when_custom_extension_retargets_options_builder_type()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            services.AddOptions<AppOptions>()
                .Retarget<OtherOptions>()
                .ValidateDataAnnotations();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }

            public sealed class OtherOptions
            {
                public string Value { get; set; } = "";
            }

            public static class CustomOptionsBuilderExtensions
            {
                public static OptionsBuilder<TOther> Retarget<TOther>(this OptionsBuilder<AppOptions> builder)
                    where TOther : class
                {
                    return null!;
                }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_stays_quiet_for_direct_configure_when_options_name_is_not_constant()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            var name = "tenant";
            services.Configure<AppOptions>(name, configuration.GetSection("App"));
            services.AddOptions<AppOptions>("tenant")
                .ValidateDataAnnotations();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_stays_quiet_when_unrelated_builder_binds_in_the_same_block()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            var other = services.AddOptions<OtherOptions>();
            other.BindConfiguration("Other");
            services.AddOptions<AppOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }

            public sealed class OtherOptions
            {
                public string Value { get; set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_does_not_report_direct_configure_when_same_block_calls_validate_on_start()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            services.AddOptions<AppOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_does_not_report_direct_configure_when_same_block_uses_add_options_with_validate_on_start()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            services.AddOptionsWithValidateOnStart<AppOptions>()
                .ValidateDataAnnotations();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_reports_named_direct_configure_when_matching_named_validation_lacks_validate_on_start()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>("tenant", configuration.GetSection("App"));
            {|#0:services.AddOptions<AppOptions>("tenant")
                .ValidateDataAnnotations()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_stays_quiet_for_named_direct_configure_when_default_validation_lacks_validate_on_start()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>("tenant", configuration.GetSection("App"));
            services.AddOptions<AppOptions>()
                .ValidateDataAnnotations();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_reports_direct_configure_empty_string_name_when_default_validation_lacks_validate_on_start()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(string.Empty, configuration.GetSection("App"));
            {|#0:services.AddOptions<AppOptions>()
                .ValidateDataAnnotations()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_configure_all_direct_section_when_named_validation_lacks_validate_on_start()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(name: null, config: configuration.GetSection("App"));
            {|#0:services.AddOptions<AppOptions>("tenant")
                .ValidateDataAnnotations()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_direct_configure_when_returned_validation_lacks_validate_on_start()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            RegisterOptions(services, configuration);
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", extraMembers: """
            private static OptionsBuilder<AppOptions> RegisterOptions(IServiceCollection services, IConfiguration configuration)
            {
                services.Configure<AppOptions>(configuration.GetSection("App"));
                return {|#0:services.AddOptions<AppOptions>()
                    .ValidateDataAnnotations()|};
            }
            """, optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_stays_quiet_for_direct_configure_when_validation_is_nested_local_function()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));

            void RegisterValidation()
            {
                services.AddOptions<AppOptions>()
                    .ValidateDataAnnotations();
            }
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_stays_quiet_for_direct_configure_when_validation_is_conditional()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));

            if (DateTime.UtcNow.Year > 2000)
            {
                services.AddOptions<AppOptions>()
                    .ValidateDataAnnotations();
            }
            """, extraUsings: "using System;\nusing Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_does_not_duplicate_when_same_block_bind_configuration_already_reports()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_once_when_two_direct_configure_calls_share_same_block_validation()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            services.Configure<AppOptions>(configuration.GetSection("Other"));
            {|#0:services.AddOptions<AppOptions>()
                .ValidateDataAnnotations()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_does_not_treat_custom_validate_extension_as_same_block_validation()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<PlainOptions>(configuration.GetSection("Plain"));
            services.AddOptions<PlainOptions>()
                .Validate("noop");
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", optionsTypes: """
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
    public async Task Cfg003_reports_top_level_direct_configure_when_same_scope_validation_lacks_validate_on_start()
    {
        var source = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            IServiceCollection services = new ServiceCollection();
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            {|#0:services.AddOptions<AppOptions>()
                .ValidateDataAnnotations()|};

            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerConsoleAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "ConnectionString": "configured"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg005_stays_quiet_for_direct_configure_only_with_nested_annotations()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions Database { get; set; } = new();
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
    public async Task Cfg005_reports_nested_when_same_block_direct_configure_enables_data_annotations()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            {|#0:services.Configure<AppOptions>(configuration.GetSection("App"))|};
            services.AddOptions<AppOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions {|#1:Database|} { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg005_stays_quiet_for_direct_configure_when_same_block_validate_lacks_data_annotations()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            {|#0:services.AddOptions<AppOptions>()
                .Validate(_ => true)
                .ValidateOnStart()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class AppOptions
            {
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
    public async Task Cfg004_reports_direct_configure_when_same_block_add_options_with_validate_on_start_lacks_data_annotations()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            {|#0:services.AddOptionsWithValidateOnStart<AppOptions>()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_does_not_duplicate_when_split_local_initializer_already_binds()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            var builder = {|#0:services.AddOptions<AppOptions>().BindConfiguration("App")|};
            builder.ValidateDataAnnotations();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_does_not_duplicate_when_split_local_later_statement_already_binds()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            var builder = services.AddOptions<AppOptions>();
            {|#0:builder.BindConfiguration("App")|};
            builder.ValidateDataAnnotations();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_stays_quiet_for_direct_configure_when_unrelated_invocation_is_not_member_access()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            Touch();
            services.Configure<AppOptions>(configuration.GetSection("App"));
            services.AddOptions<AppOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", extraMembers: """
            private static void Touch()
            {
            }
            """, optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_reports_once_when_validation_is_in_local_initializer()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            var builder = {|#0:services.AddOptions<AppOptions>().ValidateDataAnnotations()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_when_parameter_builder_validates_without_validate_on_start()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            Register(services, configuration, services.AddOptions<AppOptions>());
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", extraMembers: """
            private static void Register(
                IServiceCollection services,
                IConfiguration configuration,
                OptionsBuilder<AppOptions> builder)
            {
                services.Configure<AppOptions>(configuration.GetSection("App"));
                {|#0:builder.ValidateDataAnnotations()|};
            }
            """, optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_stays_quiet_when_configure_and_validation_use_different_service_collections()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            var first = new ServiceCollection();
            var second = new ServiceCollection();
            first.Configure<AppOptions>(configuration.GetSection("App"));
            second.AddOptions<AppOptions>()
                .ValidateDataAnnotations();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_reports_when_service_collection_receiver_is_not_a_local_or_parameter()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            CreateServices().Configure<AppOptions>(configuration.GetSection("App"));
            {|#0:CreateServices().AddOptions<AppOptions>()
                .ValidateDataAnnotations()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", extraMembers: """
            private static IServiceCollection CreateServices() => new ServiceCollection();
            """, optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_stays_quiet_when_field_builder_validates_without_validate_on_start()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            builder.ValidateDataAnnotations();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", extraMembers: """
            private readonly OptionsBuilder<AppOptions> builder = null!;
            """, optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_stays_quiet_when_builder_local_has_no_initializer()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            OptionsBuilder<AppOptions> builder;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            builder = services.AddOptions<AppOptions>();
            builder.ValidateDataAnnotations();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_stays_quiet_when_same_block_configure_targets_a_different_options_type()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<OtherOptions>(configuration.GetSection("Other"));
            services.AddOptions<AppOptions>()
                .ValidateDataAnnotations();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }

            public sealed class OtherOptions
            {
                public string Value { get; set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_stays_quiet_when_named_configure_does_not_match_parameter_builder()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            Register(services, configuration, services.AddOptions<AppOptions>());
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", extraMembers: """
            private static void Register(
                IServiceCollection services,
                IConfiguration configuration,
                OptionsBuilder<AppOptions> builder)
            {
                services.Configure<AppOptions>("tenant", configuration.GetSection("App"));
                builder.ValidateDataAnnotations();
            }
            """, optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg004_stays_quiet_for_direct_configure_when_same_block_only_calls_validate_on_start()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            services.AddOptions<AppOptions>()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg004_reports_direct_configure_when_chained_add_options_with_validate_on_start_lacks_data_annotations()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            {|#0:services.AddOptionsWithValidateOnStart<AppOptions>()
                .Validate(_ => true)|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }
}
