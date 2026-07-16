using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ConfigContraband.Tests;

public sealed class ConfigurationModelCoverageTests
{
    [Fact]
    public void Json_parser_decodes_supported_escapes()
    {
        var root = JsonConfigurationParser.Parse("appsettings.json", SourceText.From("""
            {
              "Escaped\"Quote\\Slash\/Back\bForm\fLine\nReturn\rTab\tUnicode\u0041Lower\u006fOther\q": {
                "Value": "ok"
              }
            }
            """));

        Assert.NotNull(root);
        var property = Assert.Single(root!.Properties);
        Assert.Contains('"', property.Key);
        Assert.Contains('\\', property.Key);
        Assert.Contains('/', property.Key);
        Assert.Contains('\b', property.Key);
        Assert.Contains('\f', property.Key);
        Assert.Contains('\n', property.Key);
        Assert.Contains('\r', property.Key);
        Assert.Contains('\t', property.Key);
        Assert.Contains("UnicodeA", property.Key);
        Assert.Contains("Lowero", property.Key);
        Assert.Contains("Otherq", property.Key);
    }

    [Fact]
    public void Json_parser_keeps_malformed_unicode_escapes_as_literal_text()
    {
        var root = JsonConfigurationParser.Parse("appsettings.json", SourceText.From("""
            {
              "Bad\u00g1": {
                "Value": "ok"
              }
            }
            """));

        Assert.NotNull(root);
        var property = Assert.Single(root!.Properties);
        Assert.Equal("Badu00g1", property.Key);
    }

    [Fact]
    public void Json_parser_keeps_unfinished_unicode_escape_as_literal_text()
    {
        var root = JsonConfigurationParser.Parse("appsettings.json", SourceText.From("{\"Bad\\u"));

        Assert.NotNull(root);
        var property = Assert.Single(root!.Properties);
        Assert.Equal("Badu", property.Key);
    }

    [Fact]
    public void Json_parser_recovers_from_malformed_object_member()
    {
        var root = JsonConfigurationParser.Parse("appsettings.json", SourceText.From("{ unquoted: true }"));
        Assert.NotNull(root);
        var file = new ConfigurationFile("appsettings.json", root);

        Assert.Equal("appsettings.json", file.Path);
        Assert.Empty(file.Root.Properties);
        Assert.True(file.Root.IsObject);
        Assert.False(file.Root.TryGetProperty("Missing", out _));
    }

    [Fact]
    public void Json_parser_skips_line_and_block_comments()
    {
        var root = JsonConfigurationParser.Parse("appsettings.json", SourceText.From("""
            {
              // environment-specific overrides are merged at runtime
              "Stripe": {
                /* required by StripeOptions */
                "ApiKey": "secret"
              }
            }
            """));

        Assert.NotNull(root);
        Assert.True(root!.TryGetProperty("Stripe", out var stripe));
        Assert.True(stripe.Value.TryGetProperty("ApiKey", out _));
    }

    [Fact]
    public void Json_parser_keeps_unclosed_array_items()
    {
        var root = JsonConfigurationParser.Parse("appsettings.json", SourceText.From("""
            {
              "Servers": [
                {
                  "Host": "api"
                }
            }
            """));
        Assert.NotNull(root);

        Assert.True(root!.TryGetProperty("Servers", out var servers));
        var item = Assert.Single(servers.Value.Properties);
        Assert.Equal("0", item.Key);
        Assert.True(item.Value.TryGetProperty("Host", out _));
    }

    [Fact]
    public void Json_parser_preserves_duplicate_object_members()
    {
        var root = JsonConfigurationParser.Parse("appsettings.json", SourceText.From("""
            {
              "Features": {
                "Billing": {
                  "Enabled": true
                }
              },
              "Features": {
                "Stripe": {
                  "ApiKey": "secret"
                }
              }
            }
            """));

        Assert.NotNull(root);
        Assert.Collection(
            root!.Properties.Where(property => property.Key == "Features"),
            first => Assert.True(first.Value.TryGetProperty("Billing", out _)),
            second => Assert.True(second.Value.TryGetProperty("Stripe", out _)));
    }

    [Fact]
    public void Json_parser_marks_properties_when_cfg007_is_suppressed_for_that_file()
    {
        var root = JsonConfigurationParser.Parse(
            "appsettings.json",
            SourceText.From("""
                {
                  "Stripe": {
                    "WebookSecret": "typo"
                  }
                }
                """),
            strictUnknownConfigurationKeySuppressedByAnalyzerConfig: true);

        Assert.NotNull(root);
        Assert.True(root!.TryGetProperty("Stripe", out var stripe));
        Assert.True(stripe.StrictUnknownConfigurationKeySuppressedByAnalyzerConfig);
        Assert.True(stripe.Value.TryGetProperty("WebookSecret", out var typo));
        Assert.True(typo.StrictUnknownConfigurationKeySuppressedByAnalyzerConfig);
    }

    [Fact]
    public void Configuration_snapshot_tracks_cfg007_suppression_per_appsettings_file()
    {
        var snapshot = ConfigurationSnapshot.Create(
            [
                new TestAdditionalText("appsettings.json", """
                    {
                      "Stripe": {
                        "WebookSecret": "typo"
                      }
                    }
                    """),
                new TestAdditionalText("appsettings.Production.json", """
                    {
                      "Stripe": {
                        "WebookSecret": "typo"
                      }
                    }
                    """)
            ],
            file => file.Path == "appsettings.json",
            CancellationToken.None);

        var sections = snapshot.FindSections("Stripe");
        var typoProperties = sections
            .Select(section => section.TryGetProperty("WebookSecret", out var property) ? property : null)
            .Where(property => property is not null)
            .ToArray();

        Assert.Contains(typoProperties, property =>
            property!.StrictUnknownConfigurationKeySuppressedByAnalyzerConfig);
        Assert.Contains(typoProperties, property =>
            !property!.StrictUnknownConfigurationKeySuppressedByAnalyzerConfig);
    }

    [Fact]
    public void Json_parser_captures_string_scalar_kind_value_and_location()
    {
        var root = JsonConfigurationParser.Parse("appsettings.json", SourceText.From("""
            {
              "Stripe": {
                "ApiKey": "secret"
              }
            }
            """));

        Assert.NotNull(root);
        Assert.True(root!.TryGetProperty("Stripe", out var stripe));
        Assert.True(stripe.Value.TryGetProperty("ApiKey", out var apiKey));
        Assert.Equal(ScalarKind.String, apiKey.ScalarKind);
        Assert.Equal("secret", apiKey.ScalarValue);
        Assert.NotNull(apiKey.ValueLocation);
    }

    [Fact]
    public void Json_parser_captures_number_scalar_kind_and_raw_text()
    {
        var root = JsonConfigurationParser.Parse("appsettings.json", SourceText.From("""
            {
              "Server": {
                "Port": 8080
              }
            }
            """));

        Assert.NotNull(root);
        Assert.True(root!.TryGetProperty("Server", out var server));
        Assert.True(server.Value.TryGetProperty("Port", out var port));
        Assert.Equal(ScalarKind.Number, port.ScalarKind);
        Assert.Equal("8080", port.ScalarValue);
        Assert.NotNull(port.ValueLocation);
    }

    [Fact]
    public void Json_parser_captures_bool_scalar_kind_preserving_literal_casing()
    {
        var root = JsonConfigurationParser.Parse("appsettings.json", SourceText.From("""
            {
              "Server": {
                "Enabled": true
              }
            }
            """));

        Assert.NotNull(root);
        Assert.True(root!.TryGetProperty("Server", out var server));
        Assert.True(server.Value.TryGetProperty("Enabled", out var enabled));
        Assert.Equal(ScalarKind.Bool, enabled.ScalarKind);
        Assert.Equal("true", enabled.ScalarValue);
    }

    [Fact]
    public void Json_parser_captures_null_scalar_kind()
    {
        var root = JsonConfigurationParser.Parse("appsettings.json", SourceText.From("""
            {
              "Server": {
                "Port": null
              }
            }
            """));

        Assert.NotNull(root);
        Assert.True(root!.TryGetProperty("Server", out var server));
        Assert.True(server.Value.TryGetProperty("Port", out var port));
        Assert.Equal(ScalarKind.Null, port.ScalarKind);
        Assert.Equal("null", port.ScalarValue);
    }

    [Fact]
    public void Json_parser_marks_object_values_with_no_scalar_kind()
    {
        var root = JsonConfigurationParser.Parse("appsettings.json", SourceText.From("""
            {
              "Stripe": {
                "ApiKey": "secret"
              }
            }
            """));

        Assert.NotNull(root);
        Assert.True(root!.TryGetProperty("Stripe", out var stripe));
        Assert.Equal(ScalarKind.None, stripe.ScalarKind);
        Assert.Null(stripe.ScalarValue);
        Assert.Null(stripe.ValueLocation);
    }

    [Fact]
    public void Configuration_snapshot_models_net10_runtime_section_existence()
    {
        var snapshot = ConfigurationSnapshot.Create(
            [
                new TestAdditionalText("appsettings.json", """
                    {
                      "EmptyObject": {},
                      "NullValue": null,
                      "EmptyArray": [],
                      "Scalar": "value",
                      "ObjectWithChild": {
                        "Value": "present"
                      },
                      "ObjectWithNullChild": {
                        "Value": null
                      },
                      "ObjectWithEmptyObjectChild": {
                        "Value": {}
                      },
                      "Colon:EmptyChild": {}
                    }
                    """)
            ],
            _ => false,
            CancellationToken.None);

        Assert.Equal(
            ConfigurationSectionExistence.Missing,
            snapshot.GetSectionExistence("EmptyObject", ConfigurationProviderSemantics.Net10OrLater));
        Assert.Equal(
            ConfigurationSectionExistence.Missing,
            snapshot.GetSectionExistence("NullValue", ConfigurationProviderSemantics.Net10OrLater));
        Assert.Equal(
            ConfigurationSectionExistence.Exists,
            snapshot.GetSectionExistence("EmptyArray", ConfigurationProviderSemantics.Net10OrLater));
        Assert.Equal(
            ConfigurationSectionExistence.Exists,
            snapshot.GetSectionExistence("Scalar", ConfigurationProviderSemantics.Net10OrLater));
        Assert.Equal(
            ConfigurationSectionExistence.Exists,
            snapshot.GetSectionExistence("ObjectWithChild", ConfigurationProviderSemantics.Net10OrLater));
        Assert.Equal(
            ConfigurationSectionExistence.Exists,
            snapshot.GetSectionExistence("ObjectWithNullChild", ConfigurationProviderSemantics.Net10OrLater));
        Assert.Equal(
            ConfigurationSectionExistence.Exists,
            snapshot.GetSectionExistence("ObjectWithEmptyObjectChild", ConfigurationProviderSemantics.Net10OrLater));
        Assert.Equal(
            ConfigurationSectionExistence.Exists,
            snapshot.GetSectionExistence("Colon", ConfigurationProviderSemantics.Net10OrLater));
    }

    [Fact]
    public void Configuration_snapshot_keeps_version_sensitive_shapes_unknown_without_provider_evidence()
    {
        var snapshot = ConfigurationSnapshot.Create(
            [
                new TestAdditionalText("appsettings.json", """
                    {
                      "EmptyObject": {},
                      "NullValue": null,
                      "EmptyArray": []
                    }
                    """)
            ],
            _ => false,
            CancellationToken.None);

        Assert.Equal(
            ConfigurationSectionExistence.Missing,
            snapshot.GetSectionExistence("EmptyObject", ConfigurationProviderSemantics.Unknown));
        Assert.Equal(
            ConfigurationSectionExistence.Unknown,
            snapshot.GetSectionExistence("NullValue", ConfigurationProviderSemantics.Unknown));
        Assert.Equal(
            ConfigurationSectionExistence.Unknown,
            snapshot.GetSectionExistence("EmptyArray", ConfigurationProviderSemantics.Unknown));
        Assert.Equal(
            ConfigurationSectionExistence.Exists,
            snapshot.GetSectionExistence("NullValue", ConfigurationProviderSemantics.BeforeNet10));
        Assert.Equal(
            ConfigurationSectionExistence.Missing,
            snapshot.GetSectionExistence("EmptyArray", ConfigurationProviderSemantics.BeforeNet10));
    }

    private sealed class TestAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public TestAdditionalText(string path, string text)
        {
            Path = path;
            _text = SourceText.From(text);
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            return _text;
        }
    }
}
