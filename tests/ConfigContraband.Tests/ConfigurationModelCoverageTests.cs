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
}
