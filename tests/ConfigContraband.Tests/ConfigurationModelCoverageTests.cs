using Microsoft.CodeAnalysis.Text;

namespace ConfigContraband.Tests;

public sealed class ConfigurationModelCoverageTests
{
    [Fact]
    public void Json_parser_decodes_supported_escapes()
    {
        var root = JsonConfigurationParser.Parse("appsettings.json", SourceText.From("""
            {
              "Escaped\"Quote\\Slash\/Back\bForm\fLine\nReturn\rTab\tOther\q": {
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
        Assert.Contains("Otherq", property.Key);
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
}
