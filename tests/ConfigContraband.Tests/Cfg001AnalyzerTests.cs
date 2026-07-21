using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
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
}
