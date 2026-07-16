using ConfigContraband.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ConfigContraband.Tests;

public sealed class ConfigContrabandAnalyzerTests
{
    [Fact]
    public async Task Cfg001_reports_missing_section_with_suggestion()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration({|#0:"Strpie"|})
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

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
    public async Task Cfg001_reports_missing_section_with_reordered_named_bind_configuration_arguments()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration(
                    configureBinder: binder => binder.BindNonPublicProperties = true,
                    configSectionPath: {|#0:"Strpie"|})
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

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
    public async Task Cfg001_reports_missing_nested_section_with_full_path_suggestion()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration({|#0:"Features:Strpie"|})
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Features:Strpie", ". Did you mean \"Features:Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
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
    public async Task Cfg001_does_not_report_nested_section_from_any_appsettings_file()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Features:Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            new[]
            {
                ("appsettings.json", """
                {
                  "Logging": {
                    "LogLevel": {
                      "Default": "Information"
                    }
                  }
                }
                """),
                ("appsettings.Production.json", """
                {
                  "Features": {
                    "Stripe": {
                      "ApiKey": "secret"
                    }
                  }
                }
                """)
            });
    }

    [Fact]
    public async Task Cfg001_keeps_appsettings_dot_qualified_files_visible()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.Development.local.json", """
            {
              "Stripe": {
                "ApiKey": "secret"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg001_ignores_non_dot_qualified_appsettings_like_files()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettingsBackup.json", """
            {
              "Stripe": {
                "ApiKey": "secret"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg001_does_not_report_section_from_commented_appsettings_file()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              // local development secrets are loaded separately
              "Stripe": {
                /* production overrides can replace this value */
                "ApiKey": "secret"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg001_does_not_report_section_with_json_unicode_escape()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Str\u0069pe": {
                "ApiKey": "secret"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg001_does_not_report_colon_delimited_json_section_key()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Features:Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Features:Stripe": {
                "ApiKey": "secret"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg001_does_not_report_colon_delimited_json_leaf_key()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Features:Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Features:Stripe:ApiKey": "secret"
            }
            """));
    }

    [Fact]
    public async Task Cfg001_suggests_nested_section_from_colon_delimited_json_key()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration({|#0:"Features:Strpie"|})
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Features:Strpie", ". Did you mean \"Features:Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Features:Stripe": {
                "ApiKey": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg001_does_not_report_nested_section_from_later_duplicate_section()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Features:Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Features": {
                "Billing": {
                  "Enabled": true
                }
              },
              "Features": {
                "Stripe": {
                  "ApiKey": "secret"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg001_suggests_nested_section_from_later_duplicate_section()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration({|#0:"Features:Strpie"|})
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Features:Strpie", ". Did you mean \"Features:Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Features": {
                "Billing": {
                  "Enabled": true
                }
              },
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
    public async Task Cfg001_does_not_report_when_no_appsettings_files_are_available()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Strpie")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg001_ignores_bind_configuration_on_unrelated_type()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    new Binder().BindConfiguration("Stripe");
                }
            }

            public sealed class Binder
            {
                public void BindConfiguration(string section)
                {
                }
            }
            """;

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg001_ignores_non_constant_and_empty_section_paths()
    {
        var source = OptionsSource("""
            var sectionName = GetSectionName();
            services.AddOptions<StripeOptions>()
                .BindConfiguration(sectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<StripeOptions>()
                .BindConfiguration("  ")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraMembers: """
            private static string GetSectionName()
            {
                return "Stripe";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg001_reports_missing_section_from_bind_get_section()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection({|#0:"Strpie"|}))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

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
    public async Task Cfg001_reports_missing_section_from_bind_get_required_section()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetRequiredSection({|#0:"Strpie"|}))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

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
    public async Task Cfg001_reports_missing_section_from_chained_get_section()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Features").GetSection({|#0:"Strpie"|}))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Features:Strpie", ". Did you mean \"Features:Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
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
    public async Task Cfg001_reports_missing_section_from_parenthesized_chained_get_section()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind((configuration.GetSection("Features")).GetSection({|#0:"Strpie"|}))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Features:Strpie", ". Did you mean \"Features:Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
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
    public async Task Cfg001_reports_missing_section_from_null_forgiving_chained_get_section()
    {
        var source = OptionsSource("""
            #nullable enable
            IConfiguration? configuration = null;
            services.AddOptions<StripeOptions>()
                .Bind(configuration!.GetSection("Features")!.GetSection({|#0:"Strpie"|}))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Features:Strpie", ". Did you mean \"Features:Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
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
    public async Task Cfg001_reports_missing_section_from_conditional_access_get_section()
    {
        var source = OptionsSource("""
            #nullable enable
            IConfiguration? configuration = null;
            services.AddOptions<StripeOptions>()
                .Bind(configuration?.GetSection({|#0:"Strpie"|}))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

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
    public async Task Cfg001_reports_missing_section_from_conditional_access_chained_get_section()
    {
        var source = OptionsSource("""
            #nullable enable
            IConfiguration? configuration = null;
            services.AddOptions<StripeOptions>()
                .Bind(configuration?.GetSection("Features").GetSection({|#0:"Strpie"|}))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Features:Strpie", ". Did you mean \"Features:Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
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
    public async Task Cfg001_reports_missing_section_from_get_section_before_conditional_access_chained_get_section()
    {
        var source = OptionsSource("""
            #nullable enable
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Features")?.GetSection({|#0:"Strpie"|}))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Features:Strpie", ". Did you mean \"Features:Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
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
    public async Task Cfg001_reports_missing_section_from_null_forgiving_chained_off_conditional_access_get_section()
    {
        var source = OptionsSource("""
            #nullable enable
            IConfiguration? configuration = null;
            services.AddOptions<StripeOptions>()
                .Bind(configuration?.GetSection("Features")!.GetSection({|#0:"Strpie"|}))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Features:Strpie", ". Did you mean \"Features:Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
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
    public async Task Cfg001_ignores_bind_get_section_without_constant_section_path()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            var sectionName = GetSectionName();
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection(sectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", extraMembers: """
            private static string GetSectionName()
            {
                return "Stripe";
            }
            """);

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
    public async Task Cfg001_ignores_bind_root_configuration()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

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
    public async Task Cfg001_ignores_bind_get_section_from_non_configuration_receiver()
    {
        var source = OptionsSource("""
            SectionProvider provider = null!;
            services.AddOptions<StripeOptions>()
                .Bind(provider.GetSection("Strpie"))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", extraMembers: """
            private sealed class SectionProvider
            {
                public IConfigurationSection GetSection(string key)
                {
                    throw new System.NotImplementedException();
                }
            }
            """);

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
    public async Task Cfg001_ignores_get_section_chained_off_stored_configuration_section_variable()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            var section = configuration.GetSection("Features");
            services.AddOptions<StripeOptions>()
                .Bind(section.GetSection("Strpie"))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Features": {
                "Stripe": {
                  "ApiKey": "secret"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg001_ignores_conditional_access_get_section_chained_off_stored_configuration_section_variable()
    {
        var source = OptionsSource("""
            #nullable enable
            IConfiguration configuration = null!;
            IConfigurationSection? section = configuration.GetSection("Features");
            services.AddOptions<StripeOptions>()
                .Bind(section?.GetSection("Strpie"))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Features": {
                "Stripe": {
                  "ApiKey": "secret"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg001_ignores_get_section_chained_off_nullable_annotated_configuration_section_return_value()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .Bind(GetFeaturesSection().GetSection("Strpie"))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "#nullable enable\nusing Microsoft.Extensions.Configuration;\n", extraMembers: """
            private static IConfigurationSection? GetFeaturesSection()
            {
                IConfiguration configuration = null!;
                return configuration.GetSection("Features");
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Features": {
                "Stripe": {
                  "ApiKey": "secret"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg001_ignores_get_section_chained_off_configuration_section_parameter()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .Bind(GetFeaturesSection().GetSection("Strpie"))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", extraMembers: """
            private static IConfigurationSection GetFeaturesSection()
            {
                IConfiguration configuration = null!;
                return configuration.GetSection("Features");
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Features": {
                "Stripe": {
                  "ApiKey": "secret"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg001_ignores_unrelated_configure_method()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            new OptionsConfigurator().Configure<StripeOptions>(configuration.GetSection("Strpie"));
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", extraMembers: """
            private sealed class OptionsConfigurator
            {
                public void Configure<TOptions>(IConfigurationSection section)
                {
                }
            }
            """);

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
    public async Task Analyzer_ignores_generated_code()
    {
        var source = """
            // <auto-generated/>
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Strpie")
                        .ValidateDataAnnotations();
                }
            }

            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }
            """;

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
    public async Task Analyzer_ignores_generated_file_names()
    {
        var source = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Strpie")
                        .ValidateDataAnnotations();
                }
            }

            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }
            """;

        await Verifier.VerifyAnalyzerAsync(
            [("GeneratedOptions.g.cs", source)],
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_missing_required_property()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration({|#0:"Stripe"|})
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

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
    public async Task Cfg002_stays_quiet_for_csharp_required_member()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredMemberOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredMemberOptions
                {
                    public required string MyKey { get; set; }
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_non_nullable_value_type()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredValueOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredValueOptions
                {
                    [Required]
                    public int Port { get; set; }
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_missing_required_nullable_value_type()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredValueOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredValueOptions
                {
                    [Required]
                    public int? Port { get; set; }
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("Port", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_missing_key_for_user_defined_required_attribute_subclass()
    {
        // A user-defined RequiredAttribute subclass with no IsValid override still enforces the
        // inherited required check at runtime (Validator.TryValidateObject throws when the key is
        // absent), so CFG002 must report it — matched by inheritance, not an exact type name.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredSubclassOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class MyRequiredAttribute : System.ComponentModel.DataAnnotations.RequiredAttribute
                {
                }

                public sealed class RequiredSubclassOptions
                {
                    [MyRequired]
                    public string ApiKey { get; set; } = "";
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_attribute_subclass_allowing_empty_strings_with_empty_default()
    {
        // The subclass sets the inherited AllowEmptyStrings = true, and the property's empty-string
        // default therefore satisfies the required check at runtime. The analyzer must read the
        // inherited AllowEmptyStrings (not just the exact RequiredAttribute) and stay quiet.
        var source = OptionsSource(
            registration: """
                services.AddOptions<AllowEmptySubclassOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class MyRequiredAttribute : System.ComponentModel.DataAnnotations.RequiredAttribute
                {
                }

                public sealed class AllowEmptySubclassOptions
                {
                    [MyRequired(AllowEmptyStrings = true)]
                    public string ApiKey { get; set; } = "";
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_attribute_subclass_setting_allow_empty_strings_in_constructor()
    {
        // The subclass sets the inherited AllowEmptyStrings = true in its own constructor (not a
        // named argument), so the property's empty-string default satisfies the check at runtime.
        // The analyzer must read the constructor-set value and stay quiet.
        var source = OptionsSource(
            registration: """
                services.AddOptions<CtorAllowEmptyOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class CtorRequiredAttribute : System.ComponentModel.DataAnnotations.RequiredAttribute
                {
                    public CtorRequiredAttribute()
                    {
                        AllowEmptyStrings = true;
                    }
                }

                public sealed class CtorAllowEmptyOptions
                {
                    [CtorRequired]
                    public string ApiKey { get; set; } = "";
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_attribute_subclass_setting_this_qualified_allow_empty_strings()
    {
        // The constructor uses the qualified `this.AllowEmptyStrings = true` form; it must be
        // recognized the same as the bare assignment, so the empty-string default stays quiet.
        var source = OptionsSource(
            registration: """
                services.AddOptions<ThisQualifiedAllowEmptyOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class ThisQualifiedRequiredAttribute : System.ComponentModel.DataAnnotations.RequiredAttribute
                {
                    public ThisQualifiedRequiredAttribute()
                    {
                        this.AllowEmptyStrings = true;
                    }
                }

                public sealed class ThisQualifiedAllowEmptyOptions
                {
                    [ThisQualifiedRequired]
                    public string ApiKey { get; set; } = "";
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_when_invoked_subclass_constructor_leaves_allow_empty_strings_false()
    {
        // The [OverloadRequired] usage invokes the parameterless constructor, which does NOT set
        // AllowEmptyStrings; only a different, non-invoked overload does. The empty-string default
        // therefore fails at runtime, so CFG002 must report — the scan must inspect only the
        // actually-invoked constructor, not any overload.
        var source = OptionsSource(
            registration: """
                services.AddOptions<OverloadOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class OverloadRequiredAttribute : System.ComponentModel.DataAnnotations.RequiredAttribute
                {
                    public OverloadRequiredAttribute()
                    {
                    }

                    public OverloadRequiredAttribute(bool allow)
                    {
                        AllowEmptyStrings = true;
                    }
                }

                public sealed class OverloadOptions
                {
                    [OverloadRequired]
                    public string ApiKey { get; set; } = "";
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_attribute_subclass_setting_allow_empty_strings_from_constructor_parameter()
    {
        // The subclass sets AllowEmptyStrings from a constructor parameter, used as [ParamRequired(true)].
        // The analyzer cannot reduce the parameter to a constant, so it conservatively treats the
        // subclass as possibly allowing empty strings and stays quiet — never a false positive on a
        // runtime-valid empty-string default.
        var source = OptionsSource(
            registration: """
                services.AddOptions<ParamAllowEmptyOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class ParamRequiredAttribute : System.ComponentModel.DataAnnotations.RequiredAttribute
                {
                    public ParamRequiredAttribute(bool allow)
                    {
                        AllowEmptyStrings = allow;
                    }
                }

                public sealed class ParamAllowEmptyOptions
                {
                    [ParamRequired(true)]
                    public string ApiKey { get; set; } = "";
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_attribute_subclass_setting_allow_empty_strings_in_expression_bodied_constructor()
    {
        // The constructor is expression-bodied (`=> AllowEmptyStrings = true;`). It must be
        // recognized the same as a block-bodied assignment, so the empty-string default stays quiet.
        var source = OptionsSource(
            registration: """
                services.AddOptions<ExprBodyAllowEmptyOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class ExprBodyRequiredAttribute : System.ComponentModel.DataAnnotations.RequiredAttribute
                {
                    public ExprBodyRequiredAttribute() => AllowEmptyStrings = true;
                }

                public sealed class ExprBodyAllowEmptyOptions
                {
                    [ExprBodyRequired]
                    public string ApiKey { get; set; } = "";
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_attribute_subclass_of_intermediate_that_allows_empty_strings()
    {
        // The applied attribute derives from an intermediate custom subclass whose constructor sets
        // AllowEmptyStrings = true, reached through the implicit base() call. The scan cannot inspect
        // the intermediate base constructor, so it conservatively treats the leaf subclass as
        // possibly allowing empty strings and stays quiet — never a false positive.
        var source = OptionsSource(
            registration: """
                services.AddOptions<IntermediateAllowEmptyOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public class IntermediateRequiredAttribute : System.ComponentModel.DataAnnotations.RequiredAttribute
                {
                    public IntermediateRequiredAttribute()
                    {
                        AllowEmptyStrings = true;
                    }
                }

                public sealed class LeafRequiredAttribute : IntermediateRequiredAttribute
                {
                }

                public sealed class IntermediateAllowEmptyOptions
                {
                    [LeafRequired]
                    public string ApiKey { get; set; } = "";
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_attribute_subclass_setting_base_qualified_allow_empty_strings()
    {
        // The constructor uses the `base.AllowEmptyStrings = true` form; it definitely targets the
        // inherited property and must be recognized, so the empty-string default stays quiet.
        var source = OptionsSource(
            registration: """
                services.AddOptions<BaseQualifiedAllowEmptyOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class BaseQualifiedRequiredAttribute : System.ComponentModel.DataAnnotations.RequiredAttribute
                {
                    public BaseQualifiedRequiredAttribute()
                    {
                        base.AllowEmptyStrings = true;
                    }
                }

                public sealed class BaseQualifiedAllowEmptyOptions
                {
                    [BaseQualifiedRequired]
                    public string ApiKey { get; set; } = "";
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_attribute_subclass_setting_allow_empty_strings_via_helper_call()
    {
        // The constructor enables empty strings through a helper method call rather than a direct
        // assignment. The analyzer cannot prove what the helper does, so it conservatively treats
        // the subclass as possibly allowing empty strings and stays quiet — never a false positive.
        var source = OptionsSource(
            registration: """
                services.AddOptions<HelperAllowEmptyOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class HelperRequiredAttribute : System.ComponentModel.DataAnnotations.RequiredAttribute
                {
                    public HelperRequiredAttribute()
                    {
                        EnableEmptyStrings();
                    }

                    private void EnableEmptyStrings() => AllowEmptyStrings = true;
                }

                public sealed class HelperAllowEmptyOptions
                {
                    [HelperRequired]
                    public string ApiKey { get; set; } = "";
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_when_subclass_constructor_overwrites_allow_empty_strings_to_false()
    {
        // The constructor sets AllowEmptyStrings = true then = false; the last top-level assignment
        // wins, so the effective value is false and the empty-string default fails at runtime.
        // CFG002 must report — the scan must model last-wins, not the first assignment.
        var source = OptionsSource(
            registration: """
                services.AddOptions<OverwrittenAllowEmptyOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class OverwrittenRequiredAttribute : System.ComponentModel.DataAnnotations.RequiredAttribute
                {
                    public OverwrittenRequiredAttribute()
                    {
                        AllowEmptyStrings = true;
                        AllowEmptyStrings = false;
                    }
                }

                public sealed class OverwrittenAllowEmptyOptions
                {
                    [OverwrittenRequired]
                    public string ApiKey { get; set; } = "";
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_attribute_subclass_overriding_is_valid()
    {
        // A RequiredAttribute subclass that overrides IsValid may weaken the check (e.g. accept a
        // missing value), so the analyzer cannot prove the key is required. Stay conservative and
        // do not report — preferring a false negative over a false positive.
        var source = OptionsSource(
            registration: """
                services.AddOptions<WeakenedRequiredOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class WeakenedRequiredAttribute : System.ComponentModel.DataAnnotations.RequiredAttribute
                {
                    public override bool IsValid(object? value) => true;
                }

                public sealed class WeakenedRequiredOptions
                {
                    [WeakenedRequired]
                    public string ApiKey { get; set; } = "";
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_does_not_report_when_required_property_is_present()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

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
    public async Task Cfg002_reports_missing_required_property_in_nested_section()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<StripeOptions>()
                    .BindConfiguration({|#0:"Features:Stripe"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Features:Stripe");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Features": {
                "Stripe": {
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_does_not_report_when_section_is_missing()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration({|#0:"Strpie"|})
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        // Only CFG001 should be reported
        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

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
    public async Task Cfg002_stays_quiet_when_required_property_is_in_overriding_file()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            new[]
            {
                ("appsettings.json", """
                {
                  "Stripe": {
                  }
                }
                """),
                ("appsettings.Development.json", """
                {
                  "Stripe": {
                    "ApiKey": "secret"
                  }
                }
                """)
            });
    }

    [Fact]
    public async Task Cfg002_reports_missing_key_in_empty_nested_object()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, """
            using Microsoft.Extensions.Options;
            public class AppOptions { [ValidateObjectMembers] public DatabaseOptions Database { get; set; } = new(); }
            public class DatabaseOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithSpan(12, 24, 12, 29)
            .WithArguments("ConnectionString", "App:Database");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Database": {}
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_missing_key_in_collection_element()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, """
            using System.Collections.Generic;
            using Microsoft.Extensions.Options;
            public class AppOptions { [ValidateEnumeratedItems] public List<DatabaseOptions> Databases { get; set; } = new(); }
            public class DatabaseOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithSpan(13, 24, 13, 29)
            .WithArguments("ConnectionString", "App:Databases:0");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Databases": [
                  {}
                ]
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_dictionary_value_object()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public class AppOptions { public Dictionary<string, DatabaseOptions> Databases { get; set; } = new(); }
            public class DatabaseOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Databases": {
                  "Primary": {}
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_dictionary_value_collection()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public class AppOptions { public Dictionary<string, List<DatabaseOptions>> Databases { get; set; } = new(); }
            public class DatabaseOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Databases": {
                  "Primary": [
                    {}
                  ]
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_alias_name_when_missing()
    {
        var source = OptionsSource("""
            services.AddOptions<AliasedOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, """
            using Microsoft.Extensions.Configuration;
            public class AliasedOptions
            {
                [Required]
                [ConfigurationKeyName("api-key")]
                public string ApiKey { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithSpan(16, 24, 16, 32)
            .WithArguments("api-key", "Stripe");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_stays_quiet_if_data_annotations_not_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateOnStart();
            """, """
            public class AppOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        // Should still report CFG004
        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithSpan(9, 9, 11, 23)
            .WithArguments("AppOptions");

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

    [Fact]
    public async Task Cfg002_stays_quiet_if_recursive_validation_not_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, """
            public class AppOptions { public DatabaseOptions Database { get; set; } = new(); }
            public class DatabaseOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        // Should still report CFG005
        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithSpan(10, 9, 13, 23)
            .WithSpan(3, 50, 3, 58)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Database": {}
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_missing_key_in_initialized_nested_object_even_if_section_missing()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, """
            using Microsoft.Extensions.Options;
            public class AppOptions { [ValidateObjectMembers] public DatabaseOptions Database { get; set; } = new(); }
            public class DatabaseOptions { [Required] public string ConnectionString { get; set; } = ""; }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithSpan(12, 24, 12, 29)
            .WithArguments("ConnectionString", "App:Database");

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
    public async Task Cfg002_reports_missing_key_in_default_struct_nested_object_even_if_section_missing()
    {
        // A struct nested property has a non-null default(T) at runtime even with no
        // initializer, and [ValidateObjectMembers] recursively validates it, so a missing
        // [Required] member throws at runtime. CFG002 must report it even when the nested
        // section is absent — the missing-section recursion must treat a non-nullable struct
        // default as a provably non-null instance.
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, """
            using Microsoft.Extensions.Options;
            public class AppOptions { [ValidateObjectMembers] public DatabaseOptions Database { get; set; } }
            public struct DatabaseOptions { [Required] public string ConnectionString { get; set; } }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithSpan(12, 24, 12, 29)
            .WithArguments("ConnectionString", "App:Database");

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
    public async Task Cfg002_stays_quiet_for_default_struct_nested_object_when_initializer_satisfies_required()
    {
        // The settable struct property's initializer sets the [Required] member, and the binder
        // leaves that initializer intact when the section is absent, so runtime validation
        // passes. CFG002 must not recurse as if the value were default(T): a member-setting
        // initializer is classified unprovable, so the missing-section recursion is skipped.
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, """
            using Microsoft.Extensions.Options;
            public class AppOptions { [ValidateObjectMembers] public DatabaseOptions Database { get; set; } = new() { ConnectionString = "ok" }; }
            public struct DatabaseOptions { [Required] public string ConnectionString { get; set; } }
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
    public async Task Cfg002_uses_configuration_key_name_for_missing_struct_nested_object_path()
    {
        // The nested struct property is renamed with [ConfigurationKeyName], and its section is
        // absent. The reported missing-key path must use the configured key ("App:db"), not the
        // CLR property name ("App:Database"), since the runtime binder keys the child by its
        // configured name.
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, """
            using Microsoft.Extensions.Options;
            using Microsoft.Extensions.Configuration;
            public class AppOptions { [ConfigurationKeyName("db")] [ValidateObjectMembers] public DatabaseOptions Database { get; set; } }
            public struct DatabaseOptions { [Required] public string ConnectionString { get; set; } }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithSpan(13, 24, 13, 29)
            .WithArguments("ConnectionString", "App:db");

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
    public async Task Cfg002_stays_quiet_for_required_string_with_satisfying_initializer()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    public string ApiKey { get; set; } = "sk_default";
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_required_string_with_whitespace_initializer()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    public string ApiKey { get; set; } = "   ";
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_empty_string_initializer_when_allow_empty_strings()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required(AllowEmptyStrings = true)]
                    public string ApiKey { get; set; } = "";
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_required_allow_empty_strings_without_initializer()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required(AllowEmptyStrings = true)]
                    public string? ApiKey { get; set; }
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_required_string_with_null_forgiven_initializer()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    public string ApiKey { get; set; } = null!;
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_required_string_with_method_call_initializer()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    public string ApiKey { get; set; } = CreateDefault();

                    private static string CreateDefault() => "generated";
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_nullable_value_with_satisfying_initializer()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    public int? Port { get; set; } = 8080;
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_nullable_value_with_implicit_new_initializer()
    {
        // Target-typed new() on int? constructs the underlying int (HasValue == true), so the
        // missing key cannot fail RequiredAttribute.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    public int? Port { get; set; } = new();
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_required_nullable_value_with_explicit_nullable_creation_initializer()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    public int? Port { get; set; } = new int?();
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("Port", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_nullable_value_with_negative_literal_initializer()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    public int? Port { get; set; } = -1;
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_required_initialized_property_when_constructor_overwrites_it()
    {
        // The parameterless constructor runs after the initializer and clears the value,
        // so the satisfying initializer never survives to validation.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    public RequiredDefaultOptions()
                    {
                        ApiKey = null;
                    }

                    [Required]
                    public string? ApiKey { get; set; } = "sk_default";
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_initialized_property_with_unrelated_constructor_assignment()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    public RequiredDefaultOptions()
                    {
                        Retries = 3;
                    }

                    [Required]
                    public string ApiKey { get; set; } = "sk_default";

                    public int Retries { get; set; }
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_object_creation_initializer()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    public EndpointOptions Endpoint { get; set; } = new();
                }

                public sealed class EndpointOptions
                {
                    public string Url { get; set; } = "";
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_constructor_bound_required_with_satisfying_default()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    public RequiredDefaultOptions(string apiKey = "sk_default")
                    {
                        ApiKey = apiKey;
                    }

                    [Required]
                    public string ApiKey { get; }
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_primary_constructor_bound_required_with_satisfying_default()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions(string apiKey = "sk_default")
                {
                    [Required]
                    public string ApiKey { get; set; } = apiKey;
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_primary_constructor_bound_required_with_non_satisfying_default()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions(string? apiKey = null)
                {
                    [Required]
                    public string? ApiKey { get; set; } = apiKey;
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_primary_constructor_bound_required_with_user_converted_default()
    {
        // The user-defined conversion decides the stored value, not the parameter's own default.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions(string apiKey = "sk_default")
                {
                    [Required]
                    public ConvertedValue Endpoint { get; set; } = apiKey;
                }

                public sealed class ConvertedValue
                {
                    public static implicit operator ConvertedValue(string value) => null!;
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("Endpoint", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_nullable_value_with_populated_new_initializer()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    public int? Port { get; set; } = new int?(8080);
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_nullable_struct_with_target_typed_populated_new_initializer()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    public System.TimeSpan? Delay { get; set; } = new(0, 5, 0);
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_nullable_struct_with_underlying_new_initializer()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    public System.TimeSpan? Delay { get; set; } = new System.TimeSpan(0, 5, 0);
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_constructor_bound_required_when_default_does_not_reach_property()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    public RequiredDefaultOptions(string apiKey = "sk_default")
                    {
                        _ = apiKey;
                    }

                    [Required]
                    public string? ApiKey { get; }
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_positional_record_required_with_satisfying_default()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed record RequiredDefaultOptions([property: Required] string? ApiKey = "sk_default");
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_required_object_property_with_empty_nullable_creation_initializer()
    {
        // new int?() boxes to null no matter what the declared property type is.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    public object? Port { get; set; } = new int?();
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("Port", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_required_nullable_value_with_aliased_empty_nullable_creation_initializer()
    {
        // The alias hides Nullable<int>, whose parameterless construction boxes to null.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            extraUsings: "using MaybeInt = System.Nullable<int>;",
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    public int? Port { get; set; } = new MaybeInt();
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("Port", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_required_object_property_with_constructed_string_initializer()
    {
        // A constructed string can be empty or whitespace, which RequiredAttribute rejects,
        // even when the declared property type is not string.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    public object? Value { get; set; } = new string(' ', 3);
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("Value", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_collection_property_with_explicit_object_creation_default()
    {
        // A non-array collection default-initialized via explicit object-creation syntax is a
        // non-null value RequiredAttribute accepts, same as any other object-creation default.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            extraUsings: "using System.Collections.Generic;\n",
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    public List<string> Items { get; set; } = new List<string>();
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_dictionary_property_with_target_typed_new_default()
    {
        // Target-typed new() constructs the declared dictionary type itself, which is a
        // non-null value RequiredAttribute accepts, same as any other object-creation default.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            extraUsings: "using System.Collections.Generic;\n",
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    public Dictionary<string, string> Items { get; set; } = new();
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_constructor_bound_required_when_constructor_calls_helper_after_assignment()
    {
        // The helper can mutate the property after the parameter assignment, so the
        // default is no longer provable.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    public RequiredDefaultOptions(string apiKey = "sk_default")
                    {
                        ApiKey = apiKey;
                        Reset();
                    }

                    [Required]
                    public string? ApiKey { get; set; }

                    private void Reset()
                    {
                        ApiKey = null;
                    }
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_constructor_bound_required_when_constructor_assigns_property_with_custom_setter()
    {
        // The custom setter on the other property clears the required value, so the
        // parameter default is not provable.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    private int _marker;

                    public RequiredDefaultOptions(string apiKey = "sk_default")
                    {
                        ApiKey = apiKey;
                        Marker = 1;
                    }

                    [Required]
                    public string? ApiKey { get; set; }

                    public int Marker
                    {
                        get => _marker;
                        set
                        {
                            _marker = value;
                            ApiKey = null;
                        }
                    }
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_expression_bodied_constructor_bound_required_with_custom_setter()
    {
        // The expression-bodied constructor routes the default through a custom setter that
        // discards the value, so the default never provably reaches the property.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    private string? _apiKey;

                    public RequiredDefaultOptions(string apiKey = "sk_default") => ApiKey = apiKey;

                    [Required]
                    public string? ApiKey
                    {
                        get => _apiKey;
                        set => _apiKey = null;
                    }
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_expression_bodied_constructor_bound_required_with_satisfying_default()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    public RequiredDefaultOptions(string apiKey = "sk_default") => ApiKey = apiKey;

                    [Required]
                    public string ApiKey { get; }
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_constructor_bound_required_with_satisfying_initializer_and_untouched_property()
    {
        // The constructor never writes the property, so the satisfying initializer survives
        // even though the matching parameter default is null.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    public RequiredDefaultOptions(string? apiKey = null)
                    {
                    }

                    [Required]
                    public string ApiKey { get; set; } = "sk_default";
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_constructor_bound_required_with_satisfying_initializer_when_constructor_overwrites_it()
    {
        // The constructor overwrites the satisfying initializer with the null parameter default.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    public RequiredDefaultOptions(string? apiKey = null)
                    {
                        ApiKey = apiKey;
                    }

                    [Required]
                    public string? ApiKey { get; set; } = "sk_default";
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_required_recursive_object_with_default_when_nested_required_missing()
    {
        // The new() default satisfies the parent's RequiredAttribute, but recursive validation
        // walks the default instance and still fails on the nested required key, so the parent
        // stays required.
        var source = OptionsSource(
            registration: """
                services.AddOptions<AppOptions>()
                    .BindConfiguration({|#0:"App"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            extraUsings: "using Microsoft.Extensions.Options;",
            optionsTypes: """
                public sealed class AppOptions
                {
                    [Required]
                    [ValidateObjectMembers]
                    public DatabaseOptions Database { get; set; } = new();
                }

                public sealed class DatabaseOptions
                {
                    [Required]
                    public string ConnectionString { get; set; } = "";
                }
                """);

        var expectedParent = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("Database", "App");
        var expectedChild = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ConnectionString", "App:Database");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
              }
            }
            """),
            expectedParent,
            expectedChild);
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_recursive_object_with_default_when_nested_defaults_satisfy()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<AppOptions>()
                    .BindConfiguration("App")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            extraUsings: "using Microsoft.Extensions.Options;",
            optionsTypes: """
                public sealed class AppOptions
                {
                    [Required]
                    [ValidateObjectMembers]
                    public DatabaseOptions Database { get; set; } = new();
                }

                public sealed class DatabaseOptions
                {
                    [Required]
                    public string ConnectionString { get; set; } = "Server=localhost";
                }
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
    public async Task Cfg002_reports_constructor_bound_required_when_parameter_is_reassigned_before_property_assignment()
    {
        // The first statement writes the parameter (which shadows the same-named field), so the
        // satisfying default never reaches the property.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    private string? apiKey;

                    public RequiredDefaultOptions(string apiKey = "sk_default")
                    {
                        apiKey = null!;
                        ApiKey = apiKey;
                    }

                    [Required]
                    public string? ApiKey { get; set; }

                    public string? Backup => this.apiKey;
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_required_recursive_object_when_default_initializer_overrides_nested_member()
    {
        // The object initializer mutates the nested instance, so the declared-type walk cannot
        // prove the default instance still satisfies nested requirements.
        var source = OptionsSource(
            registration: """
                services.AddOptions<AppOptions>()
                    .BindConfiguration({|#0:"App"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            extraUsings: "using Microsoft.Extensions.Options;",
            optionsTypes: """
                public sealed class AppOptions
                {
                    [Required]
                    [ValidateObjectMembers]
                    public DatabaseOptions Database { get; set; } = new() { ConnectionString = null! };
                }

                public sealed class DatabaseOptions
                {
                    [Required]
                    public string ConnectionString { get; set; } = "Server=localhost";
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("Database", "App");

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
    public async Task Cfg002_stays_quiet_for_required_initialized_property_with_private_factory_constructor()
    {
        // The binder can only run the public parameterless constructor; the private factory
        // constructor that overwrites the property is unreachable during binding.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    public RequiredDefaultOptions()
                    {
                    }

                    private RequiredDefaultOptions(string? marker)
                    {
                        ApiKey = marker;
                    }

                    [Required]
                    public string? ApiKey { get; set; } = "sk_default";

                    public static RequiredDefaultOptions Empty => new(null);
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_string_with_constant_initializer()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    private const string DefaultKey = "sk_default";

                    [Required]
                    public string ApiKey { get; set; } = DefaultKey;
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_allow_empty_strings_with_constructed_string_initializer()
    {
        // AllowEmptyStrings accepts any non-null string, and a constructed string is non-null.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required(AllowEmptyStrings = true)]
                    public string ApiKey { get; set; } = new string(' ', 3);
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_initialized_property_with_unused_public_overload()
    {
        // The binder always selects the public parameterless constructor, so the overload that
        // writes the property can never run during binding.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    public RequiredDefaultOptions()
                    {
                    }

                    public RequiredDefaultOptions(string? apiKey)
                    {
                        ApiKey = apiKey;
                    }

                    [Required]
                    public string? ApiKey { get; set; } = "sk_default";
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_constructor_bound_required_with_base_initializer()
    {
        // The base initializer runs before the constructor body, so it cannot clear the
        // parameter value assigned to the property afterwards.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public class BaseOptions
                {
                }

                public sealed class RequiredDefaultOptions : BaseOptions
                {
                    public RequiredDefaultOptions(string apiKey = "sk_default") : base()
                    {
                        ApiKey = apiKey;
                    }

                    [Required]
                    public string ApiKey { get; }
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_required_recursive_object_when_default_uses_constructor_arguments()
    {
        // Constructor arguments produce an instance the declared-type walk cannot model, so the
        // recursive default stays unproven.
        var source = OptionsSource(
            registration: """
                services.AddOptions<AppOptions>()
                    .BindConfiguration({|#0:"App"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            extraUsings: "using Microsoft.Extensions.Options;",
            optionsTypes: """
                public sealed class AppOptions
                {
                    [Required]
                    [ValidateObjectMembers]
                    public DatabaseOptions Database { get; set; } = new(null);
                }

                public sealed class DatabaseOptions
                {
                    public DatabaseOptions(string? connectionString = "Server=localhost")
                    {
                        ConnectionString = connectionString;
                    }

                    [Required]
                    public string? ConnectionString { get; }
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("Database", "App");

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
    public async Task Cfg002_reports_constructor_bound_required_when_derived_member_hides_required_base_property()
    {
        // The constructor assignment binds to the hiding derived member, so the hidden required
        // base property stays null when the key is missing.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public class BaseOptions
                {
                    [Required]
                    public string? ApiKey { get; set; }
                }

                public sealed class RequiredDefaultOptions : BaseOptions
                {
                    public RequiredDefaultOptions(string apiKey = "sk_default")
                    {
                        ApiKey = apiKey;
                    }

                    public new string? ApiKey { get; set; }
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_required_recursive_object_when_nested_recursive_default_is_unprovable()
    {
        // The unprovable creation two levels down keeps both ancestors required.
        var source = OptionsSource(
            registration: """
                services.AddOptions<AppOptions>()
                    .BindConfiguration({|#0:"App"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            extraUsings: "using Microsoft.Extensions.Options;",
            optionsTypes: """
                public sealed class AppOptions
                {
                    [Required]
                    [ValidateObjectMembers]
                    public DatabaseOptions Database { get; set; } = new();
                }

                public sealed class DatabaseOptions
                {
                    [Required]
                    [ValidateObjectMembers]
                    public CredentialOptions Credentials { get; set; } = new() { Secret = null! };
                }

                public sealed class CredentialOptions
                {
                    [Required]
                    public string Secret { get; set; } = "s3cret";
                }
                """);

        var expectedParent = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("Database", "App");
        var expectedChild = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("Credentials", "App:Database");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
              }
            }
            """),
            expectedParent,
            expectedChild);
    }

    [Fact]
    public async Task Cfg002_reports_required_initialized_property_when_base_constructor_assigns_virtual_property()
    {
        // The base constructor's write to the virtual property dispatches to the derived
        // override, which clears the required value after the initializer ran.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public class BaseOptions
                {
                    public BaseOptions()
                    {
                        Marker = 1;
                    }

                    public virtual int Marker { get; set; }
                }

                public sealed class RequiredDefaultOptions : BaseOptions
                {
                    [Required]
                    public string ApiKey { get; set; } = "sk_default";

                    public override int Marker
                    {
                        get => 0;
                        set => ApiKey = null!;
                    }
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_required_recursive_object_when_non_required_child_default_is_unprovable()
    {
        // Credentials is not required itself, but its mutated default instance is validated
        // recursively at runtime, so the ancestor stays required.
        var source = OptionsSource(
            registration: """
                services.AddOptions<AppOptions>()
                    .BindConfiguration({|#0:"App"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            extraUsings: "using Microsoft.Extensions.Options;",
            optionsTypes: """
                public sealed class AppOptions
                {
                    [Required]
                    [ValidateObjectMembers]
                    public DatabaseOptions Database { get; set; } = new();
                }

                public sealed class DatabaseOptions
                {
                    [ValidateObjectMembers]
                    public CredentialOptions Credentials { get; set; } = new() { Secret = null! };
                }

                public sealed class CredentialOptions
                {
                    [Required]
                    public string Secret { get; set; } = "s3cret";
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("Database", "App");

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
    public async Task Cfg002_stays_conservative_for_required_initialized_property_with_metadata_base_constructor()
    {
        // A base constructor from a referenced assembly has no syntax to prove harmless, so the
        // initializer-survival proof stays conservative and the warning remains.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions : System.EventArgs
                {
                    [Required]
                    public string ApiKey { get; set; } = "sk_default";
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_required_recursive_object_when_nested_member_has_other_validation()
    {
        // Recursive validation evaluates every DataAnnotations rule on the default instance,
        // and [Range] fails on the default Port value, so the parent stays required.
        var source = OptionsSource(
            registration: """
                services.AddOptions<AppOptions>()
                    .BindConfiguration({|#0:"App"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            extraUsings: "using Microsoft.Extensions.Options;",
            optionsTypes: """
                public sealed class AppOptions
                {
                    [Required]
                    [ValidateObjectMembers]
                    public DatabaseOptions Database { get; set; } = new();
                }

                public sealed class DatabaseOptions
                {
                    [Range(1, 10)]
                    public int Port { get; set; }
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("Database", "App");

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
    public async Task Cfg002_reports_required_recursive_object_with_polymorphic_default()
    {
        // The default instance is a derived type; runtime validates that instance, not the
        // declared base type, so the walk cannot prove it satisfies validation.
        var source = OptionsSource(
            registration: """
                services.AddOptions<AppOptions>()
                    .BindConfiguration({|#0:"App"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            extraUsings: "using Microsoft.Extensions.Options;",
            optionsTypes: """
                public sealed class AppOptions
                {
                    [Required]
                    [ValidateObjectMembers]
                    public BaseDbOptions Database { get; set; } = new DerivedDbOptions();
                }

                public class BaseDbOptions
                {
                    [Required]
                    public string ConnectionString { get; set; } = "Server=localhost";
                }

                public sealed class DerivedDbOptions : BaseDbOptions
                {
                    [Required]
                    public string Secret { get; set; } = "";
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("Database", "App");

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
    public async Task Cfg002_reports_required_property_with_other_validator_and_satisfying_default()
    {
        // MinLength still validates the default value when the key is absent, so satisfying
        // RequiredAttribute alone does not make the key optional.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    [MinLength(10)]
                    public string ApiKey { get; set; } = "short";
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_required_initialized_property_when_options_type_is_validatable_object()
    {
        // IValidatableObject on the options type can inspect the defaulted property, so the
        // suppression stays conservative.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            extraUsings: "using System.Collections.Generic;",
            optionsTypes: """
                public sealed class RequiredDefaultOptions : IValidatableObject
                {
                    [Required]
                    public string ApiKey { get; set; } = "sk_default";

                    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                    {
                        yield break;
                    }
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_required_initialized_property_when_base_type_has_type_level_validation()
    {
        // The inherited type-level validator runs against the whole instance, so the suppression
        // stays conservative.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class AlwaysValidAttribute : ValidationAttribute
                {
                    public override bool IsValid(object? value) => true;
                }

                [AlwaysValid]
                public class ValidatedBaseOptions
                {
                }

                public sealed class RequiredDefaultOptions : ValidatedBaseOptions
                {
                    [Required]
                    public string ApiKey { get; set; } = "sk_default";
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_required_recursive_object_when_child_has_non_bindable_required_member()
    {
        // validateAllProperties evaluates the non-bindable get-only Secret, whose null default
        // fails Required, so the parent stays required.
        var source = OptionsSource(
            registration: """
                services.AddOptions<AppOptions>()
                    .BindConfiguration({|#0:"App"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            extraUsings: "using Microsoft.Extensions.Options;",
            optionsTypes: """
                public sealed class AppOptions
                {
                    [Required]
                    [ValidateObjectMembers]
                    public ChildOptions Child { get; set; } = new();
                }

                public sealed class ChildOptions
                {
                    [Required]
                    public string Secret { get; } = null!;
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("Child", "App");

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
    public async Task Cfg002_reports_required_recursive_object_when_walked_constructor_mutates_child()
    {
        // DatabaseOptions' own constructor replaces the provable child default with a mutated
        // instance, so the ancestor stays required.
        var source = OptionsSource(
            registration: """
                services.AddOptions<AppOptions>()
                    .BindConfiguration({|#0:"App"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            extraUsings: "using Microsoft.Extensions.Options;",
            optionsTypes: """
                public sealed class AppOptions
                {
                    [Required]
                    [ValidateObjectMembers]
                    public DatabaseOptions Database { get; set; } = new();
                }

                public sealed class DatabaseOptions
                {
                    public DatabaseOptions()
                    {
                        Credentials = new CredentialOptions { Secret = null! };
                    }

                    [ValidateObjectMembers]
                    public CredentialOptions Credentials { get; set; } = new();
                }

                public sealed class CredentialOptions
                {
                    [Required]
                    public string Secret { get; set; } = "s3cret";
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("Database", "App");

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
    public async Task Cfg002_reports_required_semi_auto_property_with_custom_getter()
    {
        // The initializer writes the synthesized backing field, but RequiredAttribute reads the
        // custom getter, which returns the unrelated null field.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    private string? _apiKey;

                    [Required]
                    public string? ApiKey { get => _apiKey; set; } = "sk_default";

                    public void Store(string? value) => _apiKey = value;
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_stays_quiet_for_required_recursive_object_when_optional_child_is_null()
    {
        // Recursive validation skips null members, so the null Credentials default cannot fail
        // and the provable parent default satisfies validation.
        var source = OptionsSource(
            registration: """
                services.AddOptions<AppOptions>()
                    .BindConfiguration("App")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            extraUsings: "using Microsoft.Extensions.Options;",
            optionsTypes: """
                public sealed class AppOptions
                {
                    [Required]
                    [ValidateObjectMembers]
                    public DatabaseOptions Database { get; set; } = new();
                }

                public sealed class DatabaseOptions
                {
                    [ValidateObjectMembers]
                    public CredentialOptions? Credentials { get; set; }
                }

                public sealed class CredentialOptions
                {
                    [Required]
                    public string Secret { get; set; } = "";
                }
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
    public async Task Cfg002_reports_required_recursive_object_when_child_collection_expression_default_has_elements()
    {
        // The collection-expression default contains a mutated element the type walk cannot
        // model, so the parent stays required.
        var source = OptionsSource(
            registration: """
                services.AddOptions<AppOptions>()
                    .BindConfiguration({|#0:"App"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            extraUsings: "using System.Collections.Generic;\nusing Microsoft.Extensions.Options;",
            optionsTypes: """
                public sealed class AppOptions
                {
                    [Required]
                    [ValidateObjectMembers]
                    public DatabaseOptions Database { get; set; } = new();
                }

                public sealed class DatabaseOptions
                {
                    [ValidateEnumeratedItems]
                    public List<CredentialOptions> Credentials { get; set; } = [new() { Secret = null! }];
                }

                public sealed class CredentialOptions
                {
                    [Required]
                    public string Secret { get; set; } = "s3cret";
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("Database", "App");

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
    public async Task Cfg002_reports_nested_required_when_constructor_creates_recursive_child()
    {
        // The constructor's clean creation is the runtime default, so recursive validation
        // walks it and fails on the nested required key.
        var source = OptionsSource(
            registration: """
                services.AddOptions<AppOptions>()
                    .BindConfiguration({|#0:"App"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            extraUsings: "using Microsoft.Extensions.Options;",
            optionsTypes: """
                public sealed class AppOptions
                {
                    public AppOptions()
                    {
                        Database = new();
                    }

                    [ValidateObjectMembers]
                    public DatabaseOptions Database { get; set; }
                }

                public sealed class DatabaseOptions
                {
                    [Required]
                    public string ConnectionString { get; set; } = "";
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ConnectionString", "App:Database");

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
    public async Task Cfg002_stays_quiet_for_required_initialized_property_with_empty_base_initializer()
    {
        // A zero-argument base initializer resolves to the same parameterless base constructor
        // the implicit chain selects, so it cannot invalidate the satisfying initializer.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration("Required")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public class BaseOptions
                {
                }

                public sealed class RequiredDefaultOptions : BaseOptions
                {
                    public RequiredDefaultOptions() : base()
                    {
                    }

                    [Required]
                    public string ApiKey { get; set; } = "sk_default";
                }
                """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg002_reports_required_property_with_user_converted_constant_initializer()
    {
        // The user-defined conversion decides the stored value, not the source constant.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    public ConvertedValue Endpoint { get; set; } = "x";
                }

                public sealed class ConvertedValue
                {
                    public static implicit operator ConvertedValue(string value) => null!;
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("Endpoint", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_required_property_with_user_converted_object_creation_initializer()
    {
        // The user-defined conversion decides the stored value, not the constructed source object.
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    [Required]
                    public ConvertedValue Endpoint { get; set; } = new SourceValue();
                }

                public sealed class SourceValue
                {
                }

                public sealed class ConvertedValue
                {
                    public static implicit operator ConvertedValue(SourceValue value) => null!;
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("Endpoint", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_reports_constructor_bound_required_with_null_default()
    {
        var source = OptionsSource(
            registration: """
                services.AddOptions<RequiredDefaultOptions>()
                    .BindConfiguration({|#0:"Required"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """,
            optionsTypes: """
                public sealed class RequiredDefaultOptions
                {
                    public RequiredDefaultOptions(string? apiKey = null)
                    {
                        ApiKey = apiKey;
                    }

                    [Required]
                    public string? ApiKey { get; }
                }
                """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Required");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Required": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg003_reports_validation_without_validate_on_start()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()|};
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_named_options_builder_validation_without_validate_on_start()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>("tenant")
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()|};
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_honors_add_options_with_validate_on_start()
    {
        var source = OptionsSource("""
            services.AddOptionsWithValidateOnStart<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_honors_validate_on_start_before_bind_configuration()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .ValidateOnStart()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_does_not_treat_custom_validate_on_start_extension_as_startup_validation()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart("noop")|};
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }

            public static class CustomOptionsBuilderExtensions
            {
                public static OptionsBuilder<TOptions> ValidateOnStart<TOptions>(
                    this OptionsBuilder<TOptions> builder,
                    string marker)
                    where TOptions : class
                {
                    return builder;
                }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_validation_before_bind_configuration_without_validate_on_start()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<StripeOptions>()
                .ValidateDataAnnotations()
                .BindConfiguration("Stripe")|};
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_factory_created_builder_without_validate_on_start()
    {
        var source = OptionsSource("""
            {|#0:new BuilderFactory()
                .Create<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()|};
            """, extraUsings: "using Microsoft.Extensions.Options;\n", extraMembers: """
            private sealed class BuilderFactory
            {
                public OptionsBuilder<TOptions> Create<TOptions>()
                    where TOptions : class
                {
                    throw new System.NotImplementedException();
                }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_bind_get_section_validation_without_validate_on_start()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            {|#0:services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Stripe"))
                .ValidateDataAnnotations()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_does_not_treat_custom_validate_extension_as_options_validation()
    {
        var source = OptionsSource("""
            services.AddOptions<PlainOptions>()
                .BindConfiguration("Plain")
                .Validate("noop");
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
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
    public async Task Cfg003_honors_chained_split_local_registration_chain()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<PlainOptions>()
                .BindConfiguration("Plain");
            optionsBuilder.Validate(options => true).ValidateOnStart();
            """, optionsTypes: """
            public sealed class PlainOptions
            {
                public string Value { get; set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_stops_split_local_scan_at_unrelated_invocation()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<PlainOptions>()
                .BindConfiguration("Plain");
            Validate(optionsBuilder);
            optionsBuilder.ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", extraMembers: """
            private static void Validate(OptionsBuilder<PlainOptions> optionsBuilder)
            {
            }
            """, optionsTypes: """
            public sealed class PlainOptions
            {
                public string Value { get; set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_does_not_report_when_validate_on_start_follows_unrelated_statement()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            optionsBuilder.ValidateDataAnnotations();
            services.AddSingleton<Startup>();
            optionsBuilder.ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_reports_when_builder_local_reassigned_before_validate_on_start()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            optionsBuilder.ValidateDataAnnotations();
            optionsBuilder = services.AddOptions<StripeOptions>();
            optionsBuilder.ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_when_builder_local_retargeted_by_deconstruction_before_validate_on_start()
    {
        // The builder is retargeted through a tuple-deconstruction assignment before
        // ValidateOnStart(), so the later call applies to a different builder. The scan
        // must recognize the local as a deconstruction target and stop, not treat the
        // statement as inert.
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            optionsBuilder.ValidateDataAnnotations();
            (optionsBuilder, _) = (services.AddOptions<StripeOptions>(), 0);
            optionsBuilder.ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_does_not_report_when_deferred_lambda_reassigns_builder_after_validate_on_start()
    {
        // The only reassignment of the builder lives inside a deferred lambda that is
        // not invoked until after ValidateOnStart(), so the builder is not retargeted at
        // the validation point. The retarget scan must not descend into the lambda body,
        // otherwise it would treat the deferred assignment as immediate and false-fire.
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            optionsBuilder.ValidateDataAnnotations();
            System.Action reset = () => optionsBuilder = services.AddOptions<StripeOptions>();
            optionsBuilder.ValidateOnStart();
            reset();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_reports_when_builder_local_passed_by_ref_before_validate_on_start()
    {
        // Passing the builder local by ref lets the callee repoint it, so a later
        // ValidateOnStart() may apply to a different builder. The forward scan must stop
        // at the ref call rather than treat it as an inert intervening statement.
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            optionsBuilder.ValidateDataAnnotations();
            Reset(ref optionsBuilder);
            optionsBuilder.ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Options;\n", extraMembers: """
            private static void Reset(ref OptionsBuilder<StripeOptions> optionsBuilder)
            {
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_reports_when_validate_on_start_is_behind_conditional_return()
    {
        // ValidateOnStart() sits behind a conditional early return, so it does not run
        // on every path. The forward split-local scan must stop at control flow rather
        // than skip it, otherwise a genuine missing-startup-validation case is hidden.
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            optionsBuilder.ValidateDataAnnotations();
            if (services is null) return;
            optionsBuilder.ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_and_cfg004_honor_split_local_registration_chain()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            optionsBuilder.ValidateDataAnnotations();
            optionsBuilder.ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_and_cfg004_honor_later_local_bind_statement_chain()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            optionsBuilder.BindConfiguration("Stripe");
            optionsBuilder.ValidateDataAnnotations();
            optionsBuilder.ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_reports_later_local_bind_statement_validation_without_validate_on_start()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            optionsBuilder.ValidateDataAnnotations();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
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
    public async Task Cfg003_and_cfg004_honor_validation_before_bind_across_unrelated_statement()
    {
        // The backward mirror of the forward split-local fix: a validation call placed *before* the
        // bind, separated from it by an unrelated statement. The prior-scan must skip the inert
        // statement and still collect the earlier ValidateDataAnnotations(), so neither CFG003 nor
        // CFG004 fires (all validation and startup registration are present).
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            optionsBuilder.ValidateDataAnnotations();
            services.AddSingleton<Startup>();
            optionsBuilder.BindConfiguration("Stripe");
            optionsBuilder.ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_and_cfg004_honor_validation_before_bind_across_control_flow()
    {
        // The prior validation is a top-level unconditional statement, then a control-flow statement,
        // then the bind. The earlier validation always runs before the bind is reached, so the
        // backward scan must continue past the control-flow statement and collect it — control flow
        // does not stop the backward scan (only a retarget or the builder's declaration does).
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            optionsBuilder.ValidateDataAnnotations();
            if (services.Count > 0)
            {
                services.AddSingleton<Startup>();
            }
            optionsBuilder.BindConfiguration("Stripe");
            optionsBuilder.ValidateOnStart();
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
    public async Task Cfg003_reports_parameter_typed_builder_split_validation_without_validate_on_start()
    {
        // The builder is a method parameter and its bind/validation calls are split across separate
        // statements (not a single fluent chain). Validation is present without ValidateOnStart, so
        // CFG003 must fire — the split-statement scan must track a parameter receiver, not only a
        // local variable.
        var source = OptionsSource("", extraUsings: "using Microsoft.Extensions.Options;\n", extraMembers: """
            private static void ConfigureBuilder(OptionsBuilder<StripeOptions> builder)
            {
                {|#0:builder.BindConfiguration("Stripe")|};
                builder.ValidateDataAnnotations();
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg003_does_not_report_parameter_typed_builder_split_validation_with_validate_on_start()
    {
        // Same parameter-typed split shape but with ValidateOnStart present — the scan must collect
        // the later ValidateOnStart() call on the parameter receiver and stay quiet.
        var source = OptionsSource("", extraUsings: "using Microsoft.Extensions.Options;\n", extraMembers: """
            private static void ConfigureBuilder(OptionsBuilder<StripeOptions> builder)
            {
                builder.BindConfiguration("Stripe");
                builder.ValidateDataAnnotations();
                builder.ValidateOnStart();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_and_cfg004_honor_validation_before_later_local_bind_statement()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            optionsBuilder.ValidateDataAnnotations();
            optionsBuilder.BindConfiguration("Stripe");
            optionsBuilder.ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg003_reports_validation_before_later_local_bind_statement_without_validate_on_start()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>();
            optionsBuilder.ValidateDataAnnotations();
            {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
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
    public async Task Cfg003_honors_validate_on_start_from_local_builder_initializer_before_later_bind_statement()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptionsWithValidateOnStart<StripeOptions>();
            optionsBuilder.BindConfiguration("Stripe");
            optionsBuilder.ValidateDataAnnotations();
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
    public async Task Cfg003_reports_local_builder_initializer_validation_without_validate_on_start()
    {
        var source = OptionsSource("""
            var optionsBuilder = services.AddOptions<StripeOptions>()
                .ValidateDataAnnotations();
            {|#0:optionsBuilder.BindConfiguration("Stripe")|};
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ValidationNotOnStart)
            .WithLocation(0)
            .WithArguments("StripeOptions");

        await Verifier.VerifyAnalyzerAsync(source, expected);
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
    public async Task Cfg003_reports_split_custom_validation_without_validate_on_start()
    {
        var source = OptionsSource("""
            var optionsBuilder = {|#0:services.AddOptions<PlainOptions>()
                .BindConfiguration("Plain")|};
            optionsBuilder.Validate(options => true);
            """, optionsTypes: """
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

    [Fact]
    public async Task Cfg005_reports_nested_object_without_recursive_validation()
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg005_reports_nested_struct_without_recursive_validation()
    {
        // The nested options property is a struct carrying a validation attribute. The
        // runtime binder populates struct properties and the validator would validate them,
        // so a missing recursive-validation attribute must be reported just as for a class.
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions {|#1:Database|} { get; set; }
            }

            public struct DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg005_reports_initialized_get_only_nested_object_without_recursive_validation()
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg005_reports_constructor_bound_nested_object_without_recursive_validation()
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg005_does_not_report_ambiguous_constructor_bound_nested_object()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions(DatabaseOptions database)
                {
                    Database = database;
                    Name = "";
                }

                public AppOptions(DatabaseOptions database, string name)
                {
                    Database = database;
                    Name = name;
                }

                public DatabaseOptions Database { get; }

                public string Name { get; }
            }

            public sealed record DatabaseOptions([property: Required] string ConnectionString);
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg005_reports_constructor_bound_inherited_nested_object_without_recursive_validation()
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("BaseAppOptions", "Database");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg005_ignores_uninitialized_get_only_nested_object()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
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
    public async Task Cfg005_reports_private_set_nested_object_when_bind_non_public_properties_enabled()
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg005_reports_bind_get_section_private_set_nested_object_when_bind_non_public_properties_enabled()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            {|#0:services.AddOptions<AppOptions>()
                .Bind(configuration.GetSection("App"), options => options.BindNonPublicProperties = true)
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg005_reports_nested_validatable_object_without_recursive_validation()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions {|#1:Database|} { get; set; } = new();
            }

            public sealed class DatabaseOptions : IValidatableObject
            {
                public string ConnectionString { get; set; } = "";

                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                {
                    yield break;
                }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg005_reports_nested_type_level_validation_attribute_without_recursive_validation()
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg005_reports_nested_type_level_validation_attribute_declared_on_base_type_without_recursive_validation()
    {
        // Same inheritance boundary as CFG004: a type-level attribute declared only on a
        // nested object's base class is still evaluated by Validator.TryValidateObject and
        // must be reachable through recursive validation.
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
            public class DatabaseOptionsBase
            {
            }

            public sealed class DatabaseOptions : DatabaseOptionsBase
            {
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
    public async Task Cfg005_reports_bind_get_section_nested_object_without_recursive_validation()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            {|#0:services.AddOptions<AppOptions>()
                .Bind(configuration.GetSection("App"))
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
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
    public async Task Cfg005_reports_nested_collection_without_recursive_validation()
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

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Servers");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg005_reports_nested_collection_type_level_validation_attribute_without_recursive_validation()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<ServerOptions> {|#1:Servers|} { get; set; } = [];
            }

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class ValidServerOptionsAttribute : ValidationAttribute
            {
                protected override ValidationResult IsValid(object value, ValidationContext validationContext)
                {
                    return ValidationResult.Success!;
                }
            }

            [ValidServerOptions]
            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Servers");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg005_reports_nested_array_without_recursive_validation()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public ServerOptions[] {|#1:Servers|} { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                [Required]
                public string Host { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Servers");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg005_does_not_report_dictionary_value_object_without_recursive_validation()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, ServerOptions> Servers { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                [Required]
                public string Host { get; set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg005_does_not_report_dictionary_value_object_collection_without_recursive_validation()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, List<ServerOptions>> ServersByRegion { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                [Required]
                public string Host { get; set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg005_reports_nullable_nested_object_without_recursive_validation()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, optionsTypes: """
            #nullable enable
            public sealed class AppOptions
            {
                public DatabaseOptions? {|#1:Database|} { get; set; }
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
    public async Task Cfg005_reports_deep_nested_property_without_recursive_validation()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, extraUsings: "using Microsoft.Extensions.Options;\n", optionsTypes: """
            public sealed class AppOptions
            {
                [ValidateObjectMembers]
                public DatabaseOptions Database { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                public CredentialOptions {|#1:Credentials|} { get; set; } = new();
            }

            public sealed class CredentialOptions
            {
                [Required]
                public string Password { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("DatabaseOptions", "Credentials");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg005_does_not_report_when_nested_object_already_uses_recursive_validation()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
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

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg005_does_not_report_when_nested_collection_already_uses_recursive_validation()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: """
            using System.Collections.Generic;
            using Microsoft.Extensions.Options;

            """, optionsTypes: """
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
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg005_does_not_report_interface_typed_nested_property()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public IDatabaseOptions Database { get; set; } = null!;
            }

            public interface IDatabaseOptions
            {
                [Required]
                string ConnectionString { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg005_reports_nested_object_in_user_namespace_starting_with_system()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, extraUsings: "using Systematic.Options;\n", optionsTypes: """
            namespace Systematic.Options
            {
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
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_under_bound_section()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_unknown_key_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_and_cfg007_report_when_loose_and_strict_registrations_share_section()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>("loose")
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<StripeOptions>("strict")
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expectedInfo = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");
        var expectedWarning = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expectedInfo,
            expectedWarning);
    }

    [Fact]
    public async Task Cfg006_and_cfg007_report_when_strict_registration_uses_different_section_casing()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>("loose")
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<StripeOptions>("strict")
                .BindConfiguration("stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expectedInfo = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");
        var expectedWarning = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expectedInfo,
            expectedWarning);
    }

    [Fact]
    public async Task Cfg006_reports_loose_registration_when_matching_strict_cfg007_is_disabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>("loose")
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<StripeOptions>("strict")
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            DiagnosticIds.UnknownConfigurationKeyWillThrow,
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_loose_registration_when_matching_strict_cfg007_is_disabled_by_analyzer_config()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>("loose")
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<StripeOptions>("strict")
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerWithAnalyzerConfigAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            """
            is_global = true
            dotnet_diagnostic.CFG007.severity = none
            """,
            expected);
    }

    [Fact]
    public async Task Cfg007_file_scoped_suppression_does_not_downgrade_other_appsettings_files()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var enabledFileExpected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.Production.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");
        var suppressedFileExpected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerWithAnalyzerConfigAsync(
            source,
            new[]
            {
                ("appsettings.json", """
                {
                  "Stripe": {
                    "ApiKey": "value",
                    "WebookSecret": "typo"
                  }
                }
                """),
                ("appsettings.Production.json", """
                {
                  "Stripe": {
                    "ApiKey": "value",
                    "WebookSecret": "typo"
                  }
                }
                """)
            },
            ("/.editorconfig", """
            root = true

            [appsettings.json]
            dotnet_diagnostic.CFG007.severity = none
            """),
            enabledFileExpected,
            suppressedFileExpected);
    }

    [Fact]
    public async Task Cfg006_reports_strict_registration_when_cfg007_is_disabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            DiagnosticIds.UnknownConfigurationKeyWillThrow,
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_loose_registration_when_strict_twin_uses_different_binder_options()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>("loose")
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddOptions<StripeOptions>("strict")
                .BindConfiguration("Stripe", options =>
                {
                    options.BindNonPublicProperties = true;
                    options.ErrorOnUnknownConfiguration = true;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

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
    public async Task Cfg006_is_not_suppressed_by_matching_strict_registration_in_generated_code()
    {
        var looseSource = OptionsSource("""
            services.AddOptions<StripeOptions>("loose")
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);
        var generatedSource = """
            using Microsoft.Extensions.DependencyInjection;

            public sealed class GeneratedStartup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>("strict")
                        .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
                }
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            [
                ("Startup.cs", looseSource),
                ("Generated.g.cs", generatedSource)
            ],
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_unknown_key_from_bind_get_section_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Stripe"), options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_unknown_key_from_configure_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<StripeOptions>(
                configuration.GetSection("Stripe"),
                options => options.ErrorOnUnknownConfiguration = true);
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_unknown_key_under_nested_options_object_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions Database { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                public string Host { get; set; } = "";

                public int Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 7, 5, 12)
            .WithArguments("App:Database:Prt", "DatabaseOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Database": {
                  "Host": "example.test",
                  "Prt": 443
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_configuration_key_name_alias_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [ConfigurationKeyName("api_key")]
                public string ApiKey { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 3, 5, 3, 14)
            .WithArguments("Stripe:api_key", "StripeOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "api_key": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_suggest_configuration_key_name_alias_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [ConfigurationKeyName("api_key")]
                public string ApiKey { get; set; } = "";

                public string WebhookSecret { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:api_ky", "StripeOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "api_ky": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_does_not_suggest_strict_rejected_alias_for_clr_property_key_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [ConfigurationKeyName("api_key")]
                public string ApiKey { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

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
    public async Task Cfg007_reports_settable_constructor_bound_alias_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                public StripeOptions(string apiKey = "")
                {
                    ApiKey = apiKey;
                }

                [ConfigurationKeyName("api_key")]
                public string ApiKey { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 3, 5, 3, 14)
            .WithArguments("Stripe:api_key", "StripeOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "api_key": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_scalar_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 12)
            .WithArguments("Stripe:ApiKey:Foo", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": {
                  "Foo": "x"
                },
                "WebhookSecret": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_scalar_property_when_error_on_unknown_configuration_is_enabled_via_tuple_deconstruction()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    (options.ErrorOnUnknownConfiguration, options.BindNonPublicProperties) = (true, false);
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 12)
            .WithArguments("Stripe:ApiKey:Foo", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": {
                  "Foo": "x"
                },
                "WebhookSecret": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_scalar_property_when_error_on_unknown_configuration_is_enabled_via_nested_tuple_deconstruction()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    int unused;
                    ((options.ErrorOnUnknownConfiguration, options.BindNonPublicProperties), unused) = ((true, false), 0);
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 12)
            .WithArguments("Stripe:ApiKey:Foo", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": {
                  "Foo": "x"
                },
                "WebhookSecret": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_scalar_child_key_that_exists_on_string_type()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": {
                  "Length": 5
                },
                "WebhookSecret": "secret"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_known_scalar_child_key_when_string_property_has_no_live_instance()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string? ApiKey { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 15)
            .WithArguments("Stripe:ApiKey:Length", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": {
                  "Length": 5
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_known_scalar_child_key_when_uri_property_has_no_live_instance()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Uri? Endpoint { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 23)
            .WithArguments("App:Endpoint:OriginalString", "Uri", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "OriginalString": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_known_child_under_system_scalar_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Version Version { get; set; } = new(1, 0);
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Version": {
                  "Major": 1
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_does_not_report_known_child_under_creatable_system_scalar_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Version? Version { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Version": {
                  "Major": 1
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_does_not_report_initialized_interface_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public interface IEndpoint
            {
                string Url { get; set; }
            }

            public sealed class Endpoint : IEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class AppOptions
            {
                public IEndpoint Endpoint { get; set; } = new Endpoint();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Url": "https://example.test"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_stays_info_for_initialized_object_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class Endpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class AppOptions
            {
                public object Endpoint { get; } = new Endpoint();
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 15)
            .WithArguments("App:Endpoint", "AppOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Url": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_unknown_grandchild_under_value_type_clr_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public TimeSpan Duration { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 14)
            .WithArguments("App:Duration:Ticks:Foo", "Int64", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Duration": {
                  "Ticks": {
                    "Foo": "x"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_known_child_under_nullable_value_type_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public TimeSpan? Duration { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Duration": {
                  "Ticks": 123
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_nullable_wrapper_child_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public int? Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 17)
            .WithArguments("App:Port:HasValue", "Int32", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Port": {
                  "HasValue": true
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_null_get_only_nullable_clr_property_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public int? Port { get; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 11)
            .WithArguments("App:Port", "AppOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Port": {
                  "Foo": 443
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_scalar_child_key_that_exists_as_string_indexer_property()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": {
                  "Chars": {
                    "Foo": "x"
                  }
                },
                "WebhookSecret": "secret"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_scalar_collection_item_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<string> Values { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 14)
            .WithArguments("App:Values:0:Foo", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Values": [
                  {
                    "Foo": "x"
                  }
                ]
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_clr_member_shaped_child_key_under_string_collection_item_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<string> Values { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 17)
            .WithArguments("App:Values:0:Length", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Values": [
                  {
                    "Length": 5
                  }
                ]
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_known_child_under_value_type_collection_item_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<TimeSpan> Durations { get; set; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Durations": [
                  {
                    "Ticks": 123
                  }
                ]
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_scalar_reference_collection_item_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<Uri> Endpoints { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 22)
            .WithArguments("App:Endpoints:0:AbsoluteUri", "Uri", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoints": [
                  {
                    "AbsoluteUri": "https://example.test"
                  }
                ]
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_known_child_under_creatable_reference_collection_item_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<Version> Versions { get; set; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Versions": [
                  {
                    "Major": 1
                  }
                ]
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_does_not_report_dictionary_entries_inside_collection_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<Dictionary<string, string>> Values { get; set; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Values": [
                  {
                    "primary": "x"
                  }
                ]
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_does_not_report_known_child_under_value_type_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Point> Points { get; set; } = [];
            }

            public struct Point
            {
                public int X { get; set; }

                public int Y { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Points": {
                  "origin": {
                    "X": 1,
                    "Y": 2
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_does_not_report_known_child_under_creatable_reference_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Version> Versions { get; set; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Versions": {
                  "stable": {
                    "Major": 1
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_scalar_reference_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Uri> Endpoints { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 22)
            .WithArguments("App:Endpoints:primary:AbsoluteUri", "Uri", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoints": {
                  "primary": {
                    "AbsoluteUri": "https://example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_nested_dictionary_values_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Dictionary<string, FeatureOptions>> Map { get; set; } = [];
            }

            public sealed class FeatureOptions
            {
                public bool Enabled { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "Tenant": {
                    "Feature": {
                      "Enabled": true
                    }
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_nested_object_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Dictionary<string, FeatureOptions>> Map { get; set; } = [];
            }

            public sealed class FeatureOptions
            {
                public bool Enabled { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 6, 11, 6, 20)
            .WithArguments("App:Map:Tenant:Feature:Enabeld", "FeatureOptions", ". Did you mean \"Enabled\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "Tenant": {
                    "Feature": {
                      "Enabeld": true
                    }
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_nested_dictionary_object_collection_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Dictionary<string, List<FeatureOptions>>> Map { get; set; } = [];
            }

            public sealed class FeatureOptions
            {
                public bool Enabled { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 7, 13, 7, 22)
            .WithArguments("App:Map:Tenant:Features:0:Enabeld", "FeatureOptions", ". Did you mean \"Enabled\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "Tenant": {
                    "Features": [
                      {
                        "Enabeld": true
                      }
                    ]
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_nested_scalar_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Dictionary<string, int>> Ports { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 6, 11, 6, 16)
            .WithArguments("App:Ports:tenant:https:Foo", "Int32", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Ports": {
                  "tenant": {
                    "https": {
                      "Foo": 443
                    }
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_scalar_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, string> Labels { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 14)
            .WithArguments("App:Labels:primary:Foo", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Labels": {
                  "primary": {
                    "Foo": "x"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_open_dictionary_value_shape_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class Endpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class AppOptions
            {
                public Dictionary<string, object> Map { get; } = new()
                {
                    ["main"] = new Endpoint()
                };
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "main": {
                    "Url": "https://example.test"
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_stays_quiet_for_guid_keyed_dictionary_nested_object_values_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<Guid, EndpointOptions> Endpoints { get; set; } = [];
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoints": {
                  "3fa85f64-5717-4562-b3fc-2c963f66afa6": {
                    "Url": "https://example.test",
                    "Timout": 5
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_stays_quiet_for_guid_keyed_dictionary_object_shaped_scalar_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<Guid, string> Labels { get; set; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Labels": {
                  "3fa85f64-5717-4562-b3fc-2c963f66afa6": {
                    "Foo": "x"
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_stays_quiet_under_guid_keyed_dictionary_values()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<Guid, ServerOptions> Servers { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";

                public int Port { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Servers": {
                  "3fa85f64-5717-4562-b3fc-2c963f66afa6": {
                    "Host": "example.test",
                    "Prt": 443
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_unknown_key_under_enum_keyed_dictionary_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public enum Region
            {
                East,
                West
            }

            public sealed class AppOptions
            {
                public Dictionary<Region, EndpointOptions> Endpoints { get; set; } = [];
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 6, 9, 6, 17)
            .WithArguments("App:Endpoints:East:Timout", "EndpointOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoints": {
                  "East": {
                    "Url": "https://example.test",
                    "Timout": 5
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_unknown_key_under_int_keyed_dictionary_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<int, EndpointOptions> Endpoints { get; set; } = [];
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 6, 9, 6, 17)
            .WithArguments("App:Endpoints:1:Timout", "EndpointOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoints": {
                  "1": {
                    "Url": "https://example.test",
                    "Timout": 5
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_property_name_typo_but_stays_quiet_inside_guid_keyed_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<Guid, EndpointOptions> Endpoints { get; set; } = [];
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 9, 5, 9, 15)
            .WithArguments("App:Endpints", "AppOptions", ". Did you mean \"Endpoints\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoints": {
                  "3fa85f64-5717-4562-b3fc-2c963f66afa6": {
                    "Url": "https://example.test",
                    "Timout": 5
                  }
                },
                "Endpints": {
                  "Foo": "bar"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_prepopulated_polymorphic_dictionary_value_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, BaseEndpoint> Map { get; set; } = new()
                {
                    ["primary"] = new DerivedEndpoint()
                };
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "primary": {
                    "Token": "secret",
                    "Url": "https://example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_opaque_prepopulated_polymorphic_dictionary_value_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, BaseEndpoint> Map { get; } = CreateMap();

                private static Dictionary<string, BaseEndpoint> CreateMap()
                {
                    return new()
                    {
                        ["primary"] = new DerivedEndpoint()
                    };
                }
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "primary": {
                    "Token": "secret",
                    "Url": "https://example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_new_dictionary_entry_when_another_entry_is_prepopulated_polymorphic()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, BaseEndpoint> Map { get; set; } = new()
                {
                    ["primary"] = new DerivedEndpoint()
                };
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expectedPrimary = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:primary:Token", "BaseEndpoint", ".");
        var expectedSecondary = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 9, 9, 9, 16)
            .WithArguments("App:Map:secondary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "primary": {
                    "Token": "secret",
                    "Url": "https://primary.example.test"
                  },
                  "secondary": {
                    "Token": "secret",
                    "Url": "https://secondary.example.test"
                  }
                }
              }
            }
            """),
            expectedPrimary,
            expectedSecondary);
    }

    [Fact]
    public async Task Cfg007_reports_case_mismatched_dictionary_entry_when_default_dictionary_is_prepopulated_polymorphic()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, BaseEndpoint> Map { get; } = new()
                {
                    ["primary"] = new DerivedEndpoint()
                };
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:Primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "Primary": {
                    "Token": "secret",
                    "Url": "https://example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_polymorphic_dictionary_value_when_collection_expression_initializer_is_empty()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, BaseEndpoint> Map { get; } = [];
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "primary": {
                    "Token": "secret",
                    "Url": "https://example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_case_mismatched_prepopulated_polymorphic_dictionary_value_when_comparer_is_ignore_case()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, BaseEndpoint> Map { get; } = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["primary"] = new DerivedEndpoint()
                };
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:Primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "Primary": {
                    "Token": "secret",
                    "Url": "https://example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_dictionary_value_when_empty_comparer_constructor_has_no_prepopulated_polymorphic_value()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, BaseEndpoint> Map { get; } = new(StringComparer.OrdinalIgnoreCase);
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "primary": {
                    "Token": "secret",
                    "Url": "https://example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_nested_prepopulated_polymorphic_dictionary_value_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Dictionary<string, BaseEndpoint>> Map { get; } = new()
                {
                    ["tenant"] = new()
                    {
                        ["primary"] = new DerivedEndpoint()
                    }
                };
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 6, 11, 6, 18)
            .WithArguments("App:Map:tenant:primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "tenant": {
                    "primary": {
                      "Token": "secret",
                      "Url": "https://primary.example.test"
                    }
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_nested_prepopulated_polymorphic_dictionary_value_when_outer_comparer_is_ignore_case()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Dictionary<string, BaseEndpoint>> Map { get; } = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["tenant"] = new()
                    {
                        ["primary"] = new DerivedEndpoint()
                    }
                };
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 6, 11, 6, 18)
            .WithArguments("App:Map:TENANT:primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "TENANT": {
                    "primary": {
                      "Token": "secret",
                      "Url": "https://primary.example.test"
                    }
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_nested_prepopulated_polymorphic_dictionary_value_when_inner_comparer_matches_case_mismatch()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Dictionary<string, BaseEndpoint>> Map { get; } = new()
                {
                    ["tenant"] = new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["primary"] = new DerivedEndpoint()
                    }
                };
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 6, 11, 6, 18)
            .WithArguments("App:Map:tenant:PRIMARY:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "tenant": {
                    "PRIMARY": {
                      "Token": "secret",
                      "Url": "https://primary.example.test"
                    }
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_nested_prepopulated_polymorphic_dictionary_value_when_inner_comparer_is_assigned_in_constructor()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    Map["tenant"] = new(StringComparer.OrdinalIgnoreCase);
                    Map["tenant"]["primary"] = new DerivedEndpoint();
                }

                public Dictionary<string, Dictionary<string, BaseEndpoint>> Map { get; } = new();
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 6, 11, 6, 18)
            .WithArguments("App:Map:tenant:PRIMARY:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "tenant": {
                    "PRIMARY": {
                      "Token": "secret",
                      "Url": "https://primary.example.test"
                    }
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_nested_prepopulated_polymorphic_dictionary_value_when_only_inner_comparer_is_ignore_case()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Dictionary<string, BaseEndpoint>> Map { get; } = new()
                {
                    ["tenant"] = new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["primary"] = new DerivedEndpoint()
                    }
                };
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 6, 11, 6, 18)
            .WithArguments("App:Map:TENANT:primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "TENANT": {
                    "primary": {
                      "Token": "secret",
                      "Url": "https://primary.example.test"
                    }
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_constructor_prepopulated_polymorphic_dictionary_values_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    Map["primary"] = new DerivedEndpoint();
                    Map.Add("secondary", new DerivedEndpoint());
                }

                public Dictionary<string, BaseEndpoint> Map { get; } = new();
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expectedPrimary = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:primary:Token", "BaseEndpoint", ".");
        var expectedSecondary = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 9, 9, 9, 16)
            .WithArguments("App:Map:secondary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "primary": {
                    "Token": "secret",
                    "Url": "https://primary.example.test"
                  },
                  "secondary": {
                    "Token": "secret",
                    "Url": "https://secondary.example.test"
                  }
                }
              }
            }
            """),
            expectedPrimary,
            expectedSecondary);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_case_mismatched_constructor_prepopulated_polymorphic_dictionary_value_when_comparer_is_ignore_case()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

                public AppOptions()
                {
                    Map["primary"] = new DerivedEndpoint();
                    Map.Add("secondary", new DerivedEndpoint());
                }

                public Dictionary<string, BaseEndpoint> Map { get; } = new(Comparer);
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string AccessCode { get; set; } = "";
            }
            """);

        var expectedPrimary = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 5, 9, 5, 21)
            .WithArguments("App:Map:Primary:AccessCode", "BaseEndpoint", ".");
        var expectedSecondary = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 9, 9, 9, 21)
            .WithArguments("App:Map:Secondary:AccessCode", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "Primary": {
                    "AccessCode": "value",
                    "Url": "https://primary.example.test"
                  },
                  "Secondary": {
                    "AccessCode": "value",
                    "Url": "https://secondary.example.test"
                  }
                }
              }
            }
            """),
            expectedPrimary,
            expectedSecondary);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_case_mismatched_constructor_assigned_polymorphic_dictionary_value_when_comparer_is_ignore_case()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    Map = new(StringComparer.OrdinalIgnoreCase);
                    Map["primary"] = new DerivedEndpoint();
                }

                public Dictionary<string, BaseEndpoint> Map { get; }
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:Primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "Primary": {
                    "Token": "secret",
                    "Url": "https://primary.example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_case_mismatched_constructor_assigned_polymorphic_dictionary_value_when_iequalitycomparer_local_is_ignore_case()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    IEqualityComparer<string> comparer = StringComparer.OrdinalIgnoreCase;
                    Map = new(comparer);
                    Map["primary"] = new DerivedEndpoint();
                }

                public Dictionary<string, BaseEndpoint> Map { get; }
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:Primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "Primary": {
                    "Token": "secret",
                    "Url": "https://primary.example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_case_mismatched_constructor_assigned_polymorphic_dictionary_value_when_iequalitycomparer_field_is_ignore_case()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                private static readonly IEqualityComparer<string> Comparer = StringComparer.OrdinalIgnoreCase;

                public AppOptions()
                {
                    Map = new(Comparer);
                    Map["primary"] = new DerivedEndpoint();
                }

                public Dictionary<string, BaseEndpoint> Map { get; }
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:Primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "Primary": {
                    "Token": "secret",
                    "Url": "https://primary.example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_dictionary_value_when_constructor_prepopulation_is_inside_uninvoked_lambda()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    System.Action later = () => Map["primary"] = new DerivedEndpoint();
                }

                public Dictionary<string, BaseEndpoint> Map { get; } = new();
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "primary": {
                    "Token": "secret",
                    "Url": "https://example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_dictionary_value_when_polymorphic_prepopulation_is_in_unused_constructor_overload()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    Map["primary"] = new BaseEndpoint();
                }

                public AppOptions(string ignored)
                {
                    Map["primary"] = new DerivedEndpoint();
                }

                public Dictionary<string, BaseEndpoint> Map { get; } = new();
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 16)
            .WithArguments("App:Map:primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "primary": {
                    "Token": "secret",
                    "Url": "https://primary.example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_constructor_bound_prepopulated_polymorphic_dictionary_value_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions(string name)
                {
                    Name = name;
                    Map["primary"] = new DerivedEndpoint();
                }

                public string Name { get; }

                public Dictionary<string, BaseEndpoint> Map { get; } = new();
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 6, 9, 6, 16)
            .WithArguments("App:Map:primary:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Name": "primary",
                "Map": {
                  "primary": {
                    "Token": "secret",
                    "Url": "https://primary.example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_clr_member_shaped_child_key_under_string_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, string> Labels { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 17)
            .WithArguments("App:Labels:primary:Length", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Labels": {
                  "primary": {
                    "Length": 5
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_unrelated_binder_options_enable_error_on_unknown_configuration()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    var unrelated = new BinderOptions();
                    unrelated.ErrorOnUnknownConfiguration = true;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_strict_binding_ignores_private_set_property_name()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

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
    public async Task Cfg006_stays_info_when_strict_binding_ignores_private_property_name()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                private string ApiKey { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

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
    public async Task Cfg006_stays_info_when_strict_binding_ignores_get_only_scalar_property_name()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

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
    public async Task Cfg006_stays_info_for_initialized_polymorphic_nested_property_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public BaseEndpoint Endpoint { get; set; } = new DerivedEndpoint();
            }

            public abstract class BaseEndpoint
            {
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 7, 4, 12)
            .WithArguments("App:Endpoint:Url", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Url": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_unknown_child_under_null_initialized_settable_nested_property_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public EndpointOptions? Endpoint { get; set; } = null!;
            }

            public class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 13)
            .WithArguments("App:Endpoint:Urll", "EndpointOptions", ". Did you mean \"Url\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_null_initialized_get_only_nested_property_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public EndpointOptions? Endpoint { get; } = null!;
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 15)
            .WithArguments("App:Endpoint", "AppOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_initialized_polymorphic_nested_property_with_same_simple_type_name_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public Alpha.Endpoint Endpoint { get; set; } = new Beta.Endpoint();
            }

            namespace Alpha
            {
                public class Endpoint
                {
                    public string Url { get; set; } = "";
                }
            }

            namespace Beta
            {
                public class Endpoint : Alpha.Endpoint
                {
                    public string Token { get; set; } = "";
                }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 7, 4, 14)
            .WithArguments("App:Endpoint:Token", "Endpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Token": "secret",
                  "Url": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_initialized_polymorphic_nested_property_with_type_alias_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Endpoint = Beta.Endpoint;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Alpha.Endpoint Endpoint { get; set; } = new Endpoint();
            }

            namespace Alpha
            {
                public class Endpoint
                {
                    public string Url { get; set; } = "";
                }
            }

            namespace Beta
            {
                public class Endpoint : Alpha.Endpoint
                {
                    public string Token { get; set; } = "";
                }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 7, 4, 14)
            .WithArguments("App:Endpoint:Token", "Endpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Token": "secret",
                  "Url": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_constructor_initialized_polymorphic_nested_property_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    Endpoint = new DerivedEndpoint();
                }

                public BaseEndpoint Endpoint { get; set; }
            }

            public abstract class BaseEndpoint
            {
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 7, 4, 12)
            .WithArguments("App:Endpoint:Url", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Url": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_inherited_constructor_initialized_polymorphic_nested_property_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions : BaseOptions
            {
                public AppOptions()
                {
                    Pet = new DogOptions();
                }
            }

            public class BaseOptions
            {
                public PetOptions Pet { get; set; } = new();
            }

            public class PetOptions
            {
                public string Name { get; set; } = "";
            }

            public sealed class DogOptions : PetOptions
            {
                public string Breed { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 7, 4, 14)
            .WithArguments("App:Pet:Breed", "PetOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Pet": {
                  "Breed": "Lab"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_protected_base_constructor_initialized_get_only_object_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions : BaseOptions
            {
            }

            public class BaseOptions
            {
                protected BaseOptions()
                {
                    Endpoint = new EndpointOptions();
                }

                public EndpointOptions Endpoint { get; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 13)
            .WithArguments("App:Endpoint:Urll", "EndpointOptions", ". Did you mean \"Url\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_derived_constructor_chains_away_from_base_initializer()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions : BaseOptions
            {
                public AppOptions()
                    : base("skip")
                {
                }
            }

            public class BaseOptions
            {
                protected BaseOptions()
                {
                    Endpoint = new EndpointOptions();
                }

                protected BaseOptions(string ignored)
                {
                }

                public EndpointOptions? Endpoint { get; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 15)
            .WithArguments("App:Endpoint", "AppOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_constructor_initialized_get_only_object_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    Endpoint = new EndpointOptions();
                }

                public EndpointOptions Endpoint { get; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 13)
            .WithArguments("App:Endpoint:Urll", "EndpointOptions", ". Did you mean \"Url\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_this_chained_constructor_initialized_get_only_object_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                    : this("primary")
                {
                }

                public AppOptions(string name)
                {
                    Name = name;
                    Endpoint = new EndpointOptions();
                }

                public string Name { get; }

                public EndpointOptions Endpoint { get; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 13)
            .WithArguments("App:Endpoint:Urll", "EndpointOptions", ". Did you mean \"Url\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_constructor_initialized_get_only_object_is_in_unused_overload()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                }

                public AppOptions(string ignored)
                {
                    Endpoint = new EndpointOptions();
                }

                public EndpointOptions? Endpoint { get; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 15)
            .WithArguments("App:Endpoint", "AppOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_constructor_get_only_object_assignment_is_conditional()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    if (System.DateTime.UtcNow.Ticks < 0)
                    {
                        Endpoint = new EndpointOptions();
                    }
                }

                public EndpointOptions? Endpoint { get; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 15)
            .WithArguments("App:Endpoint", "AppOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_constructor_private_set_object_assignment_is_inside_lambda()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    System.Action initialize = () => Endpoint = new EndpointOptions();
                }

                public EndpointOptions? Endpoint { get; private set; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 15)
            .WithArguments("App:Endpoint", "AppOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_when_constructor_polymorphic_assignment_is_inside_uninvoked_lambda()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    Endpoint = new BaseEndpoint();
                    System.Action later = () => Endpoint = new DerivedEndpoint();
                }

                public BaseEndpoint Endpoint { get; set; }
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 14)
            .WithArguments("App:Endpoint:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Token": "secret",
                  "Url": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_when_polymorphic_constructor_assignment_is_in_unused_overload()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    Endpoint = new BaseEndpoint();
                }

                public AppOptions(string ignored)
                {
                    Endpoint = new DerivedEndpoint();
                }

                public BaseEndpoint Endpoint { get; }
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 14)
            .WithArguments("App:Endpoint:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Token": "secret",
                  "Url": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_when_constructor_polymorphic_assignment_targets_another_instance()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    Endpoint = new BaseEndpoint();
                    var other = new AppOptions("other");
                    other.Endpoint = new DerivedEndpoint();
                }

                private AppOptions(string ignored)
                {
                    Endpoint = new BaseEndpoint();
                }

                public BaseEndpoint Endpoint { get; set; }
            }

            public class BaseEndpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class DerivedEndpoint : BaseEndpoint
            {
                public string Token { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 14)
            .WithArguments("App:Endpoint:Token", "BaseEndpoint", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Token": "secret",
                  "Url": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_when_nested_lambda_return_precedes_constructor_initializer()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    System.Action noop = () => { return; };
                    Endpoint = new EndpointOptions();
                }

                public EndpointOptions Endpoint { get; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 13)
            .WithArguments("App:Endpoint:Urll", "EndpointOptions", ". Did you mean \"Url\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_constructor_bound_initialized_get_only_object_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions(string name)
                {
                    Name = name;
                    Endpoint = new EndpointOptions();
                }

                public string Name { get; }

                public EndpointOptions Endpoint { get; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 7, 5, 13)
            .WithArguments("App:Endpoint:Urll", "EndpointOptions", ". Did you mean \"Url\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Name": "primary",
                "Endpoint": {
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_constructor_assignment_targets_shadowing_local()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    EndpointOptions Endpoint;
                    Endpoint = new EndpointOptions();
                }

                public EndpointOptions? Endpoint { get; }
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 15)
            .WithArguments("App:Endpoint", "AppOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Urll": "https://example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_default_initialized_get_only_clr_only_property_when_strict_binding_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Uri? Endpoint { get; } = default!;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 15)
            .WithArguments("App:Endpoint", "AppOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Bad": "example.test"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_constructor_initialized_get_only_collection_or_dictionary_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public AppOptions()
                {
                    Items = new();
                    Labels = new();
                }

                public List<string> Items { get; }

                public Dictionary<string, string> Labels { get; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Items": [
                  "alpha"
                ],
                "Labels": {
                  "primary": "value"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_get_only_value_type_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public int Port { get; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 12)
            .WithArguments("App:Port:Foo", "Int32", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Port": {
                  "Foo": 443
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_initialized_private_set_scalar_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 12)
            .WithArguments("Stripe:ApiKey:Foo", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": {
                  "Foo": "x"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_initialized_static_scalar_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public static string ApiKey { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 7, 4, 12)
            .WithArguments("Stripe:ApiKey:Foo", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": {
                  "Foo": "x"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_for_uninitialized_get_only_reference_property_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string? ApiKey { get; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": {
                  "Foo": "x"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_may_be_skipped_by_early_return()
    {
        var source = OptionsSource("""
            var strict = GetStrict();
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    if (!strict)
                    {
                        return;
                    }

                    options.ErrorOnUnknownConfiguration = true;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraMembers: """
            private static bool GetStrict()
            {
                return false;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_to_false()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    options.ErrorOnUnknownConfiguration = false;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_set_false_via_tuple_deconstruction()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    (options.ErrorOnUnknownConfiguration, options.BindNonPublicProperties) = (false, false);
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_changed_by_compound_assignment()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    options.ErrorOnUnknownConfiguration &= false;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_binder_options_parameter_is_reassigned_before_strict_assignment()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options = new Microsoft.Extensions.Configuration.BinderOptions();
                    options.ErrorOnUnknownConfiguration = true;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_tuple_deconstruction_aliases_binder_options_to_another_variable()
    {
        var source = OptionsSource("""
            BinderOptions alias = null!;
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    (options.ErrorOnUnknownConfiguration, alias) = (true, options);
                    alias.ErrorOnUnknownConfiguration = false;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_tuple_deconstruction_reassigns_binder_options_parameter()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    (options.ErrorOnUnknownConfiguration, options) = (true, new BinderOptions());
                    options.ErrorOnUnknownConfiguration = false;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_tuple_deconstruction_captures_binder_options_into_helper_argument()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    int unused;
                    (options.ErrorOnUnknownConfiguration, unused) = (true, Capture(options));
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", extraMembers: """
            private static int Capture(BinderOptions options)
            {
                options.ErrorOnUnknownConfiguration = false;
                return 0;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_parameter_alias()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    var same = options;
                    options.ErrorOnUnknownConfiguration = true;
                    same.ErrorOnUnknownConfiguration = false;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_nested_parameter_alias()
    {
        var source = OptionsSource("""
            var flag = GetFlag();
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    if (flag)
                    {
                        var alias = options;
                        alias.ErrorOnUnknownConfiguration = false;
                    }
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraMembers: """
            private static bool GetFlag()
            {
                return false;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_helper_escape()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    DisableStrict(options);
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", extraMembers: """
            private static void DisableStrict(BinderOptions options)
            {
                options.ErrorOnUnknownConfiguration = false;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_invoked_local_function()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    void DisableStrict() => options.ErrorOnUnknownConfiguration = false;
                    DisableStrict();
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_invoked_delegate()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    System.Action disableStrict = () => options.ErrorOnUnknownConfiguration = false;
                    disableStrict();
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_delegate_passed_as_argument()
    {
        // The reset delegate is passed as an argument to a helper that invokes it, so the
        // runtime binder options escape the strict-binding proof just as a directly-invoked
        // reset delegate does. CFG007 must stay conservative (CFG006 Info) rather than fire.
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    System.Action disableStrict = () => options.ErrorOnUnknownConfiguration = false;
                    RunNow(disableStrict);
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraMembers: """
            private static void RunNow(System.Action action) => action();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_inline_lambda_argument()
    {
        // The reset lambda is passed inline as an argument to a helper that invokes it.
        // Same escape as the named-delegate case; CFG007 must stay conservative.
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    RunNow(() => options.ErrorOnUnknownConfiguration = false);
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraMembers: """
            private static void RunNow(System.Action action) => action();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_invoked_delegate_invoke_method()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    System.Action disableStrict = () => options.ErrorOnUnknownConfiguration = false;
                    disableStrict.Invoke();
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_conditional_delegate_invoke()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    System.Action? disableStrict = () => options.ErrorOnUnknownConfiguration = false;
                    disableStrict?.Invoke();
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_reassigned_invoked_delegate()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    System.Action later = () => { };
                    later = () => options.ErrorOnUnknownConfiguration = false;
                    later();
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_through_extension_helper_escape()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    options.DisableStrict();
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";

                public string WebhookSecret { get; set; } = "";
            }

            public static class BinderOptionsExtensions
            {
                public static void DisableStrict(this BinderOptions options)
                {
                    options.ErrorOnUnknownConfiguration = false;
                }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_escapes_to_constructor()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    _ = new StrictDisabler(options);
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", extraMembers: """
            private sealed class StrictDisabler
            {
                public StrictDisabler(BinderOptions options)
                {
                    options.ErrorOnUnknownConfiguration = false;
                }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_escapes_to_static_field_alias()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    BinderOptionsStore.Stored = options;
                    BinderOptionsStore.Stored.ErrorOnUnknownConfiguration = false;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", extraMembers: """
            private static class BinderOptionsStore
            {
                public static BinderOptions Stored = null!;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_escapes_to_static_property_alias()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    BinderOptionsStore.Stored = options;
                    BinderOptionsStore.Stored.ErrorOnUnknownConfiguration = false;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", extraMembers: """
            private static class BinderOptionsStore
            {
                public static BinderOptions Stored { get; set; } = null!;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "value",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_when_strict_assignment_precedes_binder_options_parameter_reassignment()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    options = new Microsoft.Extensions.Configuration.BinderOptions();
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_when_nested_binder_options_lambda_assignment_follows_strict_assignment()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    System.Action later = () => options.ErrorOnUnknownConfiguration = false;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_is_reset_to_non_constant_value()
    {
        var source = OptionsSource("""
            var strict = GetStrict();
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    options.ErrorOnUnknownConfiguration = strict;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraMembers: """
            private static bool GetStrict()
            {
                return false;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_error_on_unknown_configuration_may_be_reset_in_control_flow()
    {
        var source = OptionsSource("""
            var disableStrict = GetStrict();
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    if (disableStrict)
                    {
                        options.ErrorOnUnknownConfiguration = false;
                    }
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraMembers: """
            private static bool GetStrict()
            {
                return false;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_stays_info_when_tuple_deconstruction_reset_is_nested_in_control_flow()
    {
        var source = OptionsSource("""
            var disableStrict = GetStrict();
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.ErrorOnUnknownConfiguration = true;
                    if (disableStrict)
                    {
                        (options.ErrorOnUnknownConfiguration, options.BindNonPublicProperties) = (false, false);
                    }
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraMembers: """
            private static bool GetStrict()
            {
                return false;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_from_named_options_builder()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>("tenant")
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_from_bind_get_section()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Stripe"))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_from_bind_get_required_section()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetRequiredSection("Stripe"))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_from_commented_appsettings_file()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 6, 5, 6, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              // comments are accepted by the .NET JSON configuration provider
              "Stripe": {
                "ApiKey": "secret",
                /* typo kept visible to the analyzer */
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_ignores_non_dot_qualified_appsettings_like_files()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettingsSchema.json", """
            {
              "Stripe": {
                "WebookSecret": "typo"
              }
            }
            """));
    }

    [Fact]
    public async Task Configure_reports_missing_section_without_validation_diagnostics()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<StripeOptions>(configuration.GetSection({|#0:"Strpie"|}));
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

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
    public async Task Configure_named_options_reports_missing_section_without_validation_diagnostics()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<StripeOptions>("tenant", configuration.GetSection({|#0:"Strpie"|}));
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

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
    public async Task Configure_ignores_root_configuration_and_lambda_configuration()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<StripeOptions>(configuration);
            services.Configure<StripeOptions>(options => options.ApiKey = "secret");
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

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
    public async Task Configure_reports_unknown_key_without_validation_diagnostics()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<StripeOptions>(configuration.GetSection("Stripe"));
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Configure_reports_unknown_key_from_get_required_section_without_validation_diagnostics()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<StripeOptions>(configuration.GetRequiredSection("Stripe"));
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n");

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_honors_configuration_key_name_alias()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [Required]
                [ConfigurationKeyName("api_key")]
                public string ApiKey { get; set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "api_key": "secret"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_reports_property_name_when_configuration_key_name_overrides_binding_name()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                [ConfigurationKeyName("api_key")]
                public string ApiKey { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ". Did you mean \"api_key\"?");

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
    public async Task Cfg006_does_not_report_constructor_bound_record_property()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed record StripeOptions(string ApiKey);
            """);

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
    public async Task Cfg006_does_not_report_constructor_bound_inherited_property()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public abstract class BaseStripeOptions
            {
                protected BaseStripeOptions(string apiKey)
                {
                    ApiKey = apiKey;
                }

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

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebhookSecret": "hook"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_does_not_report_nested_constructor_bound_record_property()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed record AppOptions(DatabaseOptions Database);

            public sealed record DatabaseOptions(string ConnectionString);
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Database": {
                  "ConnectionString": "Server=.;"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_reports_configuration_key_name_alias_on_constructor_bound_property()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed record StripeOptions([property: ConfigurationKeyName("api_key")] string ApiKey);
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 14)
            .WithArguments("Stripe:api_key", "StripeOptions", ". Did you mean \"ApiKey\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "api_key": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_does_not_report_configuration_key_name_alias_on_settable_constructor_bound_property()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                public StripeOptions(string apiKey)
                {
                    ApiKey = apiKey;
                }

                [ConfigurationKeyName("api_key")]
                public string ApiKey { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "constructor",
                "api_key": "setter"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_does_not_report_configuration_key_name_alias_when_constructor_parameter_has_default()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                public StripeOptions(string apiKey = "")
                {
                    ApiKey = apiKey;
                }

                [ConfigurationKeyName("api_key")]
                public string ApiKey { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "api_key": "setter"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_does_not_report_private_set_configuration_key_name_alias_when_bind_non_public_properties_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.BindNonPublicProperties = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                public StripeOptions(string apiKey)
                {
                    ApiKey = apiKey;
                }

                [ConfigurationKeyName("api_key")]
                public string ApiKey { get; private set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "constructor",
                "api_key": "setter"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_reports_private_set_configuration_key_name_alias_when_bind_non_public_properties_disabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                public StripeOptions(string apiKey)
                {
                    ApiKey = apiKey;
                }

                [ConfigurationKeyName("api_key")]
                public string ApiKey { get; private set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 14)
            .WithArguments("Stripe:api_key", "StripeOptions", ". Did you mean \"ApiKey\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "constructor",
                "api_key": "setter"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_get_only_property_when_public_parameterless_constructor_wins()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public StripeOptions()
                {
                    ApiKey = "";
                }

                public StripeOptions(string apiKey)
                {
                    ApiKey = apiKey;
                }

                public string ApiKey { get; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

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
    public async Task Cfg006_reports_get_only_property_when_constructor_binding_is_ambiguous()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
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

                public string ApiKey { get; }

                public string WebhookSecret { get; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

        var secondExpected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 20)
            .WithArguments("Stripe:WebhookSecret", "StripeOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "secret",
                "WebhookSecret": "hook"
              }
            }
            """),
            expected,
            secondExpected);
    }

    [Fact]
    public async Task Cfg006_does_not_report_private_set_property_when_bind_non_public_properties_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.BindNonPublicProperties = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
            """);

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
    public async Task Cfg006_does_not_report_private_set_property_when_bind_non_public_properties_is_enabled_before_return()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    options.BindNonPublicProperties = true;
                    return;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
            """);

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
    public async Task Cfg006_does_not_report_bind_get_section_private_set_property_when_bind_non_public_properties_enabled()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Stripe"), options => options.BindNonPublicProperties = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
            """);

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
    public async Task Cfg006_does_not_report_private_set_property_when_parenthesized_bind_non_public_properties_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", (options) => options.BindNonPublicProperties = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
            """);

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
    public async Task Cfg006_reports_private_set_property_when_bind_non_public_properties_is_false()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.BindNonPublicProperties = false)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

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
    public async Task Cfg006_reports_private_set_property_when_binder_options_block_does_not_enable_non_public_properties()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => { })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

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
    public async Task Cfg006_reports_private_set_property_when_bind_non_public_properties_is_not_constant_true()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options => options.BindNonPublicProperties = IsEnabled())
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraMembers: """
            private static bool IsEnabled() => true;
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

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
    public async Task Cfg006_reports_private_set_property_when_bind_non_public_properties_assignment_is_not_binder_options()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    var unrelated = new BinderOptionLookalike();
                    unrelated.BindNonPublicProperties = true;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraMembers: """
            private sealed class BinderOptionLookalike
            {
                public bool BindNonPublicProperties { get; set; }
            }
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

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
    public async Task Cfg006_reports_private_set_property_when_unrelated_binder_options_are_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe", options =>
                {
                    var unrelated = new BinderOptions();
                    unrelated.BindNonPublicProperties = true;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

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
    public async Task Cfg006_reports_private_set_property_without_bind_non_public_properties()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 13)
            .WithArguments("Stripe:ApiKey", "StripeOptions", ".");

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
    public async Task Configure_does_not_report_private_set_property_when_bind_non_public_properties_enabled()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            services.Configure<StripeOptions>(
                configuration.GetSection("Stripe"),
                options =>
                {
                    options.BindNonPublicProperties = true;
                });
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; private set; } = "";
            }
            """);

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
    public async Task Cfg006_honors_json_unicode_escapes_in_configuration_keys()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "Api\u004Bey": "secret"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_under_colon_delimited_json_section_key()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Features:Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 5, 4, 19)
            .WithArguments("Features:Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Features:Stripe": {
                "ApiKey": "secret",
                "WebookSecret": "typo"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_under_colon_delimited_json_leaf_key()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Features:Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 3, 3, 33)
            .WithArguments("Features:Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Features:Stripe:ApiKey": "secret",
              "Features:Stripe:WebookSecret": "typo"
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_checks_colon_delimited_leaf_keys_when_object_section_also_exists()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Features:Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 5, 3, 5, 33)
            .WithArguments("Features:Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Features": {
                "Stripe": { "ApiKey": "secret" }
              },
              "Features:Stripe:WebookSecret": "typo"
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_under_nested_options_object()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions Database { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                public string ConnectionString { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 7, 4, 24)
            .WithArguments("App:Database:ConnetionString", "DatabaseOptions", ". Did you mean \"ConnectionString\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Database": {
                  "ConnetionString": "Server=.;"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_under_nested_struct_options_object()
    {
        // The nested options property is a struct. The real ConfigurationBinder binds and
        // recurses into struct-typed properties, so an unknown key under it must be flagged
        // just as it is under a class-typed nested object.
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions Database { get; set; }
            }

            public struct DatabaseOptions
            {
                public string ConnectionString { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 7, 4, 24)
            .WithArguments("App:Database:ConnetionString", "DatabaseOptions", ". Did you mean \"ConnectionString\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Database": {
                  "ConnetionString": "Server=.;"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_treats_get_only_struct_nested_object_as_non_bindable()
    {
        // A get-only struct property cannot be bound in place — the binder mutates a copy it
        // cannot write back through the read-only property — so it is treated as non-bindable
        // (the same as before struct nested objects were recognized at all). The analyzer must
        // NOT recurse into it (no deep `App:Database:ConnetionString` typo report); the whole
        // `App:Database` section is instead the unmatched key, since nothing binds it. This is
        // the conservative outcome that avoids claiming a required member is satisfied when the
        // runtime binder never populates the struct.
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions Database { get; } = new();
            }

            public struct DatabaseOptions
            {
                public string ConnectionString { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 15)
            .WithArguments("App:Database", "AppOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Database": {
                  "ConnetionString": "Server=.;"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_checks_sibling_colon_delimited_nested_keys_under_one_projected_object()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions Database { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                public string Host { get; set; } = "";

                public int Port { get; set; }
            }
            """);

        var expectedHost = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 2, 3, 2, 22)
            .WithArguments("App:Database:Hots", "DatabaseOptions", ". Did you mean \"Host\"?");

        var expectedPort = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 3, 3, 21)
            .WithArguments("App:Database:Prt", "DatabaseOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App:Database:Hots": "api",
              "App:Database:Prt": 443
            }
            """),
            expectedHost,
            expectedPort);
    }

    [Fact]
    public async Task Cfg006_keeps_projected_object_when_later_colon_delimited_scalar_uses_same_path()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions Database { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                public string Host { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 2, 3, 2, 22)
            .WithArguments("App:Database:Hots", "DatabaseOptions", ". Did you mean \"Host\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App:Database:Hots": "api",
              "App:Database": "ignored scalar"
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_under_nested_object_in_user_namespace_starting_with_system()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Systematic.Options;\n", optionsTypes: """
            namespace Systematic.Options
            {
                public sealed class AppOptions
                {
                    public DatabaseOptions Database { get; set; } = new();
                }

                public sealed class DatabaseOptions
                {
                    public string ConnectionString { get; set; } = "";
                }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 7, 4, 24)
            .WithArguments("App:Database:ConnetionString", "DatabaseOptions", ". Did you mean \"ConnectionString\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Database": {
                  "ConnetionString": "Server=.;"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_honors_configuration_key_name_alias_under_nested_options_object()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions Database { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [ConfigurationKeyName("connection_string")]
                public string ConnectionString { get; set; } = "";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Database": {
                  "connection_string": "Server=.;"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_reports_alias_declared_only_on_overridden_virtual_property()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public class BaseOptions
            {
                public virtual string ApiKey { get; set; } = "";
            }

            public sealed class StripeOptions : BaseOptions
            {
                [ConfigurationKeyName("api_key")]
                public override string ApiKey { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 3, 5, 3, 14)
            .WithArguments("Stripe:api_key", "StripeOptions", ". Did you mean \"ApiKey\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "api_key": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_property_name_when_nested_configuration_key_name_overrides_binding_name()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions Database { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [ConfigurationKeyName("connection_string")]
                public string ConnectionString { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 4, 7, 4, 25)
            .WithArguments("App:Database:ConnectionString", "DatabaseOptions", ". Did you mean \"connection_string\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Database": {
                  "ConnectionString": "Server=.;"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_does_not_recurse_into_scalar_property_with_object_value()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": {
                  "Value": "secret"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_does_not_recurse_into_system_namespace_object_property()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public System.Uri Endpoint { get; set; } = new("https://example.test");
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoint": {
                  "Hots": "example.test"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_under_collection_item_object()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<ServerOptions> Servers { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";

                public int Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 6, 9, 6, 14)
            .WithArguments("App:Servers:0:Prt", "ServerOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Servers": [
                  {
                    "Host": "api",
                    "Prt": 443
                  }
                ]
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_under_initialized_get_only_collection_item_object()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<ServerOptions> Servers { get; } = [];
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";

                public int Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 6, 9, 6, 14)
            .WithArguments("App:Servers:0:Prt", "ServerOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Servers": [
                  {
                    "Host": "api",
                    "Prt": 443
                  }
                ]
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_does_not_report_scalar_array_items()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public string[] AllowedHosts { get; set; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "AllowedHosts": [
                  "api.example.com",
                  "admin.example.com"
                ]
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_does_not_report_initialized_get_only_scalar_collection_items()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<string> AllowedHosts { get; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "AllowedHosts": [
                  "api.example.com",
                  "admin.example.com"
                ]
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_does_not_report_dictionary_entries_as_unknown_properties()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, string> Labels { get; set; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Labels": {
                  "tier": "gold",
                  "region": "eu"
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_under_dictionary_value_object()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, ServerOptions> Servers { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";

                public int Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 6, 9, 6, 14)
            .WithArguments("App:Servers:primary:Prt", "ServerOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Servers": {
                  "primary": {
                    "Host": "example.test",
                    "Prt": 443
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_under_initialized_get_only_dictionary_value_object()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, ServerOptions> Servers { get; } = [];
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";

                public int Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 6, 9, 6, 14)
            .WithArguments("App:Servers:primary:Prt", "ServerOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Servers": {
                  "primary": {
                    "Host": "example.test",
                    "Prt": 443
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_does_not_report_valid_dictionary_value_object_keys()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, ServerOptions> Servers { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";

                public int Port { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Servers": {
                  "primary": {
                    "Host": "example.test",
                    "Port": 443
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_reports_unknown_key_under_dictionary_value_object_collection()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, List<ServerOptions>> ServersByRegion { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";

                public int Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.json", 7, 11, 7, 16)
            .WithArguments("App:ServersByRegion:eu:0:Prt", "ServerOptions", ". Did you mean \"Port\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "ServersByRegion": {
                  "eu": [
                    {
                      "Host": "example.test",
                      "Prt": 443
                    }
                  ]
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg006_does_not_report_valid_dictionary_value_object_collection_keys()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, List<ServerOptions>> ServersByRegion { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";

                public int Port { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "ServersByRegion": {
                  "eu": [
                    {
                      "Host": "example.test",
                      "Port": 443
                    }
                  ]
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg006_checks_every_matching_section_across_appsettings_files()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
            .WithSpan("appsettings.Production.json", 3, 5, 3, 19)
            .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            new[]
            {
                ("appsettings.json", """
                {
                  "Stripe": {
                    "ApiKey": "secret"
                  }
                }
                """),
                ("appsettings.Production.json", """
                {
                  "Stripe": {
                    "WebookSecret": "typo"
                  }
                }
                """)
            },
            expected);
    }

    private const string ServerValueOptions = """
        public sealed class ServerOptions
        {
            public TYPE Value { get; set; }
        }
        """;

    private static string ServerOptionsOf(string type) => ServerValueOptions.Replace("TYPE", type);

    private static string BindServer =>
        """
        services.AddOptions<ServerOptions>()
            .BindConfiguration("Server");
        """;

    [Theory]
    [InlineData("int", "\"eighty\"", 22)]
    [InlineData("long", "\"x\"", 17)]
    [InlineData("double", "\"abc\"", 19)]
    [InlineData("decimal", "\"abc\"", 19)]
    [InlineData("System.Guid", "\"not-a-guid\"", 26)]
    [InlineData("System.TimeSpan", "\"banana\"", 22)]
    [InlineData("System.DateTime", "\"not-a-date\"", 26)]
    public async Task Cfg008_reports_value_that_cannot_convert_to_target_type(string type, string jsonValue, int endColumn)
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf(type));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, endColumn)
            .WithArguments("Server:Value", TypeDisplay(type));

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", $$"""
            {
              "Server": {
                "Value": {{jsonValue}}
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_json_bool_value_bound_to_integer()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 18)
            .WithArguments("Server:Value", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": true
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_unknown_enum_member()
    {
        var source = OptionsSource(
            BindServer,
            optionsTypes: """
            public enum Level { Low, High }

            public sealed class ServerOptions
            {
                public Level Value { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 20)
            .WithArguments("Server:Value", "Level");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "Loud"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_non_bool_string_bound_to_bool()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("bool"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 19)
            .WithArguments("Server:Value", "bool");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "yes"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_decimal_string_with_thousands_separator()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("decimal"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 21)
            .WithArguments("Server:Value", "decimal");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "1,000"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_numeric_enum_value_outside_underlying_range()
    {
        var source = OptionsSource(BindServer, optionsTypes: """
            public enum ByteColor : byte
            {
                Red,
            }

            public sealed class ServerOptions
            {
                public ByteColor Value { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 19)
            .WithArguments("Server:Value", "ByteColor");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "256"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_empty_string_bound_to_non_nullable_integer()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 16)
            .WithArguments("Server:Value", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": ""
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_multi_char_string_bound_to_char()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("char"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 18)
            .WithArguments("Server:Value", "char");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "ab"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_bad_value_for_nullable_target()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int?"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 22)
            .WithArguments("Server:Value", "int?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_value_bound_through_bind_get_section()
    {
        var source = OptionsSource(
            """
            IConfiguration configuration = null!;
            services.AddOptions<ServerOptions>()
                .Bind(configuration.GetSection("Server"));
            """,
            extraUsings: "using Microsoft.Extensions.Configuration;\n",
            optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 22)
            .WithArguments("Server:Value", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_value_bound_through_configure_get_section()
    {
        var source = OptionsSource(
            """
            IConfiguration configuration = null!;
            services.Configure<ServerOptions>(configuration.GetSection("Server"));
            """,
            extraUsings: "using Microsoft.Extensions.Configuration;\n",
            optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 22)
            .WithArguments("Server:Value", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "eighty"
              }
            }
            """),
            expected);
    }

    [Theory]
    [InlineData("int", "8080")]
    [InlineData("int", "\"8080\"")]
    [InlineData("int", "\"0x1F\"")]
    [InlineData("int", "\"#FF\"")]
    [InlineData("bool", "true")]
    [InlineData("bool", "\"TRUE\"")]
    [InlineData("char", "\"a\"")]
    [InlineData("char", "\"\"")]
    [InlineData("System.DateTime", "\"2020-01-02\"")]
    [InlineData("System.DateTime", "\"\"")]
    [InlineData("System.Guid", "\"d3b07384-d9a0-4c9b-8b5e-000000000000\"")]
    [InlineData("string", "\"anything\"")]
    [InlineData("object", "\"anything\"")]
    [InlineData("int", "null")]
    public async Task Cfg008_does_not_report_convertible_or_skipped_value(string type, string jsonValue)
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf(type));

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", $$"""
            {
              "Server": {
                "Value": {{jsonValue}}
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_does_not_report_numeric_or_comma_list_enum_values()
    {
        var source = OptionsSource(
            BindServer,
            optionsTypes: """
            [System.Flags]
            public enum Access { None = 0, Read = 1, Write = 2 }

            public sealed class ServerOptions
            {
                public Access Value { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "Read, Write"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_does_not_report_scalar_given_to_nested_object_property()
    {
        var source = OptionsSource(
            BindServer,
            optionsTypes: """
            public sealed class Nested { public string Name { get; set; } = ""; }

            public sealed class ServerOptions
            {
                public Nested Value { get; set; } = new();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg009_reports_missing_get_required_section_with_suggestion()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Strpie"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_static_get_required_section_call()
    {
        var source = DirectReadSource("""
            _ = ConfigurationExtensions.GetRequiredSection(configuration, {|#0:"Strpie"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_static_get_connection_string_with_named_arguments()
    {
        var source = DirectReadSource("""
            _ = ConfigurationExtensions.GetConnectionString(name: {|#0:"Databsae"|}, configuration: configuration);
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("ConnectionStrings:Databsae", ". Did you mean \"ConnectionStrings:Database\"?");

        await Verifier.VerifyAnalyzerAsync(source, DatabaseConnectionAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_static_get_call()
    {
        var source = DirectReadSource("""
            _ = ConfigurationBinder.Get<ServerOptions>(configuration.GetSection({|#0:"Strpie"|}));
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_static_bind_call()
    {
        var source = DirectReadSource("""
            ConfigurationBinder.Bind(configuration.GetSection({|#0:"Strpie"|}), new ServerOptions());
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_get_required_section_on_injected_field()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            private readonly IConfiguration _configuration = null!;

            public void ReadField()
            {
                _ = _configuration.GetRequiredSection({|#0:"Missing"|});
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_get_required_section_on_configuration_root()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            public void ReadRoot(IConfigurationRoot root)
            {
                _ = root.GetRequiredSection({|#0:"Missing"|});
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_ignores_missing_section_bound_through_get_without_typo_evidence()
    {
        var source = DirectReadSource("""
            _ = configuration.GetSection("Missing").Get<ServerOptions>();
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_missing_section_bound_through_bind_without_typo_evidence()
    {
        var source = DirectReadSource("""
            configuration.GetSection("Missing").Bind(new ServerOptions());
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_reports_missing_chained_child_section_with_suggestion()
    {
        var source = DirectReadSource("""
            _ = configuration.GetSection("Parent").GetRequiredSection({|#0:"Chlid"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Parent:Chlid", ". Did you mean \"Parent:Child\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Parent": {
                "Child": {
                  "Name": "value"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_colon_delimited_path()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Foo:Bar"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Foo:Bar", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_section_from_constant_key()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetRequiredSection({|#0:SectionKey|});
            """,
            extraMembers: """
            private const string SectionKey = "Missing";
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_section_from_nameof_key()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:nameof(ServerOptions)|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("ServerOptions", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_get_required_section_through_conditional_access()
    {
        var source = DirectReadSource("""
            _ = configuration?.GetRequiredSection({|#0:"Missing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_bound_section_through_deep_conditional_access()
    {
        var source = DirectReadSource("""
            _ = configuration?.GetSection("Parent")?.GetSection({|#0:"Chlid"|})?.Get<ServerOptions>();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Parent:Chlid", ". Did you mean \"Parent:Child\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Parent": { "Child": { "Value": "present" } } }"""),
            expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_bound_root_section_through_conditional_access()
    {
        var source = DirectReadSource("""
            _ = configuration?.GetSection({|#0:"Chlid"|})?.Get<ServerOptions>();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Chlid", ". Did you mean \"Child\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Child": { "Value": "present" } }"""),
            expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_bind_section_through_conditional_access()
    {
        var source = DirectReadSource("""
            configuration?.GetSection({|#0:"Chlid"|})?.Bind(new ServerOptions());
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Chlid", ". Did you mean \"Child\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Child": { "Value": "present" } }"""),
            expected);
    }

    [Fact]
    public async Task Cfg009_deep_conditional_access_preserves_conservative_receiver_and_key_gates()
    {
        var source = DirectReadSource(
            """
            var dynamicKey = System.DateTime.UtcNow.Ticks.ToString();
            _ = configuration?.GetSection("Parent")?.GetSection(dynamicKey)?.Get<ServerOptions>();

            var local = new ConfigurationManager();
            _ = local?.GetSection("Parent")?.GetSection("Chlid")?.Get<ServerOptions>();
            """,
            extraMembers: """
            public void ReadStored(IConfigurationSection stored)
            {
                _ = stored?.GetSection("Parent")?.GetSection("Chlid")?.Get<ServerOptions>();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Parent": { "Child": { "Value": "present" } } }"""));
    }

    [Fact]
    public async Task Cfg009_deep_conditional_read_does_not_taint_later_receiver()
    {
        var source = DirectReadSource("""
            _ = configuration?.GetSection("Parent")?.GetSection("Child")?.Get<ServerOptions>();
            _ = configuration.GetRequiredSection({|#0:"Missing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Parent": { "Child": { "Value": "present" } } }"""),
            expected);
    }

    [Fact]
    public async Task Cfg009_conditional_bind_with_unproven_instance_taints_later_receiver()
    {
        var source = DirectReadSource(
            """
            configuration?.GetSection("Child")?.Bind(BindingTarget);
            _ = configuration.GetRequiredSection("Missing");
            """,
            extraMembers: """
            private ServerOptions BindingTarget => new();
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Child": { "Value": "present" } }"""));
    }

    [Fact]
    public async Task Cfg009_conditional_get_with_unproven_callback_stays_quiet()
    {
        var source = DirectReadSource(
            """
            _ = configuration?.GetSection("Chlid")?.Get<ServerOptions>(options => Seed(configuration));
            """,
            extraMembers: """
            private static void Seed(IConfiguration configuration)
            {
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Child": { "Value": "present" } }"""));
    }

    [Fact]
    public async Task Cfg009_conditional_bind_into_receiver_taints_later_read()
    {
        var source = DirectReadSource("""
            configuration?.GetSection("Child")?.Bind(configuration);
            _ = configuration.GetRequiredSection("Missing");
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Child": { "Value": "present" } }"""));
    }

    [Fact]
    public async Task Cfg009_conditional_bind_into_cast_receiver_taints_later_read()
    {
        var source = DirectReadSource("""
            ((IConfiguration)configuration)?.GetSection("Child")?.Bind(configuration);
            _ = configuration.GetRequiredSection("Missing");
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Child": { "Value": "present" } }"""));
    }

    [Fact]
    public async Task Cfg009_conditional_bind_into_cast_target_taints_later_read()
    {
        var source = DirectReadSource("""
            configuration?.GetSection("Child")?.Bind((IConfiguration)configuration);
            _ = configuration.GetRequiredSection("Missing");
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Child": { "Value": "present" } }"""));
    }

    [Fact]
    public async Task Cfg009_reports_keyed_bind_typo_after_conditional_section_prefix()
    {
        var source = DirectReadSource("""
            configuration?.GetSection("Features")?.Bind({|#0:"Strpie"|}, new ServerOptions());
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Features:Strpie", ". Did you mean \"Features:Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Features": { "Stripe": { "Value": "present" } } }"""),
            expected);
    }

    [Fact]
    public async Task Cfg009_reports_conditional_typo_after_ordinary_section_prefix()
    {
        var source = DirectReadSource("""
            _ = configuration.GetSection("Parent")?.GetSection({|#0:"Chlid"|})?.Get<ServerOptions>();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Parent:Chlid", ". Did you mean \"Parent:Child\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Parent": { "Child": { "Value": "present" } } }"""),
            expected);
    }

    [Fact]
    public async Task Cfg009_wrapped_conditional_nameof_read_does_not_taint_later_receiver()
    {
        var source = DirectReadSource("""
            _ = (configuration?.GetSection(nameof(ServerOptions))?.Get<ServerOptions>())!;
            _ = configuration.GetRequiredSection({|#0:"Missing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "ServerOptions": { "Value": "present" } }"""),
            expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_get_required_section_on_null_forgiving_receiver()
    {
        var source = DirectReadSource("""
            _ = (configuration!).GetRequiredSection({|#0:"Missing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_connection_string_typo()
    {
        var source = DirectReadSource("""
            _ = configuration.GetConnectionString({|#0:"Databsae"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("ConnectionStrings:Databsae", ". Did you mean \"ConnectionStrings:Database\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "ConnectionStrings": {
                "Database": "Server=localhost"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_section_with_composite_key_sibling_suggestion()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Foo:Brr"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Foo:Brr", ". Did you mean \"Foo:Bar\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Foo:Bar": {
                "X": "1"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg009_ignores_concrete_custom_configuration_implementation()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            public void ReadCustom(CustomConfiguration custom)
            {
                _ = custom.GetRequiredSection("Missing");
            }
            """,
            extraTypes: """
            public sealed class CustomConfiguration : IConfiguration
            {
                public string? this[string key] { get => null; set { } }
                public System.Collections.Generic.IEnumerable<IConfigurationSection> GetChildren() => System.Linq.Enumerable.Empty<IConfigurationSection>();
                public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => null!;
                public IConfigurationSection GetSection(string key) => null!;
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_reports_single_diagnostic_for_missing_required_parent_chain()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Missing"|}).GetRequiredSection("Child");
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_single_diagnostic_for_missing_required_parent_before_get()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Missing"|}).GetSection("Sub").Get<ServerOptions>();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_single_diagnostic_for_static_get_after_missing_required_parent()
    {
        var source = DirectReadSource("""
            _ = ConfigurationBinder.Get<ServerOptions>(configuration.GetRequiredSection({|#0:"Strpie"|}));
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_missing_child_in_static_required_section_chain()
    {
        var source = DirectReadSource("""
            _ = ConfigurationExtensions.GetRequiredSection(configuration, "Parent")
                .GetRequiredSection({|#0:"Chlid"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Parent:Chlid", ". Did you mean \"Parent:Child\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Parent": { "Child": { "Value": "present" } } }"""),
            expected);
    }

    [Fact]
    public async Task Cfg009_does_not_report_when_no_appsettings_files()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection("Anything");
            """);

        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Cfg009_does_not_report_existing_section()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection("Stripe");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_does_not_report_section_from_any_appsettings_file()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection("Stripe");
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            new[]
            {
                ("appsettings.json", """
                {
                  "Logging": {
                    "LogLevel": {
                      "Default": "Information"
                    }
                  }
                }
                """),
                ("appsettings.Production.json", """
                {
                  "Stripe": {
                    "ApiKey": "secret"
                  }
                }
                """)
            });
    }

    [Fact]
    public async Task Cfg009_ignores_non_constant_section_keys()
    {
        var source = DirectReadSource("""
            var name = "Missing";
            _ = configuration.GetRequiredSection(name);
            _ = configuration.GetRequiredSection($"{name}Section");
            _ = configuration.GetRequiredSection("");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_does_not_report_nameof_key_matching_existing_section()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection(nameof(ServerOptions));
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "ServerOptions": {
                "Host": "localhost"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg009_ignores_stored_configuration_section_receiver()
    {
        var source = DirectReadSource("""
            IConfigurationSection section = configuration.GetSection("Stripe");
            _ = section.GetRequiredSection("Missing");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_configuration_section_parameter_receiver()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            public void ReadSection(IConfigurationSection section)
            {
                _ = section.GetRequiredSection("Missing");
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_bare_get_section_probe()
    {
        var source = DirectReadSource("""
            var section = configuration.GetSection("Missing");
            if (configuration.GetSection("AlsoMissing").Exists())
            {
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_same_named_method_on_non_configuration_type()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            public void ReadOther(NotConfiguration other)
            {
                _ = other.GetRequiredSection("Missing");
            }
            """,
            extraTypes: """
            public sealed class NotConfiguration
            {
                public NotConfiguration GetRequiredSection(string key) => this;
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_user_defined_get_required_section_extension()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetRequiredSection("Missing");
            """,
            extraTypes: """
            public static class CustomConfigurationExtensions
            {
                public static IConfigurationSection GetRequiredSection(this IConfiguration configuration, string key) => configuration.GetSection(key);
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_same_fully_qualified_name_configuration_extensions_shadow()
    {
        var source = DirectReadSource(
            """
            _ = Microsoft.Extensions.Configuration.ConfigurationExtensions.GetRequiredSection(
                configuration,
                "Strpie");
            """,
            extraTypes: """
            #pragma warning disable CS0436
            namespace Microsoft.Extensions.Configuration
            {
                public static class ConfigurationExtensions
                {
                    public static IConfigurationSection GetRequiredSection(
                        IConfiguration configuration,
                        string key) => configuration.GetSection(key);
                }
            }
            #pragma warning restore CS0436
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_user_defined_required_section_inside_real_binder_call()
    {
        var source = DirectReadSource(
            """
            _ = configuration.CustomGetRequiredSection("Missing").Get<ServerOptions>();
            """,
            extraTypes: """
            public static class CustomConfigurationExtensions
            {
                public static IConfigurationSection CustomGetRequiredSection(
                    this IConfiguration configuration,
                    string key) => configuration.GetSection(key);
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_user_defined_static_get_required_section_inside_real_binder_call()
    {
        var source = DirectReadSource(
            """
            _ = CustomConfigurationExtensions.GetRequiredSection(configuration, "Missing").Get<ServerOptions>();
            """,
            extraTypes: """
            public static class CustomConfigurationExtensions
            {
                public static IConfigurationSection GetRequiredSection(
                    this IConfiguration configuration,
                    string key) => configuration.GetSection(key);
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_does_not_report_section_read_feeding_options_registration()
    {
        var source = """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    services.Configure<ServerOptions>(configuration.GetRequiredSection({|#0:"Missing"|}));
                }
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_does_not_duplicate_cfg001_for_expression_bodied_registration()
    {
        var source = """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services, IConfiguration configuration) =>
                    services.Configure<ServerOptions>(configuration.GetRequiredSection({|#0:"Missing"|}));
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_does_not_duplicate_cfg001_for_nested_registration_section_chain()
    {
        var source = """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    services.Configure<ServerOptions>(
                        configuration.GetRequiredSection("Missing").GetSection({|#0:"Child"|}));
                }
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Missing:Child", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_only_missing_required_parent_before_conditional_child()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Missing"|})?.GetRequiredSection("Child");
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_throwing_chain_when_parent_existence_depends_on_provider_version()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection("Parent").GetRequiredSection({|#0:"Child"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Parent:Child", ".");

        await Verifier.VerifyAnalyzerWithReferencesAsync(
            source,
            ("appsettings.json", """{ "Parent": null }"""),
            Verifier.ConfigurationAbstractionsReferences,
            expected);
    }

    [Fact]
    public async Task Cfg009_reports_net10_empty_object_as_missing_required_section()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Empty"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Empty", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Empty": {} }"""),
            expected);
    }

    [Fact]
    public async Task Cfg009_reports_net10_null_as_missing_required_section()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection({|#0:"Empty"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Empty", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Empty": null }"""),
            expected);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"value\"")]
    [InlineData("{ \"Value\": \"present\" }")]
    public async Task Cfg009_accepts_net10_shapes_that_create_a_runtime_section(string jsonValue)
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection("Present");
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", $$"""{ "Present": {{jsonValue}} }"""));
    }

    [Fact]
    public async Task Cfg001_reports_net10_empty_object_once_without_cfg009_duplicate()
    {
        var source = """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    services.Configure<ServerOptions>(configuration.GetRequiredSection({|#0:"Empty"|}));
                }
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Empty", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Empty": {} }"""),
            expected);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task Cfg001_does_not_cascade_cfg002_when_required_section_is_unavailable(string jsonValue)
    {
        var source = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    services.AddOptions<ServerOptions>()
                        .Bind(configuration.GetRequiredSection({|#0:"Empty"|}))
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
                }
            }

            public sealed class ServerOptions
            {
                [Required]
                public string Host { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Empty", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", $$"""{ "Empty": {{jsonValue}} }"""),
            expected);
    }

    [Fact]
    public async Task Cfg009_does_not_report_section_read_feeding_options_builder_bind()
    {
        var source = """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    services.AddOptions<ServerOptions>().Bind(configuration.GetRequiredSection({|#0:"Missing"|}));
                }
            }

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_ignores_locally_built_configuration()
    {
        var source = DirectReadSource("""
            var config = new ConfigurationBuilder().Build();
            _ = config.GetRequiredSection("Missing");
            _ = new ConfigurationBuilder().Build().GetRequiredSection("AlsoMissing");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_direct_and_local_configuration_manager_instances()
    {
        var source = DirectReadSource("""
            var local = new ConfigurationManager();
            _ = local.GetRequiredSection("Missing");
            _ = new ConfigurationManager().GetRequiredSection("AlsoMissing");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_reports_configuration_manager_parameter_as_host_contract()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            public void ReadManager(ConfigurationManager manager)
            {
                _ = manager.GetRequiredSection({|#0:"Missing"|});
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_tracks_latest_straight_line_configuration_assignment()
    {
        var source = DirectReadSource("""
            IConfiguration host = new ConfigurationBuilder().Build();
            host = configuration;
            _ = host.GetRequiredSection({|#0:"HostMissing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("HostMissing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_after_contract_reassignment_through_explicit_interface_cast()
    {
        var source = DirectReadSource("""
            IConfiguration current = new ConfigurationBuilder().Build();
            current = (IConfiguration)configuration;
            _ = current.GetRequiredSection({|#0:"Missing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_ignores_local_configuration_manager_through_explicit_interface_cast()
    {
        var source = DirectReadSource("""
            IConfiguration local = (IConfiguration)new ConfigurationManager();
            _ = local.GetRequiredSection("Missing");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_custom_configuration_through_explicit_interface_cast()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            public void ReadCustom(CustomConfiguration custom)
            {
                IConfiguration contract = (IConfiguration)custom;
                _ = contract.GetRequiredSection("Missing");
            }
            """,
            extraTypes: """
            public sealed class CustomConfiguration : IConfiguration
            {
                public string? this[string key] { get => null; set { } }
                public System.Collections.Generic.IEnumerable<IConfigurationSection> GetChildren() => System.Linq.Enumerable.Empty<IConfigurationSection>();
                public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => null!;
                public IConfigurationSection GetSection(string key) => null!;
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_downcast_from_contract_to_custom_configuration()
    {
        var source = DirectReadSource(
            """
            var custom = (CustomConfiguration)configuration;
            _ = custom.GetRequiredSection("Missing");
            """,
            extraTypes: """
            public sealed class CustomConfiguration : IConfiguration
            {
                public string? this[string key] { get => null; set { } }
                public System.Collections.Generic.IEnumerable<IConfigurationSection> GetChildren() => System.Linq.Enumerable.Empty<IConfigurationSection>();
                public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => null!;
                public IConfigurationSection GetSection(string key) => null!;
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_stays_quiet_after_mutation_through_interface_cast()
    {
        var source = DirectReadSource("""
            IConfiguration current = (IConfiguration)configuration;
            ((IConfiguration)current)["Missing"] = "value";
            _ = current.GetRequiredSection("Missing");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_stays_quiet_after_mutation_through_concrete_cast()
    {
        var source = DirectReadSource("""
            IConfiguration current = (IConfiguration)configuration;
            ((ConfigurationManager)current)["Missing"] = "value";
            _ = current.GetRequiredSection("Missing");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_uses_latest_local_assignment_when_host_is_replaced_by_local_configuration()
    {
        var source = DirectReadSource("""
            IConfiguration local = configuration;
            local = new ConfigurationBuilder().Build();
            _ = local.GetRequiredSection("LocalMissing");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_reports_after_earlier_read_only_configuration_calls()
    {
        var source = DirectReadSource("""
            configuration.GetSection("Stripe");
            _ = configuration.GetRequiredSection({|#0:"Missing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_after_earlier_read_only_required_section_call()
    {
        var source = DirectReadSource("""
            _ = configuration.GetRequiredSection("Stripe");
            _ = configuration.GetRequiredSection({|#0:"Missing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_after_earlier_read_only_connection_string_call()
    {
        var source = DirectReadSource("""
            _ = configuration.GetConnectionString("Database");
            _ = configuration.GetRequiredSection({|#0:"Missing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Missing", ".");

        await Verifier.VerifyAnalyzerAsync(source, DatabaseConnectionAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_stays_quiet_after_nested_configuration_section_mutation()
    {
        var source = DirectReadSource("""
            configuration.GetSection("Dynamic").Value = "value";
            _ = configuration.GetRequiredSection("Dynamic");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_stays_quiet_after_contract_parameter_is_reassigned_to_local_configuration()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            public void ReadReassigned(IConfiguration configuration)
            {
                configuration = new ConfigurationBuilder().Build();
                _ = configuration.GetRequiredSection("Missing");
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_stays_quiet_after_contract_members_are_reassigned_to_local_configuration()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            private IConfiguration _configuration = null!;
            private IConfiguration Configuration { get; set; } = null!;

            public void ReadReassignedMembers()
            {
                _configuration = new ConfigurationBuilder().Build();
                _ = _configuration.GetRequiredSection("FieldMissing");

                Configuration = new ConfigurationBuilder().Build();
                _ = Configuration.GetRequiredSection("PropertyMissing");
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_tracks_straight_line_configuration_aliases()
    {
        var source = DirectReadSource("""
            var localSource = new ConfigurationBuilder().Build();
            IConfiguration localAlias = localSource;
            _ = localAlias.GetRequiredSection("LocalMissing");

            IConfiguration hostSource = configuration;
            var hostAlias = hostSource;
            _ = hostAlias.GetRequiredSection({|#0:"HostMissing"|});
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("HostMissing", ".");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_stays_quiet_after_conditional_receiver_reassignment()
    {
        var source = DirectReadSource("""
            IConfiguration local = configuration;
            if (System.DateTime.UtcNow.Ticks > 0)
            {
                local = new ConfigurationBuilder().Build();
            }

            _ = local.GetRequiredSection("Missing");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_stays_quiet_after_configuration_manager_mutation()
    {
        var source = DirectReadSource(
            "",
            extraMembers: """
            public void ReadMutatedManager(ConfigurationManager manager)
            {
                manager["Dynamic"] = "value";
                _ = manager.GetRequiredSection("Missing");
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_stays_quiet_after_receiver_escapes_or_is_captured()
    {
        var source = DirectReadSource(
            """
            IConfiguration byValue = configuration;
            Escape(byValue);
            _ = byValue.GetRequiredSection("ByValueMissing");

            IConfiguration byReference = configuration;
            EscapeByReference(ref byReference);
            _ = byReference.GetRequiredSection("ByReferenceMissing");

            IConfiguration captured = configuration;
            System.Action action = () => _ = captured["Dynamic"];
            _ = captured.GetRequiredSection("CapturedMissing");
            """,
            extraMembers: """
            private static void Escape(IConfiguration value) { }
            private static void EscapeByReference(ref IConfiguration value) { }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_connection_string_without_near_match()
    {
        var source = DirectReadSource("""
            _ = configuration.GetConnectionString("Redis");
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "ConnectionStrings": {
                "Database": "Server=localhost"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg009_ignores_connection_string_without_connection_strings_section()
    {
        var source = DirectReadSource("""
            _ = configuration.GetConnectionString("Databsae");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_get_on_configuration_root()
    {
        var source = DirectReadSource("""
            _ = configuration.Get<ServerOptions>();
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_reports_keyed_bind_typo()
    {
        var source = DirectReadSource("""
            configuration.Bind({|#0:"Strpie"|}, new ServerOptions());
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_static_named_keyed_bind_typo()
    {
        var source = DirectReadSource("""
            ConfigurationBinder.Bind(
                instance: new ServerOptions(),
                key: {|#0:"Strpie"|},
                configuration: configuration);
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_keyed_bind_typo_on_known_section_chain()
    {
        var source = DirectReadSource("""
            configuration.GetSection("Features").Bind({|#0:"Strpie"|}, new ServerOptions());
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Features:Strpie", ". Did you mean \"Features:Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Features": {
                "Stripe": {
                  "Name": "value"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg009_ignores_section_bind_when_instance_argument_can_seed_configuration()
    {
        var source = DirectReadSource(
            """
            configuration.GetSection("Strpie").Bind(Seed(configuration));
            """,
            extraMembers: """
            private static ServerOptions Seed(IConfiguration configuration)
            {
                configuration["Strpie:Host"] = "value";
                return new ServerOptions();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_section_bind_with_static_field_instance()
    {
        var source = DirectReadSource(
            """
            configuration.GetSection("Strpie").Bind(Holder.Instance);
            """,
            extraTypes: """
            public static class Holder
            {
                public static readonly ServerOptions Instance = Create();

                private static ServerOptions Create() => new();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_section_bind_with_unprovable_instance_initializer()
    {
        var source = DirectReadSource(
            """
            configuration.GetSection("Strpie").Bind(new EffectfulOptions());
            """,
            extraTypes: """
            public sealed class EffectfulOptions
            {
                public string Host { get; set; } = Create();

                private static string Create() => "value";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_section_bind_with_user_defined_initializer_cast()
    {
        var source = DirectReadSource(
            """
            configuration.GetSection("Strpie").Bind(new CastOptions());
            """,
            extraTypes: """
            public sealed class CastOptions
            {
                public Converted Value { get; set; } = (Converted)1;
            }

            public readonly struct Converted
            {
                public static implicit operator Converted(int value) => new();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_section_bind_with_user_defined_initializer_after_unary_expression()
    {
        var source = DirectReadSource(
            """
            configuration.GetSection("Strpie").Bind(new UnaryOptions());
            """,
            extraTypes: """
            public sealed class UnaryOptions
            {
                public Converted Value { get; set; } = -1;
            }

            public readonly struct Converted
            {
                public static implicit operator Converted(int value) => new();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_section_bind_with_escaped_nameof_initializer_call()
    {
        var source = DirectReadSource(
            """
            configuration.GetSection("Strpie").Bind(new EscapedNameofOptions());
            """,
            extraTypes: """
            public sealed class EscapedNameofOptions
            {
                public string Value { get; set; } = @nameof(Seed());

                private static string @nameof(string value) => value;

                private static string Seed() => "value";
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_reports_section_bind_with_safe_instance_field()
    {
        var source = DirectReadSource(
            """
            configuration.GetSection({|#0:"Strpie"|}).Bind(_instance);
            """,
            extraMembers: """
            private readonly ServerOptions _instance = new();
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_section_bind_with_safe_parameter_instance()
    {
        var source = DirectReadSource("""
            configuration.GetSection({|#0:"Strpie"|}).Bind(configuration);
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_reports_section_bind_with_safe_implicit_initializers()
    {
        var source = DirectReadSource(
            """
            configuration.GetSection({|#0:"Strpie"|}).Bind(new SafeInitializerOptions());
            """,
            extraTypes: """
            public sealed class SafeInitializerOptions
            {
                public string? NullValue { get; set; } = null;
                public int? NullableValue { get; set; } = null;
                public object RuntimeType { get; set; } = typeof(string);
                public System.Type TypeValue { get; set; } = typeof(string);
                public string Name { get; set; } = nameof(SafeInitializerOptions);
                public int Parenthesized { get; set; } = (1);
                public int Negative { get; set; } = -1;
                public string Suppressed { get; set; } = null!;
                public int DefaultValue { get; set; } = default;
                public int Field = 1;
                public event System.Action? Changed;
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_proves_only_inline_converted_binder_options_callbacks()
    {
        var safeSource = DirectReadSource("""
            configuration.GetSection({|#0:"Strpie"|}).Bind(
                new ServerOptions(),
                (System.Action<BinderOptions>)(options => options.BindNonPublicProperties = true));
            """);
        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");
        await Verifier.VerifyAnalyzerAsync(safeSource, StripeAppSettings, expected);

        var escapedSource = DirectReadSource("""
            System.Action<BinderOptions> configure = options => options.BindNonPublicProperties = true;
            configuration.GetSection("Strpie").Bind(new ServerOptions(), configure);
            """);
        await Verifier.VerifyAnalyzerAsync(escapedSource, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_section_bind_when_binder_options_callback_can_seed_configuration()
    {
        var source = DirectReadSource("""
            configuration.GetSection("Strpie").Bind(
                new ServerOptions(),
                options => configuration["Strpie:Host"] = "value");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_reports_section_bind_with_safe_binder_options_callback()
    {
        var source = DirectReadSource("""
            configuration.GetSection({|#0:"Strpie"|}).Bind(
                new ServerOptions(),
                options => options.BindNonPublicProperties = true);
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationKeyNotFound)
            .WithLocation(0)
            .WithArguments("Strpie", ". Did you mean \"Stripe\"?");

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings, expected);
    }

    [Fact]
    public async Task Cfg009_ignores_keyed_bind_when_instance_argument_can_seed_configuration()
    {
        var source = DirectReadSource(
            """
            configuration.Bind("Strpie", Seed(configuration));
            """,
            extraMembers: """
            private static ServerOptions Seed(IConfiguration configuration)
            {
                configuration["Strpie:Host"] = "value";
                return new ServerOptions();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_static_named_keyed_bind_when_instance_argument_can_seed_configuration()
    {
        var source = DirectReadSource(
            """
            ConfigurationBinder.Bind(
                instance: Seed(configuration),
                key: "Strpie",
                configuration: configuration);
            """,
            extraMembers: """
            private static ServerOptions Seed(IConfiguration configuration)
            {
                configuration["Strpie:Host"] = "value";
                return new ServerOptions();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_keyed_bind_when_instance_field_receiver_can_seed_configuration()
    {
        var source = DirectReadSource(
            """
            configuration.Bind("Strpie", Seed(configuration).InstanceField);
            """,
            extraMembers: """
            private static Holder Seed(IConfiguration configuration)
            {
                configuration["Strpie:Host"] = "value";
                return new Holder();
            }
            """,
            extraTypes: """
            public sealed class Holder
            {
                public ServerOptions InstanceField = new();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_same_fully_qualified_name_keyed_bind_shadow()
    {
        var source = DirectReadSource(
            """
            Microsoft.Extensions.Configuration.ConfigurationBinder.Bind(
                configuration,
                "Strpie",
                new ServerOptions());
            """,
            extraTypes: """
            #pragma warning disable CS0436
            namespace Microsoft.Extensions.Configuration
            {
                public static class ConfigurationBinder
                {
                    public static void Bind(
                        IConfiguration configuration,
                        string key,
                        object instance) { }
                }
            }
            #pragma warning restore CS0436
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_bind_with_section_key_overload()
    {
        var source = DirectReadSource("""
            configuration.Bind("Missing", new ServerOptions());
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg008_reports_get_value_conversion_failure()
    {
        var source = DirectReadSource("""
            _ = configuration.GetValue<int>("Server:Port");
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 13, 3, 21)
            .WithArguments("Server:Port", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_static_named_get_value_default_overload_conversion_failure()
    {
        var source = DirectReadSource("""
            _ = ConfigurationBinder.GetValue<int>(
                configuration: configuration,
                key: "Server:Port",
                defaultValue: 8080);
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 13, 3, 21)
            .WithArguments("Server:Port", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_ignores_get_value_when_default_argument_can_mutate_configuration()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetValue<int>(
                "Server:Port",
                SetValidValue(configuration));
            """,
            extraMembers: """
            private static int SetValidValue(IConfiguration configuration)
            {
                configuration["Server:Port"] = "8080";
                return 0;
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_ignores_get_value_on_locally_initialized_interface_members()
    {
        var source = """
            using Microsoft.Extensions.Configuration;

            public sealed class Reader
            {
                private readonly IConfiguration _configuration = new ConfigurationBuilder().Build();
                private IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();

                public void Read()
                {
                    _ = _configuration.GetValue<int>("Server:Port");
                    _ = Configuration.GetValue<int>("Server:Port");
                }
            }
            """;

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_ignores_get_value_on_constructor_initialized_interface_member()
    {
        var source = """
            using Microsoft.Extensions.Configuration;

            public sealed class Reader
            {
                private readonly IConfiguration _configuration;

                public Reader()
                {
                    _configuration = new ConfigurationBuilder().Build();
                }

                public void Read()
                {
                    _ = _configuration.GetValue<int>("Server:Port");
                }
            }
            """;

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_ignores_get_value_on_custom_accessor_member()
    {
        var source = """
            using Microsoft.Extensions.Configuration;

            public sealed class Reader
            {
                private IConfiguration Configuration
                {
                    get
                    {
                        return new ConfigurationBuilder().Build();
                    }
                }

                public void Read()
                {
                    _ = Configuration.GetValue<int>("Server:Port");
                }
            }
            """;

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_reports_get_value_conversion_failure_on_null_forgiving_injected_field()
    {
        var source = """
            using Microsoft.Extensions.Configuration;

            public sealed class Reader
            {
                private readonly IConfiguration _configuration = null!;

                public void Read()
                {
                    _ = _configuration.GetValue<int>("Server:Port");
                }
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 13, 3, 21)
            .WithArguments("Server:Port", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_ignores_null_forgiving_field_overwritten_with_local_configuration_in_constructor()
    {
        var source = """
            using Microsoft.Extensions.Configuration;

            public sealed class Reader
            {
                private readonly IConfiguration _configuration = null!;

                public Reader()
                {
                    _configuration = new ConfigurationBuilder().Build();
                }

                public void Read()
                {
                    _ = _configuration.GetValue<int>("Server:Port");
                }
            }
            """;

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_reports_for_null_forgiving_field_when_constructor_only_assigns_shadowing_parameter()
    {
        var source = """
            using Microsoft.Extensions.Configuration;

            public sealed class Reader
            {
                private readonly IConfiguration _configuration = null!;

                public Reader(IConfiguration _configuration)
                {
                    _configuration = new ConfigurationBuilder().Build();
                }

                public void Read()
                {
                    _ = _configuration.GetValue<int>("Server:Port");
                }
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 13, 3, 21)
            .WithArguments("Server:Port", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public void Cfg008_rejects_get_value_from_unsigned_replacement_binder_assembly()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("""
            namespace Microsoft.Extensions.Configuration;

            public interface IConfiguration { }

            public static class ConfigurationBinder
            {
                public static T? GetValue<T>(IConfiguration configuration, string key) => default;
            }
            """);
        var compilation = CSharpCompilation.Create(
            "Microsoft.Extensions.Configuration.Binder",
            [syntaxTree],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var method = compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.Configuration.ConfigurationBinder")!
            .GetMembers("GetValue")
            .OfType<IMethodSymbol>()
            .Single();
        var identityGate = typeof(ConfigContrabandAnalyzer).GetMethod(
            "IsFrameworkGenericGetValueMethod",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var accepted = (bool)identityGate.Invoke(null, [method, method])!;

        Assert.False(accepted);
    }

    [Fact]
    public async Task Cfg008_ignores_cross_file_locally_initialized_interface_member_without_ad0001()
    {
        var sources = new[]
        {
            ("Reader.cs", """
                using Microsoft.Extensions.Configuration;

                public sealed partial class Reader
                {
                    public void Read()
                    {
                        _ = Configuration.GetValue<int>("Server:Port");
                    }
                }
                """),
            ("Reader.Configuration.cs", """
                using Microsoft.Extensions.Configuration;

                public sealed partial class Reader
                {
                    private IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();
                }
                """)
        };

        await Verifier.VerifyAnalyzerAsync(
            sources,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_reports_get_value_conversion_failure_on_known_section_chain()
    {
        var source = DirectReadSource("""
            _ = configuration.GetSection("Server").GetValue<int>("Port");
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 13, 3, 21)
            .WithArguments("Server:Port", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_get_value_conversion_failure_once_for_repeated_read()
    {
        var source = DirectReadSource("""
            _ = configuration.GetValue<int>("Server:Port");
            _ = configuration.GetValue<int>("Server:Port");
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 13, 3, 21)
            .WithArguments("Server:Port", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_once_across_options_binding_and_direct_get_value_read()
    {
        var source = """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    services.AddOptions<ServerOptions>().BindConfiguration("Server");
                    _ = configuration.GetValue<int>("Server:Port");
                }
            }

            public sealed class ServerOptions
            {
                public int Port { get; set; }
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 13, 3, 21)
            .WithArguments("Server:Port", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_ignores_same_fully_qualified_name_get_value_shadow()
    {
        var source = DirectReadSource(
            """
            _ = Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue<int>(
                configuration,
                "Server:Port");
            """,
            extraTypes: """
            #pragma warning disable CS0436
            namespace Microsoft.Extensions.Configuration
            {
                public static class ConfigurationBinder
                {
                    public static int GetValue<T>(this IConfiguration configuration, string key) => 0;
                }
            }
            #pragma warning restore CS0436
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_ignores_get_value_on_stored_configuration_section()
    {
        var source = DirectReadSource("""
            IConfigurationSection section = configuration.GetSection("Server");
            _ = section.GetValue<int>("Port");
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Port": "eighty"
            }
            """));
    }

    [Fact]
    public async Task Cfg008_ignores_safe_or_unprovable_get_value_reads()
    {
        var source = DirectReadSource("""
            _ = configuration.GetValue<int>("Server:Port");
            _ = configuration.GetValue<int>("Missing:Port");
            _ = configuration.GetValue<int>("Server:NullPort");
            _ = configuration.GetValue<ServerOptions>("Server");
            var key = "Server:BadPort";
            _ = configuration.GetValue<int>(key);
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": 8080,
                "NullPort": null,
                "BadPort": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_ignores_get_value_on_local_or_mutated_configuration()
    {
        var source = DirectReadSource("""
            var local = new ConfigurationBuilder().Build();
            _ = local.GetValue<int>("Server:Port");

            configuration["Server:Port"] = "8080";
            _ = configuration.GetValue<int>("Server:Port");
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg009_ignores_get_value_reads()
    {
        var source = DirectReadSource("""
            _ = configuration.GetValue<int>("Missing:Port");
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    [Fact]
    public async Task Cfg009_ignores_custom_get_section_overload_inside_real_binder_call()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetSection("Strpie", optional: true).Get<ServerOptions>();
            """,
            extraTypes: """
            public static class CustomConfigurationExtensions
            {
                public static IConfigurationSection GetSection(
                    this IConfiguration configuration,
                    string key,
                    bool optional) => configuration.GetSection(key);
            }
            """);

        await Verifier.VerifyAnalyzerAsync(source, StripeAppSettings);
    }

    private static (string filename, string content) StripeAppSettings =>
        ("appsettings.json", """
        {
          "Stripe": {
            "ApiKey": "secret"
          }
        }
        """);

    private static (string filename, string content) DatabaseConnectionAppSettings =>
        ("appsettings.json", """
        {
          "ConnectionStrings": {
            "Database": "Server=localhost"
          }
        }
        """);

    private static string DirectReadSource(string body, string extraMembers = "", string extraTypes = "")
    {
        return $$"""
            using Microsoft.Extensions.Configuration;

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";
                public int Port { get; set; }
            }

            public sealed class Reader
            {
                public void Read(IConfiguration configuration)
                {
                    {{body}}
                }

                {{extraMembers}}
            }

            {{extraTypes}}
            """;
    }

    private static string TypeDisplay(string type) => type;

    private static string OptionsSource(
        string registration,
        string extraUsings = "",
        string extraMembers = "",
        string? optionsTypes = null)
    {
        optionsTypes ??= """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";

                public string WebhookSecret { get; set; } = "";
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

                {{extraMembers}}
            }

            {{optionsTypes}}
            """;
    }
}
