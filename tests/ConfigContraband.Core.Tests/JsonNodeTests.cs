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

    [Fact]
    public void Number_nodes_render_their_literal_verbatim_without_quotes()
    {
        var json = JsonNode.Object()
            .Add("min", JsonNode.Number("1"))
            .Add("max", JsonNode.Number("65535"))
            .Add("rate", JsonNode.Number("1.5"))
            .ToJsonString();

        Assert.Equal(
            """
            {
              "min": 1,
              "max": 65535,
              "rate": 1.5
            }
            """,
            json,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void InsertAfter_places_a_new_member_immediately_after_the_anchor()
    {
        var json = JsonNode.Object()
            .Add("type", JsonNode.Str("integer"))
            .Add("minimum", JsonNode.Number("1"))
            .InsertAfter("type", "description", JsonNode.Str("A port."))
            .ToJsonString();

        Assert.Equal(
            """
            {
              "type": "integer",
              "description": "A port.",
              "minimum": 1
            }
            """,
            json,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void InsertAfter_replaces_an_existing_member_in_place()
    {
        var json = JsonNode.Object()
            .Add("type", JsonNode.Str("object"))
            .Add("description", JsonNode.Str("from type"))
            .Add("properties", JsonNode.Object())
            .InsertAfter("type", "description", JsonNode.Str("from property"))
            .ToJsonString();

        Assert.Equal(
            """
            {
              "type": "object",
              "description": "from property",
              "properties": {}
            }
            """,
            json,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void InsertAfter_appends_when_the_anchor_is_absent()
    {
        var json = JsonNode.Object()
            .InsertAfter("type", "description", JsonNode.Str("orphan"))
            .ToJsonString();

        Assert.Equal(
            """
            {
              "description": "orphan"
            }
            """,
            json,
            ignoreLineEndingDifferences: true);
    }
}
