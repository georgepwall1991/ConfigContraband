using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed class ConfigContrabandCodeRefactoringTests
{
    [Fact]
    public async Task Rename_refactoring_renames_unknown_key_in_json()
    {
        await Verifier.VerifyRefactoringAsync(
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    [|.BindConfiguration("Stripe")|]
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """),
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    .BindConfiguration("Stripe")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """),
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "k",
                "WebookSecret": "s"
              }
            }
            """),
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "k",
                "WebhookSecret": "s"
              }
            }
            """));
    }

    [Fact]
    public async Task Rename_refactoring_works_from_the_section_literal()
    {
        // A caret inside the bound-section literal resolves the enclosing invocation.
        await Verifier.VerifyRefactoringAsync(
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    .BindConfiguration([|"Stripe"|])
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """),
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    .BindConfiguration("Stripe")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """),
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": \"k\", \"WebookSecret\": \"s\" } }"),
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": \"k\", \"WebhookSecret\": \"s\" } }"));
    }

    [Fact]
    public async Task Rename_refactoring_quiet_when_no_invocation_at_span()
    {
        await Verifier.VerifyRefactoringAsync(
            OptionsSource("""
                var enabled = [|true|];
                services.AddOptions<StripeOptions>()
                    .BindConfiguration("Stripe")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """),
            OptionsSource("""
                var enabled = true;
                services.AddOptions<StripeOptions>()
                    .BindConfiguration("Stripe")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """),
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": \"k\", \"WebookSecret\": \"s\" } }"),
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": \"k\", \"WebookSecret\": \"s\" } }"));
    }

    [Fact]
    public async Task Rename_refactoring_offers_one_action_per_containing_file()
    {
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                [|.BindConfiguration("Stripe")|]
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeRefactoringTest<
            ConfigContrabandCodeRefactoringProvider,
            Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Verifier.OptionsReferences
        };
        test.TestState.AdditionalFiles.Add(
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": \"k\", \"WebookSecret\": \"s\" } }"));
        test.TestState.AdditionalFiles.Add(
            ("appsettings.Development.json", "{ \"Stripe\": { \"WebookSecret\": \"s\" } }"));
        // The harness applies the first offered action; the second file stays for a follow-up.
        test.FixedState.AdditionalFiles.Add(
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": \"k\", \"WebhookSecret\": \"s\" } }"));
        test.FixedState.AdditionalFiles.Add(
            ("appsettings.Development.json", "{ \"Stripe\": { \"WebookSecret\": \"s\" } }"));
        await test.RunAsync();
    }

    [Fact]
    public async Task Rename_refactoring_quiet_when_keys_all_bindable()
    {
        await Verifier.VerifyRefactoringAsync(
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    [|.BindConfiguration("Stripe")|]
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """),
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    .BindConfiguration("Stripe")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """),
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": \"k\", \"WebhookSecret\": \"s\" } }"),
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": \"k\", \"WebhookSecret\": \"s\" } }"));
    }

    [Fact]
    public async Task Rename_refactoring_quiet_without_confident_suggestion()
    {
        await Verifier.VerifyRefactoringAsync(
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    [|.BindConfiguration("Stripe")|]
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """),
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    .BindConfiguration("Stripe")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """),
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": \"k\", \"CompletelyDifferent\": \"s\" } }"),
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": \"k\", \"CompletelyDifferent\": \"s\" } }"));
    }

    [Fact]
    public async Task Rename_refactoring_quiet_on_non_registration_invocation()
    {
        await Verifier.VerifyRefactoringAsync(
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    .BindConfiguration("Stripe")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                System.Console.WriteLine([|1|]);
                """),
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    .BindConfiguration("Stripe")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                System.Console.WriteLine(1);
                """),
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": \"k\", \"WebookSecret\": \"s\" } }"),
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": \"k\", \"WebookSecret\": \"s\" } }"));
    }

    [Fact]
    public async Task Rename_refactoring_quiet_for_bindless_registration()
    {
        await Verifier.VerifyRefactoringAsync(
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    [|.ValidateOnStart()|];
                """),
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    .ValidateOnStart();
                """),
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": \"k\", \"WebookSecret\": \"s\" } }"),
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": \"k\", \"WebookSecret\": \"s\" } }"));
    }

    [Fact]
    public async Task Rename_refactoring_quiet_when_options_type_has_no_bindable_properties()
    {
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<EmptyOptions>()
                        [|.BindConfiguration("Stripe")|]
                        .ValidateOnStart();
                }
            }

            public sealed class EmptyOptions
            {
            }
            """;

        await Verifier.VerifyRefactoringAsync(
            source,
            source.Replace("[|", "").Replace("|]", ""),
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": \"k\", \"WebookSecret\": \"s\" } }"),
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": \"k\", \"WebookSecret\": \"s\" } }"));
    }

    [Fact]
    public async Task Rename_refactoring_quiet_when_file_fails_to_load()
    {
        await Verifier.VerifyRefactoringAsync(
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    [|.BindConfiguration("Stripe")|]
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """),
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    .BindConfiguration("Stripe")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """),
            ("appsettings.json", "{ \"Stripe\": {"),
            ("appsettings.json", "{ \"Stripe\": {"));
    }

    [Fact]
    public async Task Rename_refactoring_quiet_when_section_is_scalar()
    {
        await Verifier.VerifyRefactoringAsync(
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    [|.BindConfiguration("Stripe")|]
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """),
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    .BindConfiguration("Stripe")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """),
            ("appsettings.json", "{ \"Stripe\": \"conn\" }"),
            ("appsettings.json", "{ \"Stripe\": \"conn\" }"));
    }

    [Fact]
    public async Task Rename_refactoring_quiet_when_section_is_projected_from_flat_keys()
    {
        await Verifier.VerifyRefactoringAsync(
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    [|.BindConfiguration("Stripe")|]
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """),
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    .BindConfiguration("Stripe")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """),
            ("appsettings.json", "{ \"Stripe:WebookSecret\": \"s\" }"),
            ("appsettings.json", "{ \"Stripe:WebookSecret\": \"s\" }"));
    }

    [Fact]
    public async Task Rename_refactoring_quiet_for_constructor_bound_alias_key()
    {
        var source = $$"""
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        [|.BindConfiguration("Stripe")|]
                        .ValidateOnStart();
                }
            }

            public sealed class StripeOptions
            {
                public StripeOptions(string apiKey, string webhookSecret)
                {
                    ApiKey = apiKey;
                    WebhookSecret = webhookSecret;
                }

                [ConfigurationKeyName("api_key")]
                public string ApiKey { get; set; }

                public string WebhookSecret { get; set; }
            }
            """;

        // With the canonical "ApiKey" present, "api_key" resolves through the settable
        // constructor-bound alias and is not an unknown key — no rename is offered.
        await Verifier.VerifyRefactoringAsync(
            source,
            source.Replace("[|", "").Replace("|]", ""),
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": \"a\", \"api_key\": \"b\", \"WebhookSecret\": \"s\" } }"),
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": \"a\", \"api_key\": \"b\", \"WebhookSecret\": \"s\" } }"));
    }

    private static string OptionsSource(string registration)
    {
        return $$"""
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {{registration}}
                }
            }

            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";

                public string WebhookSecret { get; set; } = "";
            }
            """;
    }
}
