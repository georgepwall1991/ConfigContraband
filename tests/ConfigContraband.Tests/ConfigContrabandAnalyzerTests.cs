using ConfigContraband.Tests.Infrastructure;

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
