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
}
