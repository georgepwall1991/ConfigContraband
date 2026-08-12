using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{
    [Fact]
    public async Task Cfg010_reports_out_of_range_integer()
    {
        var source = OptionsSource(
            """
            services.AddOptions<ServerOptions>()
                .BindConfiguration("Server")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """,
            optionsTypes: """
            public sealed class ServerOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueFailsValidation)
            .WithSpan("appsettings.json", 3, 13, 3, 14)
            .WithArguments("Server:Port", "Range", "ServerOptions");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": 0
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg010_stays_quiet_for_in_range_integer()
    {
        var source = OptionsSource(
            """
            services.AddOptions<ServerOptions>()
                .BindConfiguration("Server")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """,
            optionsTypes: """
            public sealed class ServerOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": 8080
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg010_stays_quiet_without_validate_data_annotations()
    {
        var source = OptionsSource(
            """
            {|#0:services.AddOptions<ServerOptions>()
                .BindConfiguration("Server")
                .ValidateOnStart()|};
            """,
            optionsTypes: """
            public sealed class ServerOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.DataAnnotationsNotEnabled)
            .WithLocation(0)
            .WithArguments("ServerOptions");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": 0
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg010_does_not_report_when_cfg008_owns_conversion_failure()
    {
        var source = OptionsSource(
            """
            services.AddOptions<ServerOptions>()
                .BindConfiguration("Server")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """,
            optionsTypes: """
            public sealed class ServerOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
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
    public async Task Cfg010_stays_quiet_for_missing_required_key()
    {
        var source = OptionsSource(
            """
            services.AddOptions<ServerOptions>()
                .BindConfiguration({|#0:"Server"|})
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """,
            optionsTypes: """
            public sealed class ServerOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.MissingRequiredConfigurationKey)
            .WithLocation(0)
            .WithArguments("ApiKey", "Server");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg010_reports_maxlength_failure()
    {
        var source = OptionsSource(
            """
            services.AddOptions<NameOptions>()
                .BindConfiguration("Name")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """,
            optionsTypes: """
            public sealed class NameOptions
            {
                [MaxLength(3)]
                public string Code { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueFailsValidation)
            .WithSpan("appsettings.json", 3, 13, 3, 19)
            .WithArguments("Name:Code", "MaxLength", "NameOptions");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Name": {
                "Code": "abcd"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg010_reports_allowed_values_failure()
    {
        var source = OptionsSource(
            """
            services.AddOptions<EnvOptions>()
                .BindConfiguration("Env")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """,
            optionsTypes: """
            public sealed class EnvOptions
            {
                [AllowedValues("dev", "prod")]
                public string Environment { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueFailsValidation)
            .WithSpan("appsettings.json", 3, 20, 3, 29)
            .WithArguments("Env:Environment", "AllowedValues", "EnvOptions");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Env": {
                "Environment": "staging"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg010_stays_quiet_for_nested_range_without_recursive_validation()
    {
        var source = OptionsSource(
            """
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """,
            optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions {|#1:Database|} { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Database": {
                  "Port": 0
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg010_reports_nested_range_when_recursive_validation_is_enabled()
    {
        var source = OptionsSource(
            """
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """,
            extraUsings: "using Microsoft.Extensions.Options;\n",
            optionsTypes: """
            public sealed class AppOptions
            {
                [ValidateObjectMembers]
                public DatabaseOptions Database { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueFailsValidation)
            .WithSpan("appsettings.json", 4, 15, 4, 16)
            .WithArguments("App:Database:Port", "Range", "DatabaseOptions");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Database": {
                  "Port": 0
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg010_stays_quiet_under_dictionary_values()
    {
        var source = OptionsSource(
            """
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """,
            extraUsings: "using System.Collections.Generic;\n",
            optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, EndpointOptions> Endpoints { get; set; } = new();
            }

            public sealed class EndpointOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoints": {
                  "primary": {
                    "Port": 0
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg010_reports_direct_configure_when_same_block_enables_data_annotations()
    {
        var source = OptionsSource(
            """
            IConfiguration configuration = null!;
            services.Configure<ServerOptions>(configuration.GetSection("Server"));
            services.AddOptions<ServerOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """,
            extraUsings: "using Microsoft.Extensions.Configuration;\n",
            optionsTypes: """
            public sealed class ServerOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueFailsValidation)
            .WithSpan("appsettings.json", 3, 13, 3, 14)
            .WithArguments("Server:Port", "Range", "ServerOptions");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": 0
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg010_stays_quiet_for_configure_only()
    {
        var source = OptionsSource(
            """
            IConfiguration configuration = null!;
            services.Configure<ServerOptions>(configuration.GetSection("Server"));
            """,
            extraUsings: "using Microsoft.Extensions.Configuration;\n",
            optionsTypes: """
            public sealed class ServerOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": 0
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg010_stays_quiet_for_direct_getvalue()
    {
        var source = DirectReadSource("""
            _ = configuration.GetValue<int>("Server:Port");
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": 0
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg010_reports_collection_range_when_enumerated_validation_is_enabled()
    {
        var source = OptionsSource(
            """
            services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """,
            extraUsings: "using System.Collections.Generic;\nusing Microsoft.Extensions.Options;\n",
            optionsTypes: """
            public sealed class AppOptions
            {
                [ValidateEnumeratedItems]
                public List<EndpointOptions> Endpoints { get; set; } = new();
            }

            public sealed class EndpointOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueFailsValidation)
            .WithSpan("appsettings.json", 5, 17, 5, 18)
            .WithArguments("App:Endpoints:0:Port", "Range", "EndpointOptions");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoints": [
                  {
                    "Port": 0
                  }
                ]
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg010_stays_quiet_for_culture_dependent_range()
    {
        var source = OptionsSource(
            """
            services.AddOptions<PriceOptions>()
                .BindConfiguration("Price")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """,
            optionsTypes: """
            public sealed class PriceOptions
            {
                [Range(typeof(decimal), "0.0", "100.0")]
                public decimal Amount { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Price": {
                "Amount": -1
              }
            }
            """));
    }
}
