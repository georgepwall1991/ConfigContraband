using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ConfigContraband.Core.Tests;

public sealed partial class JsonSchemaBuilderTests
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
    public void Required_properties_with_satisfying_defaults_are_not_marked_required()
    {
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class DbOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";

                [Required]
                public string Host { get; set; } = "localhost";
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
                "Host": {
                  "type": "string"
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
    public void Required_recursive_object_with_default_keeps_parent_required_when_nested_required_is_unsatisfied()
    {
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.Options;

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
                  },
                  "required": [
                    "ConnectionString"
                  ]
                }
              },
              "required": [
                "Database"
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
    public void Alias_declared_only_on_virtual_override_is_not_emitted()
    {
        var schema = BuildSchema(
            """
            using Microsoft.Extensions.Configuration;

            public class BaseOptions
            {
                public virtual string ApiKey { get; set; } = "";
            }

            public sealed class DerivedOptions : BaseOptions
            {
                [ConfigurationKeyName("api-key")]
                public override string ApiKey { get; set; } = "";
            }
            """,
            "DerivedOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "ApiKey": {
                  "type": "string"
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Virtual_override_keeps_inherited_required_schema_metadata()
    {
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public class BaseOptions
            {
                [Required, MaxLength(5)]
                public virtual string ApiKey { get; set; } = "";

                [StringLength(5)]
                public virtual string Token { get; set; } = "";
            }

            public sealed class DerivedOptions : BaseOptions
            {
                [StringLength(10)]
                public override string ApiKey { get; set; } = "";

                [StringLength(10)]
                public override string Token { get; set; } = "";
            }
            """,
            "DerivedOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "ApiKey": {
                  "type": "string",
                  "maxLength": 5
                },
                "Token": {
                  "type": "string",
                  "maxLength": 10
                }
              },
              "required": [
                "ApiKey"
              ]
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);

        var customTypeIdSchema = BuildSchema(
            """
            using System;
            using System.ComponentModel.DataAnnotations;

            public sealed class ReplacingMetadataAttribute : Attribute
            {
                public override object TypeId => typeof(MaxLengthAttribute);
            }

            public class BaseOptions
            {
                [MaxLength(5)]
                public virtual string ApiKey { get; set; } = "";
            }

            public sealed class DerivedOptions : BaseOptions
            {
                [ReplacingMetadata]
                public override string ApiKey { get; set; } = "";
            }
            """,
            "DerivedOptions");

        Assert.DoesNotContain("\"maxLength\"", customTypeIdSchema);
    }

    [Fact]
    public void Virtual_override_uses_effective_required_attribute_and_derived_default()
    {
        var allowsEmptySchema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public class BaseOptions
            {
                [Required]
                public virtual string ApiKey { get; set; } = "";
            }

            public sealed class DerivedOptions : BaseOptions
            {
                [Required(AllowEmptyStrings = true)]
                public override string ApiKey { get; set; } = "";
            }
            """,
            "DerivedOptions");

        Assert.DoesNotContain("\"required\"", allowsEmptySchema);

        var derivedNullSchema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public class BaseOptions
            {
                [Required]
                public virtual string ApiKey { get; set; } = "base-default";
            }

            public sealed class DerivedOptions : BaseOptions
            {
                public override string ApiKey { get; set; } = null!;
            }
            """,
            "DerivedOptions");

        Assert.Contains("\"required\": [\n    \"ApiKey\"\n  ]", derivedNullSchema);

        var distinctRequiredTypesSchema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class AllowsEmptyRequiredAttribute : RequiredAttribute
            {
                public AllowsEmptyRequiredAttribute()
                {
                    AllowEmptyStrings = true;
                }
            }

            public class BaseOptions
            {
                [Required]
                public virtual string ApiKey { get; set; } = "";
            }

            public sealed class DerivedOptions : BaseOptions
            {
                [AllowsEmptyRequired]
                public override string ApiKey { get; set; } = "";
            }
            """,
            "DerivedOptions");

        Assert.Contains("\"required\": [\n    \"ApiKey\"\n  ]", distinctRequiredTypesSchema);

        var customTypeIdSchema = BuildSchema(
            """
            using System;
            using System.ComponentModel.DataAnnotations;

            public sealed class ReplacingRequiredAttribute : RequiredAttribute
            {
                public ReplacingRequiredAttribute()
                {
                    AllowEmptyStrings = true;
                }

                public override object TypeId => typeof(RequiredAttribute);
            }

            public class BaseOptions
            {
                [Required]
                public virtual string ApiKey { get; set; } = "";
            }

            public sealed class DerivedOptions : BaseOptions
            {
                [ReplacingRequired]
                public override string ApiKey { get; set; } = "";
            }
            """,
            "DerivedOptions");

        Assert.DoesNotContain("\"required\"", customTypeIdSchema);

        var nonValidationTypeIdSchema = BuildSchema(
            """
            using System;
            using System.ComponentModel.DataAnnotations;

            public sealed class ReplacingMetadataAttribute : Attribute
            {
                public override object TypeId => typeof(RequiredAttribute);
            }

            public class BaseOptions
            {
                [Required]
                public virtual string ApiKey { get; set; } = "";
            }

            public sealed class DerivedOptions : BaseOptions
            {
                [ReplacingMetadata]
                public override string ApiKey { get; set; } = "";
            }
            """,
            "DerivedOptions");

        Assert.DoesNotContain("\"required\"", nonValidationTypeIdSchema);
    }

    [Fact]
    public void Nullable_value_types_unwrap_and_also_accept_null()
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
                  "type": [
                    "integer",
                    "null"
                  ]
                },
                "Expiry": {
                  "type": [
                    "string",
                    "null"
                  ]
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
            new SchemaSection("Features:Stripe", type, strict: false, bindsNonPublicProperties: false, validatesDataAnnotations: false),
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

    [Theory]
    [InlineData("byte", "integer")]
    [InlineData("sbyte", "integer")]
    [InlineData("short", "integer")]
    [InlineData("ushort", "integer")]
    [InlineData("int", "integer")]
    [InlineData("uint", "integer")]
    [InlineData("long", "integer")]
    [InlineData("ulong", "integer")]
    [InlineData("float", "number")]
    [InlineData("double", "number")]
    [InlineData("decimal", "number")]
    [InlineData("bool", "boolean")]
    [InlineData("char", "string")]
    [InlineData("string", "string")]
    [InlineData("System.Guid", "string")]
    [InlineData("System.Uri", "string")]
    [InlineData("System.Version", "string")]
    [InlineData("System.TimeSpan", "string")]
    [InlineData("System.DateTime", "string")]
    [InlineData("System.DateTimeOffset", "string")]
    [InlineData("System.DateOnly", "string")]
    [InlineData("System.TimeOnly", "string")]
    public void Clr_scalar_types_map_to_expected_json_type(string clrType, string jsonType)
    {
        var schema = BuildSchema(
            $"public sealed class O {{ public {clrType} P {{ get; set; }} = default!; }}",
            "O");

        Assert.Contains($"\"type\": \"{jsonType}\"", schema);
    }

    [Fact]
    public void Unknown_scalar_types_stay_permissive_with_no_type_constraint()
    {
        var schema = BuildSchema(
            "public struct Custom { } public sealed class O { public Custom P { get; set; } }",
            "O");

        Assert.Contains("\"P\": {}", schema);
    }

    [Fact]
    public void Options_type_with_no_bindable_properties_emits_bare_object()
    {
        var schema = BuildSchema("public sealed class O { }", "O");

        Assert.Equal(
            """
            {
              "type": "object"
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Document_merges_sections_that_share_a_prefix()
    {
        var (compilation, first) = Compile(
            """
            public sealed class FirstOptions
            {
                public string A { get; set; } = "";
            }

            public sealed class SecondOptions
            {
                public int B { get; set; }
            }
            """,
            "FirstOptions");
        var second = compilation.GetTypeByMetadataName("SecondOptions")!;

        var sections = new[]
        {
            new SchemaSection("Outer:First", first, strict: false, bindsNonPublicProperties: false, validatesDataAnnotations: false),
            new SchemaSection("Outer:Second", second, strict: false, bindsNonPublicProperties: false, validatesDataAnnotations: false),
        };

        var document = SchemaDocumentBuilder.Build(sections, compilation).ToJsonString();

        Assert.Equal(
            """
            {
              "$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "properties": {
                "Outer": {
                  "type": "object",
                  "properties": {
                    "First": {
                      "type": "object",
                      "properties": {
                        "A": {
                          "type": "string"
                        }
                      }
                    },
                    "Second": {
                      "type": "object",
                      "properties": {
                        "B": {
                          "type": "integer"
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

    [Fact]
    public void Polymorphic_initialized_property_stays_open_under_strict_binding()
    {
        var schema = BuildSchema(
            """
            public class BaseOptions
            {
                public string Common { get; set; } = "";
            }

            public sealed class DerivedOptions : BaseOptions
            {
                public string Extra { get; set; } = "";
            }

            public sealed class RootOptions
            {
                public BaseOptions Section { get; set; } = new DerivedOptions();
            }
            """,
            "RootOptions",
            strict: true);

        // The root stays strict, but the polymorphic Section must remain open so derived-only keys
        // (which the runtime binder accepts) are not flagged as unknown.
        var additionalPropertiesFalseCount =
            schema.Split(["\"additionalProperties\": false"], StringSplitOptions.None).Length - 1;
        Assert.Equal(1, additionalPropertiesFalseCount);
        Assert.Contains("\"Common\"", schema);
    }

    [Fact]
    public void Nullable_enum_allows_null_in_both_type_and_enum()
    {
        var schema = BuildSchema(
            """
            public enum Level { Trace, Debug }

            public sealed class LogOptions
            {
                public Level? MinimumLevel { get; set; }
            }
            """,
            "LogOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "MinimumLevel": {
                  "type": [
                    "string",
                    "null"
                  ],
                  "enum": [
                    "Trace",
                    "Debug",
                    null
                  ]
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Required_is_omitted_when_data_annotations_validation_is_not_enabled()
    {
        // [Required] without ValidateDataAnnotations() is not enforced at runtime (CFG002), so the
        // schema must not mark the key required.
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class DbOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";
            }
            """,
            "DbOptions",
            validates: false);

        Assert.Contains("\"ConnectionString\"", schema);
        Assert.DoesNotContain("\"required\"", schema);
    }

    [Fact]
    public void Document_keeps_child_section_when_parent_is_also_bound()
    {
        var (compilation, parent) = Compile(
            """
            public sealed class FeaturesOptions
            {
                public bool Enabled { get; set; }
            }

            public sealed class StripeOptions
            {
                public string ApiKey { get; set; } = "";
            }
            """,
            "FeaturesOptions");
        var stripe = compilation.GetTypeByMetadataName("StripeOptions")!;

        var sections = new[]
        {
            new SchemaSection("Features", parent, strict: false, bindsNonPublicProperties: false, validatesDataAnnotations: false),
            new SchemaSection("Features:Stripe", stripe, strict: false, bindsNonPublicProperties: false, validatesDataAnnotations: false),
        };

        var document = SchemaDocumentBuilder.Build(sections, compilation).ToJsonString();

        // The parent type's own property survives, and the separately-bound child section is folded in.
        Assert.Contains("\"Enabled\"", document);
        Assert.Contains("\"Stripe\"", document);
        Assert.Contains("\"ApiKey\"", document);
    }

    private static string BuildSchema(string source, string typeName, bool strict = false, bool validates = true)
    {
        var (compilation, type) = Compile(source, typeName);
        return JsonSchemaBuilder
            .BuildObjectSchema(type, compilation, strict, bindsNonPublicProperties: false, validatesDataAnnotations: validates)
            .ToJsonString();
    }

    private static (Compilation compilation, INamedTypeSymbol type) Compile(string source, string typeName)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            "SchemaTests",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(documentationMode: DocumentationMode.Parse))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return (compilation, compilation.GetTypeByMetadataName(typeName)!);
    }
}
