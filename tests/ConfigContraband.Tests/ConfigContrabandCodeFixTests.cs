using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed class ConfigContrabandCodeFixTests
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

    [Fact]
    public async Task Cfg005_fix_adds_validate_object_members()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, optionsTypes: """
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

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
                }
            }

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
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg005_fix_adds_validate_object_members_for_type_level_validation_attribute()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, extraUsings: "using System;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions {|#1:Database|} { get; set; } = new();
            }

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class ValidDatabaseOptionsAttribute : ValidationAttribute
            {
                protected override ValidationResult IsValid(object value, ValidationContext validationContext)
                {
                    return ValidationResult.Success!;
                }
            }

            [ValidDatabaseOptions]
            public sealed class DatabaseOptions
            {
                public string ConnectionString { get; set; } = "";
            }
            """);

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using System;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
                }
            }

            public sealed class AppOptions
            {
                [ValidateObjectMembers]
                public DatabaseOptions Database { get; set; } = new();
            }

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class ValidDatabaseOptionsAttribute : ValidationAttribute
            {
                protected override ValidationResult IsValid(object value, ValidationContext validationContext)
                {
                    return ValidationResult.Success!;
                }
            }

            [ValidDatabaseOptions]
            public sealed class DatabaseOptions
            {
                public string ConnectionString { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg005_fix_adds_validate_object_members_to_initialized_get_only_property()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions {|#1:Database|} { get; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";
            }
            """);

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
                }
            }

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
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg005_fix_adds_validate_object_members_to_constructor_bound_record_property()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, optionsTypes: """
            public sealed record AppOptions(DatabaseOptions {|#1:Database|});

            public sealed record DatabaseOptions([property: Required] string ConnectionString);
            """);

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
                }
            }

            public sealed record AppOptions([property: ValidateObjectMembers] DatabaseOptions Database);

            public sealed record DatabaseOptions([property: Required] string ConnectionString);
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg005_fix_adds_validate_object_members_to_constructor_bound_inherited_property()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, optionsTypes: """
            public abstract class BaseAppOptions
            {
                protected BaseAppOptions(DatabaseOptions database)
                {
                    Database = database;
                }

                public DatabaseOptions {|#1:Database|} { get; }
            }

            public sealed class AppOptions : BaseAppOptions
            {
                public AppOptions(DatabaseOptions database)
                    : base(database)
                {
                }
            }

            public sealed record DatabaseOptions([property: Required] string ConnectionString);
            """);

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
                }
            }

            public abstract class BaseAppOptions
            {
                protected BaseAppOptions(DatabaseOptions database)
                {
                    Database = database;
                }

                [ValidateObjectMembers]
                public DatabaseOptions Database { get; }
            }

            public sealed class AppOptions : BaseAppOptions
            {
                public AppOptions(DatabaseOptions database)
                    : base(database)
                {
                }
            }

            public sealed record DatabaseOptions([property: Required] string ConnectionString);
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("BaseAppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg005_fix_adds_validate_enumerated_items_to_constructor_bound_record_collection()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed record AppOptions(List<ServerOptions> {|#1:Servers|});

            public sealed record ServerOptions([property: Required] string Host);
            """);

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using System.Collections.Generic;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
                }
            }

            public sealed record AppOptions([property: ValidateEnumeratedItems] List<ServerOptions> Servers);

            public sealed record ServerOptions([property: Required] string Host);
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Servers");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg005_fix_adds_validate_object_members_to_private_set_property_when_bind_non_public_properties_enabled()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.BindNonPublicProperties = true)
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions {|#1:Database|} { get; private set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; private set; } = "";
            }
            """);

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.BindNonPublicProperties = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
                }
            }

            public sealed class AppOptions
            {
                [ValidateObjectMembers]
                public DatabaseOptions Database { get; private set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; private set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg005_fix_reuses_existing_options_using()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, extraUsings: "using Microsoft.Extensions.Options;", optionsTypes: """
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

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
                }
            }

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
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg005_fix_adds_validate_enumerated_items()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<ServerOptions> {|#1:Servers|} { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                [Required]
                public string Host { get; set; } = "";
            }
            """);

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using System.Collections.Generic;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
                }
            }

            public sealed class AppOptions
            {
                [ValidateEnumeratedItems]
                public List<ServerOptions> Servers { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                [Required]
                public string Host { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Servers");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg005_fix_updates_nested_object_property_in_target_document()
    {
        var startupSource = """
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart()|};
                }
            }
            """;

        var optionsSource = """
            using System.ComponentModel.DataAnnotations;

            public sealed class AppOptions
            {
                // Primary database settings.
                public DatabaseOptions {|#1:Database|} { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";
            }
            """;

        var fixedStartupSource = """
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
                }
            }
            """;

        var fixedOptionsSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.Options;

            public sealed class AppOptions
            {
                // Primary database settings.
                [ValidateObjectMembers]
                public DatabaseOptions Database { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(
            [
                ("Startup.cs", startupSource),
                ("Options.cs", optionsSource)
            ],
            [
                ("Startup.cs", fixedStartupSource),
                ("Options.cs", fixedOptionsSource)
            ],
            expected);
    }

    [Fact]
    public async Task Cfg005_fix_updates_collection_property_in_target_document_without_duplicate_using()
    {
        var startupSource = """
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart()|};
                }
            }
            """;

        var optionsSource = """
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.Options;

            public sealed class AppOptions
            {
                public List<ServerOptions> {|#1:Servers|} { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                [Required]
                public string Host { get; set; } = "";
            }
            """;

        var fixedStartupSource = """
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
                }
            }
            """;

        var fixedOptionsSource = """
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.Options;

            public sealed class AppOptions
            {
                [ValidateEnumeratedItems]
                public List<ServerOptions> Servers { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                [Required]
                public string Host { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Servers");

        await Verifier.VerifyCodeFixAsync(
            [
                ("Startup.cs", startupSource),
                ("Options.cs", optionsSource)
            ],
            [
                ("Startup.cs", fixedStartupSource),
                ("Options.cs", fixedOptionsSource)
            ],
            expected);
    }

    [Fact]
    public async Task Cfg005_fix_reuses_namespace_local_options_using()
    {
        var startupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart()|};
                }
            }
            """;

        var optionsSource = """
            namespace MyApp
            {
                using System.ComponentModel.DataAnnotations;
                using Microsoft.Extensions.Options;

                public sealed class AppOptions
                {
                    public DatabaseOptions {|#1:Database|} { get; set; } = new();
                }

                public sealed class DatabaseOptions
                {
                    [Required]
                    public string ConnectionString { get; set; } = "";
                }
            }
            """;

        var fixedStartupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
                }
            }
            """;

        var fixedOptionsSource = """
            namespace MyApp
            {
                using System.ComponentModel.DataAnnotations;
                using Microsoft.Extensions.Options;

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
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(
            [
                ("Startup.cs", startupSource),
                ("Options.cs", optionsSource)
            ],
            [
                ("Startup.cs", fixedStartupSource),
                ("Options.cs", fixedOptionsSource)
            ],
            expected);
    }

    [Fact]
    public async Task Cfg005_fix_adds_options_using_to_namespace_local_usings()
    {
        var startupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart()|};
                }
            }
            """;

        var optionsSource = """
            namespace MyApp
            {
                using System.ComponentModel.DataAnnotations;

                public sealed class AppOptions
                {
                    public DatabaseOptions {|#1:Database|} { get; set; } = new();
                }

                public sealed class DatabaseOptions
                {
                    [Required]
                    public string ConnectionString { get; set; } = "";
                }
            }
            """;

        var fixedStartupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
                }
            }
            """;

        var fixedOptionsSource = """
            namespace MyApp
            {
                using System.ComponentModel.DataAnnotations;
                using Microsoft.Extensions.Options;

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
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(
            [
                ("Startup.cs", startupSource),
                ("Options.cs", optionsSource)
            ],
            [
                ("Startup.cs", fixedStartupSource),
                ("Options.cs", fixedOptionsSource)
            ],
            expected);
    }

    [Fact]
    public async Task Cfg005_fix_qualifies_attribute_when_local_attribute_name_conflicts()
    {
        var startupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart()|};
                }
            }
            """;

        var optionsSource = """
            namespace MyApp
            {
                using System;
                using System.ComponentModel.DataAnnotations;

                public sealed class ValidateObjectMembersAttribute : Attribute
                {
                }

                public sealed class AppOptions
                {
                    public DatabaseOptions {|#1:Database|} { get; set; } = new();
                }

                public sealed class DatabaseOptions
                {
                    [Required]
                    public string ConnectionString { get; set; } = "";
                }
            }
            """;

        var fixedStartupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
                }
            }
            """;

        var fixedOptionsSource = """
            namespace MyApp
            {
                using System;
                using System.ComponentModel.DataAnnotations;

                public sealed class ValidateObjectMembersAttribute : Attribute
                {
                }

                public sealed class AppOptions
                {
                    [global::Microsoft.Extensions.Options.ValidateObjectMembersAttribute]
                    public DatabaseOptions Database { get; set; } = new();
                }

                public sealed class DatabaseOptions
                {
                    [Required]
                    public string ConnectionString { get; set; } = "";
                }
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(
            [
                ("Startup.cs", startupSource),
                ("Options.cs", optionsSource)
            ],
            [
                ("Startup.cs", fixedStartupSource),
                ("Options.cs", fixedOptionsSource)
            ],
            expected);
    }

    [Fact]
    public async Task Cfg005_fix_qualifies_constructor_bound_attribute_when_local_attribute_name_conflicts()
    {
        var startupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart()|};
                }
            }
            """;

        var optionsSource = """
            namespace MyApp
            {
                using System;
                using System.ComponentModel.DataAnnotations;

                public sealed class ValidateObjectMembersAttribute : Attribute
                {
                }

                public sealed record AppOptions(DatabaseOptions {|#1:Database|});

                public sealed record DatabaseOptions([property: Required] string ConnectionString);
            }
            """;

        var fixedStartupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
                }
            }
            """;

        var fixedOptionsSource = """
            namespace MyApp
            {
                using System;
                using System.ComponentModel.DataAnnotations;

                public sealed class ValidateObjectMembersAttribute : Attribute
                {
                }

                public sealed record AppOptions([property: global::Microsoft.Extensions.Options.ValidateObjectMembersAttribute] DatabaseOptions Database);

                public sealed record DatabaseOptions([property: Required] string ConnectionString);
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(
            [
                ("Startup.cs", startupSource),
                ("Options.cs", optionsSource)
            ],
            [
                ("Startup.cs", fixedStartupSource),
                ("Options.cs", fixedOptionsSource)
            ],
            expected);
    }

    [Fact]
    public async Task Cfg005_fix_qualifies_constructor_bound_collection_attribute_when_local_attribute_name_conflicts()
    {
        var startupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart()|};
                }
            }
            """;

        var optionsSource = """
            namespace MyApp
            {
                using System;
                using System.Collections.Generic;
                using System.ComponentModel.DataAnnotations;

                public sealed class ValidateEnumeratedItemsAttribute : Attribute
                {
                }

                public sealed record AppOptions(List<ServerOptions> {|#1:Servers|});

                public sealed record ServerOptions([property: Required] string Host);
            }
            """;

        var fixedStartupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
                }
            }
            """;

        var fixedOptionsSource = """
            namespace MyApp
            {
                using System;
                using System.Collections.Generic;
                using System.ComponentModel.DataAnnotations;

                public sealed class ValidateEnumeratedItemsAttribute : Attribute
                {
                }

                public sealed record AppOptions([property: global::Microsoft.Extensions.Options.ValidateEnumeratedItemsAttribute] List<ServerOptions> Servers);

                public sealed record ServerOptions([property: Required] string Host);
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Servers");

        await Verifier.VerifyCodeFixAsync(
            [
                ("Startup.cs", startupSource),
                ("Options.cs", optionsSource)
            ],
            [
                ("Startup.cs", fixedStartupSource),
                ("Options.cs", fixedOptionsSource)
            ],
            expected);
    }

    [Fact]
    public async Task Cfg005_fix_qualifies_collection_attribute_when_local_attribute_name_conflicts()
    {
        var startupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart()|};
                }
            }
            """;

        var optionsSource = """
            namespace MyApp
            {
                using System;
                using System.Collections.Generic;
                using System.ComponentModel.DataAnnotations;

                public sealed class ValidateEnumeratedItemsAttribute : Attribute
                {
                }

                public sealed class AppOptions
                {
                    public List<ServerOptions> {|#1:Servers|} { get; set; } = [];
                }

                public sealed class ServerOptions
                {
                    [Required]
                    public string Host { get; set; } = "";
                }
            }
            """;

        var fixedStartupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
                }
            }
            """;

        var fixedOptionsSource = """
            namespace MyApp
            {
                using System;
                using System.Collections.Generic;
                using System.ComponentModel.DataAnnotations;

                public sealed class ValidateEnumeratedItemsAttribute : Attribute
                {
                }

                public sealed class AppOptions
                {
                    [global::Microsoft.Extensions.Options.ValidateEnumeratedItemsAttribute]
                    public List<ServerOptions> Servers { get; set; } = [];
                }

                public sealed class ServerOptions
                {
                    [Required]
                    public string Host { get; set; } = "";
                }
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Servers");

        await Verifier.VerifyCodeFixAsync(
            [
                ("Startup.cs", startupSource),
                ("Options.cs", optionsSource)
            ],
            [
                ("Startup.cs", fixedStartupSource),
                ("Options.cs", fixedOptionsSource)
            ],
            expected);
    }

    [Fact]
    public async Task Cfg009_fix_replaces_section_literal()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Strpie"|});
            """);

        var fixedSource = DirectReadSource("""
            _ = configuration.GetRequiredSection("Stripe");
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
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
    public async Task Cfg009_fix_replaces_static_call_key_argument()
    {
        var source = DirectReadSource("""
            _ = ConfigurationExtensions.GetRequiredSection(configuration, {|#0:"Strpie"|});
            """);
        var fixedSource = DirectReadSource("""
            _ = ConfigurationExtensions.GetRequiredSection(configuration, "Stripe");
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
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
    public async Task Cfg009_fix_preserves_chained_leaf_segments()
    {
        var source = DirectReadSource("""
            _ = configuration.GetSection("Features").GetRequiredSection({|#0:"Sub:Strpie"|});
            """);

        var fixedSource = DirectReadSource("""
            _ = configuration.GetSection("Features").GetRequiredSection("Sub:Stripe");
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
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
    public async Task Cfg009_fix_not_offered_without_suggestion()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Missing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyCodeFixAsync(
            source,
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
