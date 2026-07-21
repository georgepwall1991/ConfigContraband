using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{
    [Fact]
    public async Task Cfg002_stays_quiet_for_direct_configure_section()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_direct_configure_section_when_same_block_enables_data_annotations()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection({|#0:"App"|}));
            services.AddOptions<AppOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ConnectionString", "App");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_direct_configure_default_name_when_same_block_enables_data_annotations()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(Options.DefaultName, configuration.GetSection({|#0:"App"|}));
            services.AddOptions<AppOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ConnectionString", "App");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_direct_configure_empty_string_name_as_default_validation()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(string.Empty, configuration.GetSection({|#0:"App"|}));
            services.AddOptions<AppOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ConnectionString", "App");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_direct_configure_section_when_returned_validation_enables_data_annotations()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            RegisterOptions(services, configuration);
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", extraMembers: """
            private static OptionsBuilder<AppOptions> RegisterOptions(IServiceCollection services, IConfiguration configuration)
            {
                services.Configure<AppOptions>(configuration.GetSection({|#0:"App"|}));
                return services.AddOptions<AppOptions>()
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
            }
            """, optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ConnectionString", "App");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_direct_configure_when_validation_is_nested_local_function()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));

            void RegisterValidation()
            {
                services.AddOptions<AppOptions>()
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
            }
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_direct_configure_when_validation_is_conditional()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));

            if (DateTime.UtcNow.Year > 2000)
            {
                services.AddOptions<AppOptions>()
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
            }
            """, extraUsings: "using System;\nusing Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_named_direct_configure_when_default_validation_uses_reordered_named_arguments()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(config: configuration.GetSection("App"), name: "tenant");
            services.AddOptions<AppOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_default_direct_configure_when_binder_options_use_reordered_named_arguments()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(
                configureBinder: binder => binder.BindNonPublicProperties = true,
                config: configuration.GetSection({|#0:"App"|}));
            services.AddOptions<AppOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ConnectionString", "App");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_positional_named_direct_configure_with_named_config_and_default_validation()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>("tenant", config: configuration.GetSection("App"));
            services.AddOptions<AppOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_configure_all_direct_section_when_named_validation_enables_data_annotations()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(name: null, config: configuration.GetSection({|#0:"App"|}));
            services.AddOptions<AppOptions>("tenant")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ConnectionString", "App");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_cyclic_options_builder_local_validation()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            OptionsBuilder<AppOptions> builder = builder;
            builder.ValidateDataAnnotations();
            services.Configure<AppOptions>(configuration.GetSection("App"));
            """, extraUsings: "using Microsoft.Extensions.Configuration;\nusing Microsoft.Extensions.Options;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expectedCompilerError = Microsoft.CodeAnalysis.Testing.DiagnosticResult
            .CompilerError("CS0165")
            .WithSpan(12, 38, 12, 45)
            .WithArguments("builder");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
              }
            }
            """),
            expectedCompilerError);
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_expression_bodied_direct_configure_with_unrelated_validation()
    {
        var source = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services, IConfiguration configuration) =>
                    services.Configure<AppOptions>(configuration.GetSection("App"));

                public void Other(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
                }
            }

            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """;

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_top_level_direct_configure_section_when_same_scope_enables_data_annotations()
    {
        var source = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            IServiceCollection services = new ServiceCollection();
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection({|#0:"App"|}));
            services.AddOptions<AppOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();

            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ConnectionString", "App");

        await Verifier.VerifyAnalyzerConsoleAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
              }
            }
            """),
            expected);
    }
}
