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
}
