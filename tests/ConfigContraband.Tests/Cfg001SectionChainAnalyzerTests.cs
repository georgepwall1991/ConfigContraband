using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{
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
    public async Task Cfg001_reports_get_section_chained_off_origin_visible_stored_section_variable()
    {
        // The stored section's own path is statically visible ("Features"), so the chained
        // literal resolves to the provable full path Features:Strpie instead of staying quiet.
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            var section = configuration.GetSection("Features");
            services.AddOptions<StripeOptions>()
                .Bind(section.GetSection({|#0:"Strpie"|}))
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
    public async Task Cfg001_reports_conditional_access_get_section_chained_off_origin_visible_stored_section_variable()
    {
        var source = OptionsSource("""
            #nullable enable
            IConfiguration configuration = null!;
            IConfigurationSection? section = configuration.GetSection("Features");
            services.AddOptions<StripeOptions>()
                .Bind(section?.GetSection({|#0:"Strpie"|}))
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
    public async Task Cfg001_reports_get_section_chained_off_stored_section_alias()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            var section = configuration.GetSection("Features");
            var alias = section;
            services.AddOptions<StripeOptions>()
                .Bind(alias.GetSection({|#0:"Strpie"|}))
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
    public async Task Cfg001_reports_get_section_chained_off_reassigned_stored_section()
    {
        // The last same-block simple reassignment before the bind supplies the origin.
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            var section = configuration.GetSection("Logging");
            section = configuration.GetSection("Features");
            services.AddOptions<StripeOptions>()
                .Bind(section.GetSection({|#0:"Strpie"|}))
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
    public async Task Cfg001_reports_get_section_chained_off_stored_required_section_origin()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            var section = configuration.GetRequiredSection("Features");
            services.AddOptions<StripeOptions>()
                .Bind(section.GetSection({|#0:"Strpie"|}))
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
    public async Task Cfg001_reports_get_required_section_chained_off_origin_visible_stored_section()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            var section = configuration.GetSection("Features");
            services.AddOptions<StripeOptions>()
                .Bind(section.GetRequiredSection({|#0:"Strpie"|}))
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
    public async Task Cfg001_reports_get_section_chained_off_origin_visible_stored_section_configure_registration()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            var section = configuration.GetSection("Features");
            services.Configure<StripeOptions>(section.GetSection({|#0:"Strpie"|}));
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
    public async Task Cfg001_ignores_get_section_chained_off_section_parameter_receiver()
    {
        // A parameter's origin lives at the call site and is not statically visible.
        var source = OptionsSource("""
            Register(services, null!);
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", extraMembers: """
            private static void Register(IServiceCollection services, IConfigurationSection section)
            {
                services.AddOptions<StripeOptions>()
                    .Bind(section.GetSection("Strpie"))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
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
    public async Task Cfg001_ignores_get_section_chained_off_conditionally_reassigned_stored_section()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            var section = configuration.GetSection("Features");
            if (configuration.GetValue<bool>("UseOther"))
            {
                section = configuration.GetSection("Other");
            }

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
    public async Task Cfg001_ignores_get_section_chained_off_mutated_stored_section()
    {
        // The indexer write can create the key at runtime, so the missing-section
        // diagnostic would be a false positive.
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            var section = configuration.GetSection("Features");
            section["Strpie"] = "x";
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
    public async Task Cfg001_ignores_get_section_chained_off_escaped_stored_section()
    {
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            var section = configuration.GetSection("Features");
            Use(section);
            services.AddOptions<StripeOptions>()
                .Bind(section.GetSection("Strpie"))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using Microsoft.Extensions.Configuration;\n", extraMembers: """
            private static void Use(IConfigurationSection section)
            {
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
    public async Task Cfg001_ignores_get_section_chained_off_method_return_stored_section()
    {
        var source = OptionsSource("""
            var section = GetFeaturesSection();
            services.AddOptions<StripeOptions>()
                .Bind(section.GetSection("Strpie"))
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
    public async Task Cfg001_ignores_get_section_chained_off_cross_block_stored_section()
    {
        // The declaration lives in the outer block while the bind executes in a nested
        // block, so the same-block origin proof stays conservative.
        var source = OptionsSource("""
            IConfiguration configuration = null!;
            var section = configuration.GetSection("Features");
            if (configuration.GetValue<bool>("Enabled"))
            {
                services.AddOptions<StripeOptions>()
                    .Bind(section.GetSection("Strpie"))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
            }
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
}
