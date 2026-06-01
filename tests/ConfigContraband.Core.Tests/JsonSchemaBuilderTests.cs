using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ConfigContraband.Core.Tests;

public sealed class JsonSchemaBuilderTests
{
    [Fact]
    public void Scalar_properties_map_to_json_schema_types()
    {
        var schema = BuildSchema(
            """
            public sealed class StripeOptions
            {
                public string ApiKey { get; set; } = "";
                public int RetryCount { get; set; }
                public bool Enabled { get; set; }
                public double Rate { get; set; }
            }
            """,
            "StripeOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "ApiKey": {
                  "type": "string"
                },
                "RetryCount": {
                  "type": "integer"
                },
                "Enabled": {
                  "type": "boolean"
                },
                "Rate": {
                  "type": "number"
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Required_data_annotations_populate_the_required_array()
    {
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class DbOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";
                public int Timeout { get; set; }
            }
            """,
            "DbOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "ConnectionString": {
                  "type": "string"
                },
                "Timeout": {
                  "type": "integer"
                }
              },
              "required": [
                "ConnectionString"
              ]
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Nested_object_properties_recurse()
    {
        var schema = BuildSchema(
            """
            public sealed class AppOptions
            {
                public DatabaseOptions Database { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                public string ConnectionString { get; set; } = "";
            }
            """,
            "AppOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "Database": {
                  "type": "object",
                  "properties": {
                    "ConnectionString": {
                      "type": "string"
                    }
                  }
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Enum_properties_emit_string_with_member_names()
    {
        var schema = BuildSchema(
            """
            public enum Level { Trace, Debug, Info }

            public sealed class LogOptions
            {
                public Level MinimumLevel { get; set; }
            }
            """,
            "LogOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "MinimumLevel": {
                  "type": "string",
                  "enum": [
                    "Trace",
                    "Debug",
                    "Info"
                  ]
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Collection_properties_emit_array_with_item_schema()
    {
        var schema = BuildSchema(
            """
            using System.Collections.Generic;

            public sealed class HostOptions
            {
                public string[] Hosts { get; set; } = [];
                public List<int> Ports { get; set; } = [];
            }
            """,
            "HostOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "Hosts": {
                  "type": "array",
                  "items": {
                    "type": "string"
                  }
                },
                "Ports": {
                  "type": "array",
                  "items": {
                    "type": "integer"
                  }
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Dictionary_properties_emit_object_with_additional_properties()
    {
        var schema = BuildSchema(
            """
            using System.Collections.Generic;

            public sealed class CacheOptions
            {
                public Dictionary<string, int> Limits { get; set; } = new();
            }
            """,
            "CacheOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "Limits": {
                  "type": "object",
                  "additionalProperties": {
                    "type": "integer"
                  }
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Strict_bindings_forbid_unknown_keys_recursively()
    {
        var schema = BuildSchema(
            """
            public sealed class RootOptions
            {
                public string ApiKey { get; set; } = "";
                public ChildOptions Child { get; set; } = new();
            }

            public sealed class ChildOptions
            {
                public int Value { get; set; }
            }
            """,
            "RootOptions",
            strict: true);

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "ApiKey": {
                  "type": "string"
                },
                "Child": {
                  "type": "object",
                  "properties": {
                    "Value": {
                      "type": "integer"
                    }
                  },
                  "additionalProperties": false
                }
              },
              "additionalProperties": false
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Configuration_key_name_alias_replaces_the_clr_property_name()
    {
        var schema = BuildSchema(
            """
            using Microsoft.Extensions.Configuration;

            public sealed class AliasOptions
            {
                [ConfigurationKeyName("api-key")]
                public string ApiKey { get; set; } = "";
            }
            """,
            "AliasOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "api-key": {
                  "type": "string"
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Nullable_value_types_are_unwrapped_to_the_underlying_type()
    {
        var schema = BuildSchema(
            """
            using System;

            public sealed class TimeoutOptions
            {
                public int? MaxRetries { get; set; }
                public DateTime? Expiry { get; set; }
            }
            """,
            "TimeoutOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "MaxRetries": {
                  "type": "integer"
                },
                "Expiry": {
                  "type": "string"
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Cyclic_option_graphs_terminate_with_an_open_object()
    {
        var schema = BuildSchema(
            """
            public sealed class NodeOptions
            {
                public string Name { get; set; } = "";
                public NodeOptions? Next { get; set; }
            }
            """,
            "NodeOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "Name": {
                  "type": "string"
                },
                "Next": {
                  "type": "object"
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Document_nests_sections_by_colon_path_under_a_draft07_root()
    {
        var (compilation, type) = Compile(
            """
            public sealed class StripeOptions
            {
                public string ApiKey { get; set; } = "";
            }
            """,
            "StripeOptions");

        var sections = new[]
        {
            new SchemaSection("Features:Stripe", type, strict: false, bindsNonPublicProperties: false),
        };

        var document = SchemaDocumentBuilder.Build(sections, compilation).ToJsonString();

        Assert.Equal(
            """
            {
              "$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "properties": {
                "Features": {
                  "type": "object",
                  "properties": {
                    "Stripe": {
                      "type": "object",
                      "properties": {
                        "ApiKey": {
                          "type": "string"
                        }
                      }
                    }
                  }
                }
              }
            }
            """,
            document,
            ignoreLineEndingDifferences: true);
    }

    private static string BuildSchema(string source, string typeName, bool strict = false)
    {
        var (compilation, type) = Compile(source, typeName);
        return JsonSchemaBuilder.BuildObjectSchema(type, compilation, strict).ToJsonString();
    }

    private static (Compilation compilation, INamedTypeSymbol type) Compile(string source, string typeName)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            "SchemaTests",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return (compilation, compilation.GetTypeByMetadataName(typeName)!);
    }
}
