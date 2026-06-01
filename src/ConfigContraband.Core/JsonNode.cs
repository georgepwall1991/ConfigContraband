using System.Collections.Generic;
using System.Text;

namespace ConfigContraband;

/// <summary>
/// Minimal, dependency-free, deterministic JSON model used to emit <c>appsettings.schema.json</c>.
/// Core ships inside the analyzer package, so it deliberately avoids a JSON serializer dependency
/// that could conflict with the Roslyn host. Output is pretty-printed with two-space indentation and
/// preserves insertion order so generated schemas diff cleanly.
/// </summary>
internal abstract class JsonNode
{
    public static JsonObject Object() => new();

    public static JsonArray Array() => new();

    public static JsonNode Str(string value) => new JsonStringNode(value);

    public static JsonNode Bool(bool value) => new JsonBoolNode(value);

    public string ToJsonString()
    {
        var builder = new StringBuilder();
        Write(builder, 0);
        return builder.ToString();
    }

    internal abstract void Write(StringBuilder builder, int indentLevel);

    private protected static void AppendIndent(StringBuilder builder, int indentLevel)
    {
        builder.Append(' ', indentLevel * 2);
    }

    private protected static void AppendString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (ch < ' ')
                    {
                        builder.Append("\\u");
                        builder.Append(((int)ch).ToString("x4"));
                    }
                    else
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}

internal sealed class JsonObject : JsonNode
{
    private readonly List<KeyValuePair<string, JsonNode>> _members = new();

    public bool HasMembers => _members.Count > 0;

    public JsonObject Add(string key, JsonNode value)
    {
        _members.Add(new KeyValuePair<string, JsonNode>(key, value));
        return this;
    }

    internal override void Write(StringBuilder builder, int indentLevel)
    {
        if (_members.Count == 0)
        {
            builder.Append("{}");
            return;
        }

        builder.Append('{');
        for (var i = 0; i < _members.Count; i++)
        {
            builder.Append('\n');
            AppendIndent(builder, indentLevel + 1);
            AppendString(builder, _members[i].Key);
            builder.Append(": ");
            _members[i].Value.Write(builder, indentLevel + 1);
            if (i < _members.Count - 1)
            {
                builder.Append(',');
            }
        }

        builder.Append('\n');
        AppendIndent(builder, indentLevel);
        builder.Append('}');
    }
}

internal sealed class JsonArray : JsonNode
{
    private readonly List<JsonNode> _items = new();

    public JsonArray Add(JsonNode value)
    {
        _items.Add(value);
        return this;
    }

    internal override void Write(StringBuilder builder, int indentLevel)
    {
        if (_items.Count == 0)
        {
            builder.Append("[]");
            return;
        }

        builder.Append('[');
        for (var i = 0; i < _items.Count; i++)
        {
            builder.Append('\n');
            AppendIndent(builder, indentLevel + 1);
            _items[i].Write(builder, indentLevel + 1);
            if (i < _items.Count - 1)
            {
                builder.Append(',');
            }
        }

        builder.Append('\n');
        AppendIndent(builder, indentLevel);
        builder.Append(']');
    }
}

internal sealed class JsonStringNode : JsonNode
{
    private readonly string _value;

    public JsonStringNode(string value)
    {
        _value = value;
    }

    internal override void Write(StringBuilder builder, int indentLevel)
    {
        AppendString(builder, _value);
    }
}

internal sealed class JsonBoolNode : JsonNode
{
    private readonly bool _value;

    public JsonBoolNode(bool value)
    {
        _value = value;
    }

    internal override void Write(StringBuilder builder, int indentLevel)
    {
        builder.Append(_value ? "true" : "false");
    }
}
