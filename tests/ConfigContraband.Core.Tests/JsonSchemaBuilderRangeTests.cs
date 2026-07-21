namespace ConfigContraband.Core.Tests;

public sealed partial class JsonSchemaBuilderTests
{
    [Fact]
    public void Range_on_an_integer_emits_inclusive_integer_bounds()
    {
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """,
            "ServerOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "Port": {
                  "type": "integer",
                  "minimum": 1,
                  "maximum": 65535
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Range_on_a_double_emits_invariant_number_bounds()
    {
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class RateOptions
            {
                [Range(0.5, 2.5)]
                public double Multiplier { get; set; }
            }
            """,
            "RateOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "Multiplier": {
                  "type": "number",
                  "minimum": 0.5,
                  "maximum": 2.5
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Range_double_bounds_stay_invariant_under_a_comma_decimal_culture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var schema = BuildSchema(
                """
                using System.ComponentModel.DataAnnotations;

                public sealed class RateOptions
                {
                    [Range(0.5, 2.5)]
                    public double Multiplier { get; set; }
                }
                """,
                "RateOptions");

            Assert.Contains("\"minimum\": 0.5", schema);
            Assert.Contains("\"maximum\": 2.5", schema);
            Assert.DoesNotContain("0,5", schema);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Range_type_string_overload_emits_numeric_bounds()
    {
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class PriceOptions
            {
                [Range(typeof(decimal), "0.0", "100.0")]
                public decimal Price { get; set; }
            }
            """,
            "PriceOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "Price": {
                  "type": "number",
                  "minimum": 0.0,
                  "maximum": 100.0
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Exclusive_range_bounds_emit_exclusive_keywords()
    {
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class RateOptions
            {
                [Range(0, 1, MinimumIsExclusive = true, MaximumIsExclusive = true)]
                public double Ratio { get; set; }
            }
            """,
            "RateOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "Ratio": {
                  "type": "number",
                  "exclusiveMinimum": 0,
                  "exclusiveMaximum": 1
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Range_string_bounds_are_normalized_to_json_numbers()
    {
        // ".5" and "1." are accepted by RangeAttribute but are not JSON number tokens; they are parsed and
        // re-formatted (".5" -> 0.5, "1." -> 1) instead of dropped.
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class PriceOptions
            {
                [Range(typeof(double), ".5", "1.")]
                public double Price { get; set; }
            }
            """,
            "PriceOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "Price": {
                  "type": "number",
                  "minimum": 0.5,
                  "maximum": 1
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Range_integer_string_bounds_are_normalized()
    {
        // "+1" and "010" are accepted by RangeAttribute for an int operand and re-formatted as 1 and 10.
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class WorkerOptions
            {
                [Range(typeof(int), "+1", "010")]
                public int Count { get; set; }
            }
            """,
            "WorkerOptions");

        Assert.Contains("\"minimum\": 1", schema);
        Assert.Contains("\"maximum\": 10", schema);
    }

    [Fact]
    public void Range_string_bounds_are_parsed_invariantly_and_deterministically()
    {
        // A build-time generator cannot know the app's runtime culture, so bounds are parsed invariantly to
        // keep the committed schema deterministic. Culture-specific comma bounds do not parse invariantly and
        // are dropped (the safe under-enforcing direction) regardless of the current culture.
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var schema = BuildSchema(
                """
                using System.ComponentModel.DataAnnotations;

                public sealed class PriceOptions
                {
                    [Range(typeof(decimal), "1,5", "2,5")]
                    public decimal Price { get; set; }
                }
                """,
                "PriceOptions");

            Assert.DoesNotContain("minimum", schema);
            Assert.DoesNotContain("maximum", schema);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Range_uint64_string_bounds_above_int64_max_are_kept()
    {
        // The ulong upper bound exceeds long.MaxValue, so it must be parsed as ulong rather than dropped.
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class CapacityOptions
            {
                [Range(typeof(ulong), "0", "18446744073709551615")]
                public ulong Capacity { get; set; }
            }
            """,
            "CapacityOptions");

        Assert.Contains("\"minimum\": 0", schema);
        Assert.Contains("\"maximum\": 18446744073709551615", schema);
    }

    [Fact]
    public void Range_double_string_bounds_use_operand_rounding()
    {
        // RangeAttribute parses the bound as a double, rounding 9007199254740993 to 9007199254740992; the
        // schema must mirror that rounding so it does not reject the value the runtime accepts.
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class PrecisionOptions
            {
                [Range(typeof(double), "9007199254740993", "9007199254740993")]
                public double Value { get; set; }
            }
            """,
            "PrecisionOptions");

        Assert.Contains("9007199254740992", schema);
        Assert.DoesNotContain("9007199254740993", schema);
    }

    [Fact]
    public void Unparseable_range_string_bounds_are_skipped()
    {
        // A bound that does not parse as a number is dropped so the schema never contains an invalid token.
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class PriceOptions
            {
                [Range(typeof(double), "abc", "5")]
                public double Price { get; set; }
            }
            """,
            "PriceOptions");

        Assert.DoesNotContain("minimum", schema);
        Assert.Contains("\"maximum\": 5", schema);
    }

    [Fact]
    public void Non_finite_range_bounds_are_skipped()
    {
        // double.PositiveInfinity is a compile-time constant, so this compiles; emitting "Infinity" would
        // produce invalid JSON, so the infinite bound is dropped while the finite one is kept.
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class LimitOptions
            {
                [Range(0, double.PositiveInfinity)]
                public double Limit { get; set; }
            }
            """,
            "LimitOptions");

        Assert.Contains("\"minimum\": 0", schema);
        Assert.DoesNotContain("maximum", schema);
        Assert.DoesNotContain("Infinity", schema);
    }

    [Fact]
    public void Max_length_on_a_string_emits_a_max_length_keyword()
    {
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class SecretOptions
            {
                [MaxLength(64)]
                public string ApiKey { get; set; } = "";
            }
            """,
            "SecretOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "ApiKey": {
                  "type": "string",
                  "maxLength": 64
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void String_length_emits_its_maximum_but_not_its_minimum()
    {
        // The StringLength maximum maps safely to maxLength; MinimumLength is intentionally not emitted
        // (UTF-16 vs Unicode code-point counting could otherwise reject a runtime-valid value).
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class NameOptions
            {
                [StringLength(64, MinimumLength = 8)]
                public string DisplayName { get; set; } = "";
            }
            """,
            "NameOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "DisplayName": {
                  "type": "string",
                  "maxLength": 64
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Min_length_is_not_emitted_to_avoid_surrogate_pair_false_positives()
    {
        // DataAnnotations counts UTF-16 units while JSON Schema counts code points, so emitting minLength
        // could reject a runtime-valid value containing non-BMP characters. It is intentionally omitted.
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class SecretOptions
            {
                [MinLength(32)]
                public string ApiKey { get; set; } = "";
            }
            """,
            "SecretOptions");

        Assert.DoesNotContain("minLength", schema);
    }

    [Fact]
    public void Combined_max_length_validators_keep_the_strictest_bound()
    {
        // All DataAnnotations validators run, so the schema keeps the smallest (strictest) maximum length.
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class SecretOptions
            {
                [MaxLength(64)]
                [StringLength(32)]
                public string ApiKey { get; set; } = "";
            }
            """,
            "SecretOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "ApiKey": {
                  "type": "string",
                  "maxLength": 32
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Negative_max_length_sentinel_is_skipped()
    {
        // DataAnnotations treats [MaxLength(-1)] as "no maximum"; emitting "maxLength": -1 would be invalid
        // JSON Schema and reject every non-empty string, so the bound is dropped.
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class BlobOptions
            {
                [MaxLength(-1)]
                public string Payload { get; set; } = "";
            }
            """,
            "BlobOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "Payload": {
                  "type": "string"
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Email_and_url_attributes_do_not_emit_a_format_keyword()
    {
        // [EmailAddress]/[Url] are lenient regex checks; JSON Schema `format: email`/`uri` follow strict RFC
        // grammars and could reject a runtime-valid value in a format-enforcing validator. To preserve the
        // zero-false-positive guarantee the schema emits no `format` for them.
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ContactOptions
            {
                [EmailAddress]
                public string Email { get; set; } = "";
                [Url]
                public string Endpoint { get; set; } = "";
            }
            """,
            "ContactOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "Email": {
                  "type": "string"
                },
                "Endpoint": {
                  "type": "string"
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Constraints_are_omitted_when_data_annotations_validation_is_not_enabled()
    {
        // Constraints, like [Required], are only enforced when ValidateDataAnnotations() runs (CFG002/CFG004),
        // so a loose binding must not be over-constrained.
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """,
            "ServerOptions",
            validates: false);

        Assert.DoesNotContain("minimum", schema);
        Assert.DoesNotContain("maximum", schema);
    }

    [Fact]
    public void Length_attributes_do_not_constrain_collection_properties()
    {
        // [MinLength]/[MaxLength] on a collection map to item counts at runtime, but this release only
        // emits constraints for scalar string/number nodes, so the array stays unconstrained (never a
        // false validation error).
        var schema = BuildSchema(
            """
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations;

            public sealed class HostOptions
            {
                [MaxLength(5)]
                public List<int> Ports { get; set; } = [];
            }
            """,
            "HostOptions");

        Assert.DoesNotContain("maxLength", schema);
        Assert.DoesNotContain("maxItems", schema);
        Assert.Contains("\"type\": \"array\"", schema);
    }

    [Fact]
    public void Range_minimum_precedes_maximum_regardless_of_attribute_shape()
    {
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [Range(1, 10)]
                public int Workers { get; set; }
            }
            """,
            "ServerOptions");

        Assert.True(schema.IndexOf("minimum", System.StringComparison.Ordinal)
                    < schema.IndexOf("maximum", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Xml_summary_becomes_a_description()
    {
        var schema = BuildSchema(
            """
            public sealed class ServerOptions
            {
                /// <summary>The TCP port the server listens on.</summary>
                public int Port { get; set; }
            }
            """,
            "ServerOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "Port": {
                  "type": "integer",
                  "description": "The TCP port the server listens on."
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Description_attribute_wins_over_xml_summary()
    {
        var schema = BuildSchema(
            """
            using System.ComponentModel;

            public sealed class ServerOptions
            {
                /// <summary>Ignored summary.</summary>
                [Description("The TCP port.")]
                public int Port { get; set; }
            }
            """,
            "ServerOptions");

        Assert.Contains("\"description\": \"The TCP port.\"", schema);
        Assert.DoesNotContain("Ignored summary", schema);
    }

    [Fact]
    public void Display_name_is_used_when_no_description_or_summary_exists()
    {
        var schema = BuildSchema(
            """
            using System.ComponentModel;

            public sealed class ServerOptions
            {
                [DisplayName("Listen Port")]
                public int Port { get; set; }
            }
            """,
            "ServerOptions");

        Assert.Contains("\"description\": \"Listen Port\"", schema);
    }

    [Fact]
    public void Properties_without_docs_emit_no_description()
    {
        var schema = BuildSchema(
            """
            public sealed class ServerOptions
            {
                public int Port { get; set; }
            }
            """,
            "ServerOptions");

        Assert.DoesNotContain("description", schema);
    }

    [Fact]
    public void Inheritdoc_only_documentation_is_skipped()
    {
        var schema = BuildSchema(
            """
            public sealed class ServerOptions
            {
                /// <inheritdoc/>
                public int Port { get; set; }
            }
            """,
            "ServerOptions");

        Assert.DoesNotContain("description", schema);
    }

    [Fact]
    public void Inline_doc_tags_are_stripped_but_their_text_is_kept()
    {
        var schema = BuildSchema(
            """
            public sealed class ServerOptions
            {
                /// <summary>The <c>Port</c> the server <see cref="ServerOptions"/> listens on.</summary>
                public int Port { get; set; }
            }
            """,
            "ServerOptions");

        Assert.Contains("\"description\": \"The Port the server listens on.\"", schema);
    }

    [Fact]
    public void Multiline_summary_is_collapsed_to_a_single_line()
    {
        var schema = BuildSchema(
            """
            public sealed class ServerOptions
            {
                /// <summary>
                /// The TCP port
                /// the server listens on.
                /// </summary>
                public int Port { get; set; }
            }
            """,
            "ServerOptions");

        Assert.Contains("\"description\": \"The TCP port the server listens on.\"", schema);
    }

    [Fact]
    public void Type_level_summary_describes_the_object_node()
    {
        var schema = BuildSchema(
            """
            /// <summary>Connection settings for the primary database.</summary>
            public sealed class DatabaseOptions
            {
                public string ConnectionString { get; set; } = "";
            }
            """,
            "DatabaseOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "description": "Connection settings for the primary database.",
              "properties": {
                "ConnectionString": {
                  "type": "string"
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Property_summary_overrides_the_nested_type_summary()
    {
        var schema = BuildSchema(
            """
            public sealed class AppOptions
            {
                /// <summary>The primary database.</summary>
                public DatabaseOptions Database { get; set; } = new();
            }

            /// <summary>Generic database settings.</summary>
            public sealed class DatabaseOptions
            {
                public string ConnectionString { get; set; } = "";
            }
            """,
            "AppOptions");

        Assert.Contains("\"description\": \"The primary database.\"", schema);
        Assert.DoesNotContain("Generic database settings.", schema);
    }

    [Fact]
    public void Description_and_constraints_emit_together_in_canonical_order()
    {
        var schema = BuildSchema(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                /// <summary>The TCP port.</summary>
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """,
            "ServerOptions");

        Assert.Equal(
            """
            {
              "type": "object",
              "properties": {
                "Port": {
                  "type": "integer",
                  "description": "The TCP port.",
                  "minimum": 1,
                  "maximum": 65535
                }
              }
            }
            """,
            schema,
            ignoreLineEndingDifferences: true);
    }
}
