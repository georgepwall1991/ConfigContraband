using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{
    [Fact]
    public async Task Cfg004_reports_direct_configure_when_same_block_validate_lacks_data_annotations()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            {|#0:services.AddOptions<AppOptions>()
                .Validate(_ => true)
                .ValidateOnStart()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_does_not_report_direct_configure_only_with_data_annotations()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg004_does_not_report_direct_configure_when_same_block_enables_data_annotations()
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
    public async Task Cfg004_stays_quiet_for_named_direct_configure_when_default_validate_lacks_data_annotations()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>("tenant", configuration.GetSection("App"));
            services.AddOptions<AppOptions>()
                .Validate(_ => true)
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg004_does_not_duplicate_when_same_block_bind_configuration_already_reports()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .Validate(_ => true)
                .ValidateOnStart()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_reports_private_set_when_matching_configure_enables_bind_non_public_properties()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(
                configuration.GetSection("App"),
                options => options.BindNonPublicProperties = true);
            {|#0:services.AddOptions<AppOptions>()
                .Validate(_ => true)
                .ValidateOnStart()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; private set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg004_ignores_private_set_when_matching_configure_does_not_enable_bind_non_public_properties()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<AppOptions>(configuration.GetSection("App"));
            services.AddOptions<AppOptions>()
                .Validate(_ => true)
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class AppOptions { [Required] public string ConnectionString { get; private set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }
}
