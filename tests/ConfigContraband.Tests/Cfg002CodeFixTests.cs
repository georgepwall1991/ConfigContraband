using System.Collections.Immutable;
using ConfigContraband.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandCodeFixTests
{
    private const string StripeWithSecret = """
        public sealed class StripeOptions
        {
            [Required]
            public string ApiKey { get; set; } = "";

            public string WebhookSecret { get; set; } = "";
        }
        """;

    private const string BindStripeRegistration = """
        services.AddOptions<StripeOptions>()
            .BindConfiguration({|#0:"Stripe"|})
            .ValidateDataAnnotations()
            .ValidateOnStart();
        """;

    private const string BindStripeFixed = """
        services.AddOptions<StripeOptions>()
            .BindConfiguration("Stripe")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        """;

    [Fact]
    public async Task Cfg002_fix_inserts_required_key_into_section()
    {
        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Stripe");

        await Verifier.VerifyCodeFixAsync(
            OptionsSource(BindStripeRegistration, optionsTypes: StripeWithSecret),
            OptionsSource(BindStripeFixed, optionsTypes: StripeWithSecret),
            ("appsettings.json", """
            {
              "Stripe": {
                "WebhookSecret": "secret"
              }
            }
            """),
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": null,
                "WebhookSecret": "secret"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_fix_expands_empty_section()
    {
        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Stripe");

        await Verifier.VerifyCodeFixAsync(
            OptionsSource(BindStripeRegistration, optionsTypes: StripeWithSecret),
            OptionsSource(BindStripeFixed, optionsTypes: StripeWithSecret),
            ("appsettings.json", """
            {
              "Stripe": {
              }
            }
            """),
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": null
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_fix_expands_inline_empty_section()
    {
        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Stripe");

        await Verifier.VerifyCodeFixAsync(
            OptionsSource(BindStripeRegistration, optionsTypes: StripeWithSecret),
            OptionsSource(BindStripeFixed, optionsTypes: StripeWithSecret),
            ("appsettings.json", "{ \"Stripe\": { } }"),
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": null } }"),
            expected);
    }

    [Fact]
    public async Task Cfg002_fix_stays_inline_for_single_line_section()
    {
        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Stripe");

        await Verifier.VerifyCodeFixAsync(
            OptionsSource(BindStripeRegistration, optionsTypes: StripeWithSecret),
            OptionsSource(BindStripeFixed, optionsTypes: StripeWithSecret),
            ("appsettings.json", "{ \"Stripe\": { \"WebhookSecret\": \"s\" } }"),
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": null, \"WebhookSecret\": \"s\" } }"),
            expected);
    }

    [Fact]
    public async Task Cfg002_fix_offers_edit_per_containing_file()
    {
        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Stripe");

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
            ConfigContrabandAnalyzer,
            ConfigContrabandCodeFixProvider,
            Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = OptionsSource(BindStripeRegistration, optionsTypes: StripeWithSecret),
            FixedCode = OptionsSource(BindStripeFixed, optionsTypes: StripeWithSecret),
            ReferenceAssemblies = Verifier.OptionsReferences
        };
        test.TestState.AdditionalFiles.Add(
            ("appsettings.json", "{ \"Stripe\": { \"WebhookSecret\": \"s\" } }"));
        test.TestState.AdditionalFiles.Add(
            ("appsettings.Development.json", "{ \"Stripe\": { } }"));
        test.ExpectedDiagnostics.Add(expected);
        test.FixedState.AdditionalFiles.Add(
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": null, \"WebhookSecret\": \"s\" } }"));
        // The merged configuration resolves once the key lands in any file, so the
        // appsettings.Development.json alternative stays untouched.
        test.FixedState.AdditionalFiles.Add(
            ("appsettings.Development.json", "{ \"Stripe\": { } }"));
        await test.RunAsync();
    }

    [Fact]
    public async Task Cfg002_fix_inserts_into_nested_section()
    {
        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Outer:Inner");

        await Verifier.VerifyCodeFixAsync(
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    .BindConfiguration({|#0:"Outer:Inner"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """, optionsTypes: StripeWithSecret),
            OptionsSource("""
                services.AddOptions<StripeOptions>()
                    .BindConfiguration("Outer:Inner")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """, optionsTypes: StripeWithSecret),
            ("appsettings.json", """
            {
              "Outer": {
                "Inner": {
                  "WebhookSecret": "s"
                }
              }
            }
            """),
            ("appsettings.json", """
            {
              "Outer": {
                "Inner": {
                  "ApiKey": null,
                  "WebhookSecret": "s"
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_fix_respects_tab_indentation()
    {
        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Stripe");

        await Verifier.VerifyCodeFixAsync(
            OptionsSource(BindStripeRegistration, optionsTypes: StripeWithSecret),
            OptionsSource(BindStripeFixed, optionsTypes: StripeWithSecret),
            ("appsettings.json", "{\n\t\"Stripe\": {\n\t\t\"WebhookSecret\": \"s\"\n\t}\n}"),
            ("appsettings.json", "{\n\t\"Stripe\": {\n\t\t\"ApiKey\": null,\n\t\t\"WebhookSecret\": \"s\"\n\t}\n}"),
            expected);
    }

    [Fact]
    public async Task Cfg002_fix_defaults_to_two_space_indent_when_file_has_none()
    {
        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Stripe");

        // The only member shares the '{' line, so the indent falls back to a two-space unit.
        await Verifier.VerifyCodeFixAsync(
            OptionsSource(BindStripeRegistration, optionsTypes: StripeWithSecret),
            OptionsSource(BindStripeFixed, optionsTypes: StripeWithSecret),
            ("appsettings.json", "{ \"Stripe\": { \"WebhookSecret\": \"s\",\n} }"),
            ("appsettings.json", "{ \"Stripe\": {\n  \"ApiKey\": null, \"WebhookSecret\": \"s\",\n} }"),
            expected);
    }

    [Fact]
    public async Task Cfg002_fix_preserves_crlf_line_endings()
    {
        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Stripe");

        await Verifier.VerifyCodeFixAsync(
            OptionsSource(BindStripeRegistration, optionsTypes: StripeWithSecret),
            OptionsSource(BindStripeFixed, optionsTypes: StripeWithSecret),
            ("appsettings.json", "{\r\n  \"Stripe\": {\r\n    \"WebhookSecret\": \"s\"\r\n  }\r\n}"),
            ("appsettings.json", "{\r\n  \"Stripe\": {\r\n    \"ApiKey\": null,\r\n    \"WebhookSecret\": \"s\"\r\n  }\r\n}"),
            expected);
    }

    [Fact]
    public async Task Cfg002_fix_keeps_trailing_comma_member_valid()
    {
        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Stripe");

        await Verifier.VerifyCodeFixAsync(
            OptionsSource(BindStripeRegistration, optionsTypes: StripeWithSecret),
            OptionsSource(BindStripeFixed, optionsTypes: StripeWithSecret),
            ("appsettings.json", """
            {
              "Stripe": {
                "WebhookSecret": "s",
              }
            }
            """),
            ("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": null,
                "WebhookSecret": "s",
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg002_fix_all_adds_each_missing_key()
    {
        const string twoRequired = """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";

                [Required]
                public string Secret { get; set; } = "";

                public string Existing { get; set; } = "";
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
            ConfigContrabandAnalyzer,
            ConfigContrabandCodeFixProvider,
            Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = OptionsSource(BindStripeRegistration, optionsTypes: twoRequired),
            FixedCode = OptionsSource(BindStripeFixed, optionsTypes: twoRequired),
            ReferenceAssemblies = Verifier.OptionsReferences
        };
        test.TestState.AdditionalFiles.Add(("appsettings.json", "{ \"Stripe\": { \"Existing\": \"s\" } }"));
        test.ExpectedDiagnostics.Add(
            Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
                .WithLocation(0)
                .WithArguments("ApiKey", "Stripe"));
        test.ExpectedDiagnostics.Add(
            Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
                .WithLocation(0)
                .WithArguments("Secret", "Stripe"));
        // Iterative application inserts the first-selected diagnostic's key, then prepends
        // the second; the batched Fix All concatenates inserts in diagnostic order.
        test.FixedState.AdditionalFiles.Add(
            ("appsettings.json", "{ \"Stripe\": { \"Secret\": null, \"ApiKey\": null, \"Existing\": \"s\" } }"));
        test.BatchFixedState.AdditionalFiles.Add(
            ("appsettings.json", "{ \"Stripe\": { \"ApiKey\": null, \"Secret\": null, \"Existing\": \"s\" } }"));
        await test.RunAsync();
    }

    [Fact]
    public async Task Cfg002_fix_all_defers_conflicting_inserts_into_empty_section()
    {
        // Both keys want to replace the same "{ }" interior, which cannot merge — the second
        // insert lands on the next Fix All iteration.
        const string twoRequired = """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";

                [Required]
                public string Secret { get; set; } = "";
            }
            """;

        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
            ConfigContrabandAnalyzer,
            ConfigContrabandCodeFixProvider,
            Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = OptionsSource(BindStripeRegistration, optionsTypes: twoRequired),
            FixedCode = OptionsSource(BindStripeFixed, optionsTypes: twoRequired),
            ReferenceAssemblies = Verifier.OptionsReferences,
            NumberOfIncrementalIterations = 2,
            NumberOfFixAllInDocumentIterations = 2,
            NumberOfFixAllInProjectIterations = 2,
            NumberOfFixAllIterations = 2
        };
        test.TestState.AdditionalFiles.Add(("appsettings.json", "{ \"Stripe\": { } }"));
        test.ExpectedDiagnostics.Add(
            Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
                .WithLocation(0)
                .WithArguments("ApiKey", "Stripe"));
        test.ExpectedDiagnostics.Add(
            Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
                .WithLocation(0)
                .WithArguments("Secret", "Stripe"));
        // Each successive insert prepends, so the deferred key lands before the first key.
        test.FixedState.AdditionalFiles.Add(
            ("appsettings.json", "{ \"Stripe\": { \"Secret\": null, \"ApiKey\": null } }"));
        test.BatchFixedState.AdditionalFiles.Add(
            ("appsettings.json", "{ \"Stripe\": { \"Secret\": null, \"ApiKey\": null } }"));
        await test.RunAsync();
    }

    [Fact]
    public async Task Cfg001_fix_all_ignores_json_anchored_diagnostics()
    {
        // The unknown-key diagnostic lives in the additional file (no document), so Fix All
        // must skip it while still applying the C#-anchored CFG001 fix.
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
            ConfigContrabandAnalyzer,
            ConfigContrabandCodeFixProvider,
            Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = OptionsSource("""
                services.AddOptions<StripeOptions>()
                    .BindConfiguration({|#0:"Strip"|})
                    .ValidateDataAnnotations()
                    .ValidateOnStart();

                services.AddOptions<StripeOptions>()
                    .BindConfiguration("Stripe")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """, optionsTypes: StripeWithSecret),
            FixedCode = OptionsSource("""
                services.AddOptions<StripeOptions>()
                    .BindConfiguration("Stripe")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();

                services.AddOptions<StripeOptions>()
                    .BindConfiguration("Stripe")
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                """, optionsTypes: StripeWithSecret),
            ReferenceAssemblies = Verifier.OptionsReferences,
            CodeFixTestBehaviors = Microsoft.CodeAnalysis.Testing.CodeFixTestBehaviors.SkipLocalDiagnosticCheck,
            NumberOfIncrementalIterations = 1,
            NumberOfFixAllIterations = 1
        };
        test.TestState.AdditionalFiles.Add(("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "k",
                "WebookSecret": "s"
              }
            }
            """));
        test.FixedState.AdditionalFiles.Add(("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "k",
                "WebookSecret": "s"
              }
            }
            """));
        test.BatchFixedState.AdditionalFiles.Add(("appsettings.json", """
            {
              "Stripe": {
                "ApiKey": "k",
                "WebookSecret": "s"
              }
            }
            """));
        test.ExpectedDiagnostics.Add(
            Verifier.Diagnostic(DiagnosticDescriptors.MissingConfigurationSection)
                .WithLocation(0)
                .WithArguments("Strip", ". Did you mean \"Stripe\"?"));
        test.ExpectedDiagnostics.Add(
            Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKey)
                .WithSpan("appsettings.json", 4, 5, 4, 19)
                .WithArguments("Stripe:WebookSecret", "StripeOptions", ". Did you mean \"WebhookSecret\"?"));
        await test.RunAsync();
    }

    [Fact]
    public async Task Cfg002_fix_skips_diagnostics_without_key_properties()
    {
        var actions = await RegisterCfg002FixesAsync(
            properties: null,
            ("appsettings.json", "{ \"Stripe\": { \"WebhookSecret\": \"s\" } }"));
        Assert.Empty(actions);
    }

    [Fact]
    public async Task Cfg002_fix_skips_files_that_fail_to_load()
    {
        // The analyzer excludes rejected files from CFG001-CFG010, but a broken sibling can
        // still sit next to the file that produced the diagnostic.
        var actions = await RegisterCfg002FixesAsync(
            properties: Cfg002Properties("Stripe", "ApiKey"),
            ("appsettings.Broken.json", "{ \"Stripe\": {"));
        Assert.Empty(actions);
    }

    [Fact]
    public async Task Cfg002_fix_skips_scalar_sections()
    {
        var actions = await RegisterCfg002FixesAsync(
            properties: Cfg002Properties("Stripe", "ApiKey"),
            ("appsettings.json", "{ \"Stripe\": \"conn\" }"));
        Assert.Empty(actions);
    }

    [Fact]
    public async Task Cfg002_fix_skips_projected_sections()
    {
        // A flat "Stripe:WebhookSecret" key projects a synthetic "Stripe" object node with
        // no JSON span, so there is nowhere safe to insert a member.
        var actions = await RegisterCfg002FixesAsync(
            properties: Cfg002Properties("Stripe", "ApiKey"),
            ("appsettings.json", "{ \"Stripe:WebhookSecret\": \"s\" }"));
        Assert.Empty(actions);
    }

    [Fact]
    public async Task Cfg002_fix_ignores_non_appsettings_files()
    {
        var actions = await RegisterCfg002FixesAsync(
            properties: Cfg002Properties("Stripe", "ApiKey"),
            ("other.json", "{ \"Stripe\": { \"WebhookSecret\": \"s\" } }"));
        Assert.Empty(actions);
    }

    [Fact]
    public async Task Fix_all_returns_null_when_no_fix_matches_the_equivalence_key()
    {
        var (document, provider) = CreateFixWorkspace(
            Cfg002Properties("Stripe", "ApiKey"),
            ("appsettings.json", "{ \"Stripe\": { \"WebhookSecret\": \"s\" } }"),
            out var diagnostic);

        var fixAllContext = new FixAllContext(
            document,
            provider,
            FixAllScope.Document,
            "NoSuchEquivalenceKey",
            new[] { DiagnosticIds.MissingRequiredConfigurationKey },
            new StaticDiagnosticProvider(diagnostic),
            CancellationToken.None);

        Assert.Null(await ConfigContrabandFixAllProvider.Instance.GetFixAsync(fixAllContext));
    }

    private static ImmutableDictionary<string, string?> Cfg002Properties(string sectionPath, string key)
    {
        return ImmutableDictionary<string, string?>.Empty
            .Add(ConfigContrabandAnalyzer.RequiredKeySectionPathPropertyName, sectionPath)
            .Add(ConfigContrabandAnalyzer.RequiredKeyPropertyName, key);
    }

    private static (Document Document, ConfigContrabandCodeFixProvider Provider) CreateFixWorkspace(
        ImmutableDictionary<string, string?>? properties,
        (string Name, string Content) additionalFile,
        out Diagnostic diagnostic)
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(projectId, "Test", "Test", LanguageNames.CSharp);
        var documentId = DocumentId.CreateNewId(projectId);
        solution = solution.AddDocument(documentId, "Test.cs", SourceText.From("class C { }"));
        var project = solution.GetProject(projectId)!;
        var additionalDocument = project.AddAdditionalDocument(
            additionalFile.Name,
            SourceText.From(additionalFile.Content),
            filePath: additionalFile.Name);
        project = additionalDocument.Project;

        diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.MissingRequiredConfigurationKey,
            Location.None,
            properties,
            "ApiKey",
            "Stripe");
        return (project.GetDocument(documentId)!, new ConfigContrabandCodeFixProvider());
    }

    private static async Task<ImmutableArray<CodeAction>> RegisterCfg002FixesAsync(
        ImmutableDictionary<string, string?>? properties,
        params (string Name, string Content)[] additionalFiles)
    {
        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        foreach (var file in additionalFiles)
        {
            var (document, provider) = CreateFixWorkspace(properties, file, out var diagnostic);
            await provider.RegisterCodeFixesAsync(new CodeFixContext(
                document,
                diagnostic,
                (action, _) => actions.Add(action),
                CancellationToken.None));
        }

        return actions.ToImmutable();
    }

    private sealed class StaticDiagnosticProvider : FixAllContext.DiagnosticProvider
    {
        private readonly ImmutableArray<Diagnostic> _diagnostics;

        public StaticDiagnosticProvider(params Diagnostic[] diagnostics)
        {
            _diagnostics = diagnostics.ToImmutableArray();
        }

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(
            Project project, CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<Diagnostic>>(_diagnostics);
        }

        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(
            Document document, CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<Diagnostic>>(_diagnostics);
        }

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(
            Project project, CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<Diagnostic>>(_diagnostics);
        }
    }
}
