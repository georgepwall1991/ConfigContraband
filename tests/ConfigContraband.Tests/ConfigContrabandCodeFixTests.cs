using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandCodeFixTests
{
    [Fact]
    public async Task Cfg001_fix_replaces_section_literal()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration({|#0:"Strpie"|})
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var fixedSource = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyCodeFixAsync(
            source,
            fixedSource,
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
    public async Task Section_fix_all_applies_each_cfg001_and_cfg009_replacement()
    {
        var primarySource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration({|#0:"Strpie"|})
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
                }
            }

            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }
            """;

        var fixedPrimarySource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe")
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
                }
            }

            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }
            """;

        var secondarySource = """
            using Microsoft.Extensions.Configuration;

            public sealed class Reader
            {
                public void Read(IConfiguration configuration)
                {
                    _ = configuration.GetRequiredSection({|#1:"Databsae"|});
                }
            }
            """;

        var fixedSecondarySource = """
            using Microsoft.Extensions.Configuration;

            public sealed class Reader
            {
                public void Read(IConfiguration configuration)
                {
                    _ = configuration.GetRequiredSection("Database");
                }
            }
            """;

        var cfg001Expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");
        var cfg009Expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(1)
            .WithArguments("Databsae", ". Did you mean \"Database\"?");

        await Verifier.VerifyFixAllAcrossProjectsAsync(
            ("Primary.cs", primarySource),
            ("Primary.cs", fixedPrimarySource),
            ("Secondary.cs", secondarySource),
            ("Secondary.cs", fixedSecondarySource),
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "configured"
              },
              "Database": {
                "Host": "localhost"
              }
            }
            """),
            "UseSuggestedSection",
            cfg001Expected,
            cfg009Expected);
    }

    [Fact]
    public async Task Cfg001_fix_preserves_verbatim_section_literal()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration({|#0:@"Strpie"|})
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var fixedSource = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration(@"Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyCodeFixAsync(
            source,
            fixedSource,
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
    public async Task Cfg001_fix_escapes_quotes_in_verbatim_section_literal()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration({|#0:@"Strpie""Quoted"|})
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var fixedSource = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration(@"Stripe""Quoted")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Strpie\"Quoted", ". Did you mean \"Stripe\"Quoted\"?");

        await Verifier.VerifyCodeFixAsync(
            source,
            fixedSource,
            ("appsettings.json", """
            {
              "Stripe\"Quoted": {
                "ApiKey": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg001_fix_preserves_raw_section_literal()
    {
        var source = OptionsSource(""""
            services.AddOptions<StripeOptions>()
                .BindConfiguration({|#0:"""Strpie"""|})
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """");

        var fixedSource = OptionsSource(""""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("""Stripe""")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyCodeFixAsync(
            source,
            fixedSource,
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
    public async Task Cfg001_fix_preserves_raw_section_literal_with_quotes()
    {
        var source = OptionsSource(""""
            services.AddOptions<StripeOptions>()
                .BindConfiguration({|#0:"""Strpie"Quoted"""|})
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """");

        var fixedSource = OptionsSource(""""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("""Stripe"Quoted""")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Strpie\"Quoted", ". Did you mean \"Stripe\"Quoted\"?");

        await Verifier.VerifyCodeFixAsync(
            source,
            fixedSource,
            ("appsettings.json", """
            {
              "Stripe\"Quoted": {
                "ApiKey": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg001_fix_uses_escaped_literal_when_raw_section_replacement_contains_newline()
    {
        var source = OptionsSource(""""
            services.AddOptions<StripeOptions>()
                .BindConfiguration({|#0:"""Strpe"""|})
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """");

        var fixedSource = OptionsSource(""""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stri\npe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Strpe", ". Did you mean \"Stri\npe\"?");

        await Verifier.VerifyCodeFixAsync(
            source,
            fixedSource,
            ("appsettings.json", """
            {
              "Stri\npe": {
                "ApiKey": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg001_fix_replaces_constant_section_identifier()
    {
        var source = OptionsSource("""
            const string SectionName = "Strpie";
            services.AddOptions<StripeOptions>()
                .BindConfiguration({|#0:SectionName|})
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var fixedSource = OptionsSource("""
            const string SectionName = "Strpie";
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyCodeFixAsync(
            source,
            fixedSource,
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
    public async Task Cfg001_fix_replaces_nested_section_literal_with_full_path()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration({|#0:"Features:Strpie"|})
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var fixedSource = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Features:Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Features:Strpie", ". Did you mean \"Features:Stripe\"?");

        await Verifier.VerifyCodeFixAsync(
            source,
            fixedSource,
            ("appsettings.json", """
            {
              "Features": {
                "Stripe": {
                  "ApiKey": "secret"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg001_fix_replaces_get_section_literal()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection({|#0:"Strpie"|}))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var fixedSource = OptionsSource("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Stripe"))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyCodeFixAsync(
            source,
            fixedSource,
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
    public async Task Cfg001_fix_replaces_chained_get_section_leaf_literal()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Features").GetSection({|#0:"Strpie"|}))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var fixedSource = OptionsSource("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Features").GetSection("Stripe"))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Features:Strpie", ". Did you mean \"Features:Stripe\"?");

        await Verifier.VerifyCodeFixAsync(
            source,
            fixedSource,
            ("appsettings.json", """
            {
              "Features": {
                "Stripe": {
                  "ApiKey": "secret"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg001_fix_preserves_colon_segments_in_chained_get_section_leaf_literal()
    {
        // The chained literal itself spans multiple segments ("Sub:Strpie"). The fix must
        // rewrite only the typo'd leaf and preserve the "Sub:" segment, not overwrite the
        // whole literal with the corrected leaf (which would silently drop "Sub:").
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Features").GetSection({|#0:"Sub:Strpie"|}))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var fixedSource = OptionsSource("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Features").GetSection("Sub:Stripe"))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Features:Sub:Strpie", ". Did you mean \"Features:Sub:Stripe\"?");

        await Verifier.VerifyCodeFixAsync(
            source,
            fixedSource,
            ("appsettings.json", """
            {
              "Features": {
                "Sub": {
                  "Stripe": {
                    "ApiKey": "secret"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg001_suppresses_fix_for_non_literal_chained_colon_section()
    {
        // The chained section literal is supplied through a const whose value spans
        // multiple segments, so the leading "Sub:" segment cannot be reproduced safely
        // from the anchored expression. The fix is suppressed — the diagnostic and the
        // "Did you mean" message still appear, but the source is left unchanged rather
        // than risk a segment-dropping edit.
        var source = OptionsSource("""
            const string sub = "Sub:Strpie";
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Features").GetSection({|#0:sub|}))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Features:Sub:Strpie", ". Did you mean \"Features:Sub:Stripe\"?");

        await Verifier.VerifyCodeFixAsync(
            source,
            source,
            ("appsettings.json", """
            {
              "Features": {
                "Sub": {
                  "Stripe": {
                    "ApiKey": "secret"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg003_fix_appends_validate_on_start()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()|};
            """);

        var fixedSource = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg003_fix_appends_validate_on_start_when_options_validator_is_registered()
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
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: validatorTypes);

        var fixedSource = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: validatorTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg003_fix_appends_validate_on_start_to_same_block_direct_configure_validation()
    {
        var source = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    IConfiguration configuration = null!;
                    services.Configure<AppOptions>(configuration.GetSection("App"));
                    {|#0:services.AddOptions<AppOptions>()
                        .ValidateDataAnnotations()|};
                }
            }

            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """;

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    IConfiguration configuration = null!;
                    services.Configure<AppOptions>(configuration.GetSection("App"));
                    services.AddOptions<AppOptions>()
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
                }
            }

            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg003_fix_appends_validate_on_start_to_split_local_direct_configure_validation()
    {
        var source = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    IConfiguration configuration = null!;
                    services.Configure<AppOptions>(configuration.GetSection("App"));
                    var builder = services.AddOptions<AppOptions>();
                    {|#0:builder.ValidateDataAnnotations()|};
                }
            }

            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """;

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    IConfiguration configuration = null!;
                    services.Configure<AppOptions>(configuration.GetSection("App"));
                    var builder = services.AddOptions<AppOptions>();
                    builder.ValidateDataAnnotations().ValidateOnStart();
                }
            }

            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg003_fix_appends_validate_on_start_to_parameter_builder_direct_configure_validation()
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
                    Register(services, configuration, services.AddOptions<AppOptions>());
                }

                private static void Register(
                    IServiceCollection services,
                    IConfiguration configuration,
                    OptionsBuilder<AppOptions> builder)
                {
                    services.Configure<AppOptions>(configuration.GetSection("App"));
                    {|#0:builder.ValidateDataAnnotations()|};
                }
            }

            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
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
                    Register(services, configuration, services.AddOptions<AppOptions>());
                }

                private static void Register(
                    IServiceCollection services,
                    IConfiguration configuration,
                    OptionsBuilder<AppOptions> builder)
                {
                    services.Configure<AppOptions>(configuration.GetSection("App"));
                    builder.ValidateDataAnnotations().ValidateOnStart();
                }
            }

            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg003_fix_appends_validate_on_start_to_local_initializer_direct_configure_validation()
    {
        var source = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    IConfiguration configuration = null!;
                    services.Configure<AppOptions>(configuration.GetSection("App"));
                    var builder = {|#0:services.AddOptions<AppOptions>().ValidateDataAnnotations()|};
                }
            }

            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """;

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    IConfiguration configuration = null!;
                    services.Configure<AppOptions>(configuration.GetSection("App"));
                    var builder = services.AddOptions<AppOptions>().ValidateDataAnnotations().ValidateOnStart();
                }
            }

            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg004_fix_appends_data_annotations_to_same_block_direct_configure_validate()
    {
        var source = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    IConfiguration configuration = null!;
                    services.Configure<AppOptions>(configuration.GetSection("App"));
                    {|#0:services.AddOptions<AppOptions>()
                        .Validate(_ => true)
                        .ValidateOnStart()|};
                }
            }

            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """;

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    IConfiguration configuration = null!;
                    services.Configure<AppOptions>(configuration.GetSection("App"));
                    services.AddOptions<AppOptions>()
                        .Validate(_ => true)
                        .ValidateOnStart()
                        .ValidateDataAnnotations();
                }
            }

            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("AppOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg003_fix_all_appends_validate_on_start_to_each_registration()
    {
        var source = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.AddOptions<FirstOptions>()
                        .BindConfiguration("First")
                        .ValidateDataAnnotations()|};

                    {|#1:services.AddOptions<SecondOptions>()
                        .BindConfiguration("Second")
                        .Validate(_ => true)|};
                }
            }

            public sealed class FirstOptions
            {
                [Required]
                public string Value { get; set; } = "";
            }

            public sealed class SecondOptions
            {
                public string Value { get; set; } = "";
            }
            """;

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<FirstOptions>()
                        .BindConfiguration("First")
                        .ValidateDataAnnotations()
                        .ValidateOnStart();

                    services.AddOptions<SecondOptions>()
                        .BindConfiguration("Second")
                        .Validate(_ => true)
                        .ValidateOnStart();
                }
            }

            public sealed class FirstOptions
            {
                [Required]
                public string Value { get; set; } = "";
            }

            public sealed class SecondOptions
            {
                public string Value { get; set; } = "";
            }
            """;

        var firstExpected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("FirstOptions");
        var secondExpected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(1)
            .WithArguments("SecondOptions");

        await Verifier.VerifyFixAllAsync(
            source,
            fixedSource,
            "AddValidateOnStart",
            firstExpected,
            secondExpected);
    }

    [Fact]
    public async Task Cfg003_fix_appends_validate_on_start_for_named_options_builder()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>("tenant")
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()|};
            """);

        var fixedSource = OptionsSource("""
            services.AddOptions<StripeOptions>("tenant")
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg003_suppresses_fix_after_terminal_non_builder_extension()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .Finish()|};
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: TerminalExtensionOptionsTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, source, expected);
    }

    [Fact]
    public async Task Cfg003_fix_appends_validate_on_start_after_derived_builder_extension()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .AsDerived()|};
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: DerivedExtensionOptionsTypes);

        var fixedSource = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .AsDerived()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: DerivedExtensionOptionsTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg003_fix_appends_validate_on_start_after_constrained_builder_extension()
    {
        var source = ConstrainedBuilderSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .As<TBuilder>()|};
            """);

        var fixedSource = ConstrainedBuilderSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .As<TBuilder>()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg003_suppresses_fix_after_terminal_builder_for_different_options_type()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .SwitchOptions()|};
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: SwitchedBuilderOptionsTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, source, expected);
    }

    [Fact]
    public async Task Cfg003_fix_appends_validate_on_start_after_bind_get_section()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            {|#0:services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Stripe"))
                .ValidateDataAnnotations()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;


            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    IConfiguration configuration = null!;
                    services.AddOptions<StripeOptions>()
                        .Bind(configuration.GetSection("Stripe"))
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
                }
            }

            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg004_fix_appends_data_annotations_and_validate_on_start()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            """);

        var fixedSource = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg004_fix_appends_data_annotations_and_validate_on_start_for_named_options_builder()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>("tenant")
                .BindConfiguration("Stripe")|};
            """);

        var fixedSource = OptionsSource("""
            services.AddOptions<StripeOptions>("tenant")
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg004_suppresses_fix_after_terminal_non_builder_extension()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .Finish()|};
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: TerminalExtensionOptionsTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, source, expected);
    }

    [Fact]
    public async Task Cfg004_fix_appends_validation_after_derived_builder_extension()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .AsDerived()|};
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: DerivedExtensionOptionsTypes);

        var fixedSource = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .AsDerived()
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: DerivedExtensionOptionsTypes);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg004_fix_does_not_append_validate_on_start_when_registration_already_starts_validation()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptionsWithValidateOnStart<StripeOptions>()
                .BindConfiguration("Stripe")|};
            """);

        var fixedSource = OptionsSource("""
            services.AddOptionsWithValidateOnStart<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg004_fix_all_applies_each_diagnostics_validation_shape()
    {
        var source = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.AddOptionsWithValidateOnStart<FirstOptions>()
                        .BindConfiguration("First")|};

                    {|#1:services.AddOptions<SecondOptions>()
                        .BindConfiguration("Second")|};
                }
            }

            public sealed class FirstOptions
            {
                [Required]
                public string Value { get; set; } = "";
            }

            public sealed class SecondOptions
            {
                [Required]
                public string Value { get; set; } = "";
            }
            """;

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptionsWithValidateOnStart<FirstOptions>()
                        .BindConfiguration("First")
                        .ValidateDataAnnotations();

                    services.AddOptions<SecondOptions>()
                        .BindConfiguration("Second")
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
                }
            }

            public sealed class FirstOptions
            {
                [Required]
                public string Value { get; set; } = "";
            }

            public sealed class SecondOptions
            {
                [Required]
                public string Value { get; set; } = "";
            }
            """;

        var firstExpected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("FirstOptions");
        var secondExpected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(1)
            .WithArguments("SecondOptions");

        await Verifier.VerifyFixAllAsync(
            source,
            fixedSource,
            "AddValidateDataAnnotations",
            firstExpected,
            secondExpected);
    }

    [Fact]
    public async Task Cfg004_fix_appends_data_annotations_after_bind_get_section()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            {|#0:services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Stripe"))|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Configuration;


            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    IConfiguration configuration = null!;
                    services.AddOptions<StripeOptions>()
                        .Bind(configuration.GetSection("Stripe"))
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
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

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg004_fix_appends_data_annotations_for_inherited_data_annotations()
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

        var fixedSource = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateOnStart()
                .ValidateDataAnnotations();
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

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg004_fix_appends_data_annotations_for_nested_data_annotations()
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

        var fixedSource = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateOnStart()
                .ValidateDataAnnotations();
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

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg003_fix_appends_validate_on_start_for_split_validation_chain()
    {
        var source = OptionsSource("""
            var optionsBuilder = {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            optionsBuilder.ValidateDataAnnotations();
            """);

        var fixedSource = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateOnStart();
            optionsBuilder.ValidateDataAnnotations();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg004_fix_appends_data_annotations_for_split_validate_on_start_chain()
    {
        var source = OptionsSource("""
            var optionsBuilder = {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")|};
            optionsBuilder.ValidateOnStart();
            """);

        var fixedSource = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations();
            optionsBuilder.ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg003_fix_appends_validate_on_start_for_later_local_bind_statement_chain()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
                    {|#0:optionsBuilder.BindConfiguration("Stripe")|};
                    optionsBuilder.ValidateDataAnnotations();
            """);

        var fixedSource = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
                    optionsBuilder.BindConfiguration("Stripe").ValidateOnStart();
                    optionsBuilder.ValidateDataAnnotations();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg004_fix_appends_data_annotations_for_later_local_bind_statement_chain()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
                    {|#0:optionsBuilder.BindConfiguration("Stripe")|};
                    optionsBuilder.ValidateOnStart();
            """);

        var fixedSource = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
                    optionsBuilder.BindConfiguration("Stripe").ValidateDataAnnotations();
                    optionsBuilder.ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg003_fix_appends_validate_on_start_for_pre_bind_local_validation_chain()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
                    optionsBuilder.ValidateDataAnnotations();
                    {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            """);

        var fixedSource = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
                    optionsBuilder.ValidateDataAnnotations();
                    optionsBuilder.BindConfiguration("Stripe").ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg003_fix_appends_validate_on_start_for_initializer_validation_before_later_bind_statement()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>()
                .ValidateDataAnnotations();
                    {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            """);

        var fixedSource = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>()
                .ValidateDataAnnotations();
                    optionsBuilder.BindConfiguration("Stripe").ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg004_fix_appends_only_data_annotations_for_initializer_startup_validation_before_later_bind_statement()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptionsWithValidateOnStart<StripeOptions>();
                    {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            """);

        var fixedSource = OptionsSource("""
            var optionsBuilder = services.AddOptionsWithValidateOnStart<StripeOptions>();
                    optionsBuilder.BindConfiguration("Stripe").ValidateDataAnnotations();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg004_fix_preserves_multiline_custom_validation_chain()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey))
                .ValidateOnStart()|};
            """);

        var fixedSource = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey))
                .ValidateOnStart()
                .ValidateDataAnnotations();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg003_fix_preserves_chain_comments()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe") // appsettings section
                .ValidateDataAnnotations()|};
            """);

        var fixedSource = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe") // appsettings section
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg004_fix_keeps_single_line_chain_single_line()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>().BindConfiguration("Stripe")|};
            """);

        var fixedSource = OptionsSource("""
            services.AddOptions<StripeOptions>().BindConfiguration("Stripe").ValidateDataAnnotations().ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    private static string DirectReadSource(string body)
    {
        return $$"""
            using Microsoft.Extensions.Configuration;

            public sealed class Reader
            {
                public void Read(IConfiguration configuration)
                {
                    {{body}}
                }
            }
            """;
    }

    private const string TerminalExtensionOptionsTypes = """
        public sealed class StripeOptions
        {
            [Required]
            public string ApiKey { get; set; } = "";
        }

        public static class CustomOptionsBuilderExtensions
        {
            public static int Finish<TOptions>(this OptionsBuilder<TOptions> builder)
                where TOptions : class
            {
                return 0;
            }
        }
        """;

    private const string DerivedExtensionOptionsTypes = """
        public sealed class StripeOptions
        {
            [Required]
            public string ApiKey { get; set; } = "";
        }

        public sealed class DerivedOptionsBuilder<TOptions> : OptionsBuilder<TOptions>
            where TOptions : class
        {
            public DerivedOptionsBuilder(IServiceCollection services, string? name)
                : base(services, name)
            {
            }
        }

        public static class CustomOptionsBuilderExtensions
        {
            public static DerivedOptionsBuilder<TOptions> AsDerived<TOptions>(
                this OptionsBuilder<TOptions> builder)
                where TOptions : class
            {
                return new DerivedOptionsBuilder<TOptions>(builder.Services, builder.Name);
            }
        }
        """;

    private const string SwitchedBuilderOptionsTypes = """
        public sealed class StripeOptions
        {
            [Required]
            public string ApiKey { get; set; } = "";
        }

        public sealed class OtherOptions
        {
        }

        public static class CustomOptionsBuilderExtensions
        {
            public static OptionsBuilder<OtherOptions> SwitchOptions(
                this OptionsBuilder<StripeOptions> builder)
            {
                return new OptionsBuilder<OtherOptions>(builder.Services, builder.Name);
            }
        }
        """;

    private static string ConstrainedBuilderSource(string registration)
    {
        return $$"""
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public void Configure<TBuilder>(IServiceCollection services)
                    where TBuilder : OptionsBuilder<StripeOptions>
                {
                    {{registration}}
                }
            }

            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }

            public static class CustomOptionsBuilderExtensions
            {
                public static TBuilder As<TBuilder>(this OptionsBuilder<StripeOptions> builder)
                    where TBuilder : OptionsBuilder<StripeOptions>
                {
                    return null!;
                }
            }
            """;
    }

    private static string OptionsSource(string registration, string extraUsings = "", string? optionsTypes = null)
    {
        optionsTypes ??= """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }
            """;

        return $$"""
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            {{extraUsings}}

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {{registration}}
                }
            }

            {{optionsTypes}}
            """;
    }
}
