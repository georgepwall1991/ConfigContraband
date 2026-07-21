using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{
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
}
