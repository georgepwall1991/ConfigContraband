using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{

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
    public async Task Cfg002_reports_missing_key_when_default_struct_skips_member_initializer()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, """
            using Microsoft.Extensions.Options;
            public class AppOptions { [ValidateObjectMembers] public DatabaseOptions Database { get; set; } }
            public struct DatabaseOptions
            {
                public DatabaseOptions() { }
                [Required] public string ConnectionString { get; set; } = "ok";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithSpan(16, 24, 16, 29)
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
}
