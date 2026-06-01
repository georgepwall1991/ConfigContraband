namespace ConfigContraband.Core.Tests;

public sealed class JsonNodeTests
{
    [Fact]
    public void Empty_object_and_array_are_compact()
    {
        Assert.Equal("{}", JsonNode.Object().ToJsonString());
        Assert.Equal("[]", JsonNode.Array().ToJsonString());
    }

    [Fact]
    public void Arrays_render_each_item_on_its_own_line()
    {
        var json = JsonNode.Array()
            .Add(JsonNode.Bool(true))
            .Add(JsonNode.Bool(false))
            .ToJsonString();

        Assert.Equal(
            """
            [
              true,
              false
            ]
            """,
            json,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Strings_escape_json_control_and_special_characters()
    {
        // Built from char codes so the test source stays free of backslash escapes.
        var quote = (char)34;
        var backslash = (char)92;
        var input = "q" + quote + "s" + backslash + "b" + (char)8 + "f" + (char)12 +
                    "n" + (char)10 + "r" + (char)13 + "t" + (char)9 + "c" + (char)1 + "z";

        var json = JsonNode.Object().Add("k", JsonNode.Str(input)).ToJsonString();

        var esc = backslash.ToString();
        Assert.Contains(esc + quote, json);
        Assert.Contains(esc + backslash, json);
        Assert.Contains(esc + "b", json);
        Assert.Contains(esc + "f", json);
        Assert.Contains(esc + "n", json);
        Assert.Contains(esc + "r", json);
        Assert.Contains(esc + "t", json);
        Assert.Contains(esc + "u0001", json);
    }
}
