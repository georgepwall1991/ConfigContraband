using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{

    [Fact]
    public async Task Cfg007_reports_child_key_under_scalar_collection_item_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<string> Values { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 14)
            .WithArguments("App:Values:0:Foo", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Values": [
                  {
                    "Foo": "x"
                  }
                ]
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_clr_member_shaped_child_key_under_string_collection_item_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<string> Values { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 17)
            .WithArguments("App:Values:0:Length", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Values": [
                  {
                    "Length": 5
                  }
                ]
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_known_child_under_value_type_collection_item_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<TimeSpan> Durations { get; set; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Durations": [
                  {
                    "Ticks": 123
                  }
                ]
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_scalar_reference_collection_item_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<Uri> Endpoints { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 22)
            .WithArguments("App:Endpoints:0:AbsoluteUri", "Uri", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoints": [
                  {
                    "AbsoluteUri": "https://example.test"
                  }
                ]
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_known_child_under_creatable_reference_collection_item_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<Version> Versions { get; set; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Versions": [
                  {
                    "Major": 1
                  }
                ]
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_does_not_report_dictionary_entries_inside_collection_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<Dictionary<string, string>> Values { get; set; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Values": [
                  {
                    "primary": "x"
                  }
                ]
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_does_not_report_known_child_under_value_type_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Point> Points { get; set; } = [];
            }

            public struct Point
            {
                public int X { get; set; }

                public int Y { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Points": {
                  "origin": {
                    "X": 1,
                    "Y": 2
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_does_not_report_known_child_under_creatable_reference_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Version> Versions { get; set; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Versions": {
                  "stable": {
                    "Major": 1
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_scalar_reference_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Uri> Endpoints { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 22)
            .WithArguments("App:Endpoints:primary:AbsoluteUri", "Uri", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoints": {
                  "primary": {
                    "AbsoluteUri": "https://example.test"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_nested_dictionary_values_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Dictionary<string, FeatureOptions>> Map { get; set; } = [];
            }

            public sealed class FeatureOptions
            {
                public bool Enabled { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "Tenant": {
                    "Feature": {
                      "Enabled": true
                    }
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_nested_object_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Dictionary<string, FeatureOptions>> Map { get; set; } = [];
            }

            public sealed class FeatureOptions
            {
                public bool Enabled { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 6, 11, 6, 20)
            .WithArguments("App:Map:Tenant:Feature:Enabeld", "FeatureOptions", ". Did you mean \"Enabled\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "Tenant": {
                    "Feature": {
                      "Enabeld": true
                    }
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_nested_dictionary_object_collection_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Dictionary<string, List<FeatureOptions>>> Map { get; set; } = [];
            }

            public sealed class FeatureOptions
            {
                public bool Enabled { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 7, 13, 7, 22)
            .WithArguments("App:Map:Tenant:Features:0:Enabeld", "FeatureOptions", ". Did you mean \"Enabled\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "Tenant": {
                    "Features": [
                      {
                        "Enabeld": true
                      }
                    ]
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_nested_scalar_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, Dictionary<string, int>> Ports { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 6, 11, 6, 16)
            .WithArguments("App:Ports:tenant:https:Foo", "Int32", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Ports": {
                  "tenant": {
                    "https": {
                      "Foo": 443
                    }
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_child_key_under_scalar_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<string, string> Labels { get; set; } = [];
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 5, 9, 5, 14)
            .WithArguments("App:Labels:primary:Foo", "String", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Labels": {
                  "primary": {
                    "Foo": "x"
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_does_not_report_open_dictionary_value_shape_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class Endpoint
            {
                public string Url { get; set; } = "";
            }

            public sealed class AppOptions
            {
                public Dictionary<string, object> Map { get; } = new()
                {
                    ["main"] = new Endpoint()
                };
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Map": {
                  "main": {
                    "Url": "https://example.test"
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_stays_quiet_for_guid_keyed_dictionary_nested_object_values_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<Guid, EndpointOptions> Endpoints { get; set; } = [];
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
              "App": {
                "Endpoints": {
                  "3fa85f64-5717-4562-b3fc-2c963f66afa6": {
                    "Url": "https://example.test",
                    "Timout": 5
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_stays_quiet_for_guid_keyed_dictionary_object_shaped_scalar_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<Guid, string> Labels { get; set; } = [];
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Labels": {
                  "3fa85f64-5717-4562-b3fc-2c963f66afa6": {
                    "Foo": "x"
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg007_reports_unknown_key_under_enum_keyed_dictionary_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public enum Region
            {
                East,
                West
            }

            public sealed class AppOptions
            {
                public Dictionary<Region, EndpointOptions> Endpoints { get; set; } = [];
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 6, 9, 6, 17)
            .WithArguments("App:Endpoints:East:Timout", "EndpointOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoints": {
                  "East": {
                    "Url": "https://example.test",
                    "Timout": 5
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_unknown_key_under_int_keyed_dictionary_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<int, EndpointOptions> Endpoints { get; set; } = [];
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 6, 9, 6, 17)
            .WithArguments("App:Endpoints:1:Timout", "EndpointOptions", ".");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoints": {
                  "1": {
                    "Url": "https://example.test",
                    "Timout": 5
                  }
                }
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg007_reports_property_name_typo_but_stays_quiet_inside_guid_keyed_dictionary_value_when_error_on_unknown_configuration_is_enabled()
    {
        var source = OptionsSource("""
            services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.ErrorOnUnknownConfiguration = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """, extraUsings: "using System;\nusing System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public Dictionary<Guid, EndpointOptions> Endpoints { get; set; } = [];
            }

            public sealed class EndpointOptions
            {
                public string Url { get; set; } = "";
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.UnknownConfigurationKeyWillThrow)
            .WithSpan("appsettings.json", 9, 5, 9, 15)
            .WithArguments("App:Endpints", "AppOptions", ". Did you mean \"Endpoints\"?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "App": {
                "Endpoints": {
                  "3fa85f64-5717-4562-b3fc-2c963f66afa6": {
                    "Url": "https://example.test",
                    "Timout": 5
                  }
                },
                "Endpints": {
                  "Foo": "bar"
                }
              }
            }
            """),
            expected);
    }
}
