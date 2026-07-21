using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{
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
