using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ConfigContraband;

/// <summary>
/// A single options binding the schema should describe: the configuration section path it binds to,
/// the options type, and the binder flags that affect the emitted schema.
/// </summary>
internal sealed class SchemaSection
{
    public SchemaSection(string sectionPath, INamedTypeSymbol type, bool strict, bool bindsNonPublicProperties)
    {
        SectionPath = sectionPath;
        Type = type;
        Strict = strict;
        BindsNonPublicProperties = bindsNonPublicProperties;
    }

    public string SectionPath { get; }

    public INamedTypeSymbol Type { get; }

    public bool Strict { get; }

    public bool BindsNonPublicProperties { get; }
}

/// <summary>
/// Assembles the root <c>appsettings.schema.json</c> document from the set of discovered option
/// bindings, nesting each colon-delimited section path under <c>properties</c> and placing the
/// type schema at the leaf. The root stays open (no <c>additionalProperties</c>) because real
/// configuration files contain many keys ConfigContraband does not model (Logging, AllowedHosts, ...).
/// </summary>
internal static class SchemaDocumentBuilder
{
    public static JsonNode Build(IEnumerable<SchemaSection> sections, Compilation compilation)
    {
        var root = new SectionNode();

        foreach (var section in sections)
        {
            var node = root;
            foreach (var segment in section.SectionPath.Split(':'))
            {
                if (segment.Length == 0)
                {
                    continue;
                }

                node = node.GetOrAddChild(segment);
            }

            // Last binding for a given section wins; the typed schema is authoritative for that leaf.
            node.Schema = JsonSchemaBuilder.BuildObjectSchema(
                section.Type,
                compilation,
                section.Strict,
                section.BindsNonPublicProperties);
        }

        var document = JsonNode.Object();
        document.Add("$schema", JsonNode.Str("http://json-schema.org/draft-07/schema#"));
        document.Add("type", JsonNode.Str("object"));

        var properties = ToProperties(root);
        if (properties.HasMembers)
        {
            document.Add("properties", properties);
        }

        return document;
    }

    private static JsonNode ToJson(SectionNode node)
    {
        // A node bound to an explicit options type uses that type's schema verbatim.
        if (node.Schema is not null)
        {
            return node.Schema;
        }

        var schema = JsonNode.Object();
        schema.Add("type", JsonNode.Str("object"));

        var properties = ToProperties(node);
        if (properties.HasMembers)
        {
            schema.Add("properties", properties);
        }

        return schema;
    }

    private static JsonObject ToProperties(SectionNode node)
    {
        var properties = JsonNode.Object();
        foreach (var child in node.Children)
        {
            properties.Add(child.Key, ToJson(child.Value));
        }

        return properties;
    }

    private sealed class SectionNode
    {
        private readonly List<KeyValuePair<string, SectionNode>> _children = new();

        public IEnumerable<KeyValuePair<string, SectionNode>> Children => _children;

        public JsonNode? Schema { get; set; }

        public SectionNode GetOrAddChild(string segment)
        {
            foreach (var child in _children)
            {
                if (string.Equals(child.Key, segment, System.StringComparison.OrdinalIgnoreCase))
                {
                    return child.Value;
                }
            }

            var created = new SectionNode();
            _children.Add(new KeyValuePair<string, SectionNode>(segment, created));
            return created;
        }
    }
}
