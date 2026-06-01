using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ConfigContraband;

/// <summary>
/// A single options binding the schema should describe: the configuration section path it binds to,
/// the options type, and the binder/validation flags that affect the emitted schema.
/// </summary>
internal sealed class SchemaSection
{
    public SchemaSection(
        string sectionPath,
        INamedTypeSymbol type,
        bool strict,
        bool bindsNonPublicProperties,
        bool validatesDataAnnotations)
    {
        SectionPath = sectionPath;
        Type = type;
        Strict = strict;
        BindsNonPublicProperties = bindsNonPublicProperties;
        ValidatesDataAnnotations = validatesDataAnnotations;
    }

    public string SectionPath { get; }

    public INamedTypeSymbol Type { get; }

    public bool Strict { get; }

    public bool BindsNonPublicProperties { get; }

    /// <summary>
    /// Whether the registration enables DataAnnotations validation. Required-key constraints are only
    /// emitted when this is true, mirroring CFG002: <c>[Required]</c> does nothing without
    /// <c>ValidateDataAnnotations()</c>.
    /// </summary>
    public bool ValidatesDataAnnotations { get; }
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

            // Last binding for a given section path wins.
            node.Section = section;
        }

        var document = JsonNode.Object();
        document.Add("$schema", JsonNode.Str("http://json-schema.org/draft-07/schema#"));
        document.Add("type", JsonNode.Str("object"));

        var properties = ToProperties(root, compilation);
        if (properties.HasMembers)
        {
            document.Add("properties", properties);
        }

        return document;
    }

    private static JsonNode ToJson(SectionNode node, Compilation compilation)
    {
        if (node.Section is { } section)
        {
            var schema = JsonSchemaBuilder.BuildObjectSchema(section, compilation);

            // If sub-sections are also bound under this node (e.g. both "Features" and "Features:Stripe"),
            // fold those bindings into the type schema's properties so neither is dropped.
            if (node.HasChildren && schema is JsonObject obj)
            {
                var properties = obj.GetObject("properties");
                if (properties is null)
                {
                    properties = JsonNode.Object();
                    obj.Set("properties", properties);
                }

                foreach (var child in node.Children)
                {
                    properties.Set(child.Key, ToJson(child.Value, compilation));
                }
            }

            return schema;
        }

        var nested = JsonNode.Object();
        nested.Add("type", JsonNode.Str("object"));

        var nestedProperties = ToProperties(node, compilation);
        if (nestedProperties.HasMembers)
        {
            nested.Add("properties", nestedProperties);
        }

        return nested;
    }

    private static JsonObject ToProperties(SectionNode node, Compilation compilation)
    {
        var properties = JsonNode.Object();
        foreach (var child in node.Children)
        {
            properties.Add(child.Key, ToJson(child.Value, compilation));
        }

        return properties;
    }

    private sealed class SectionNode
    {
        private readonly List<KeyValuePair<string, SectionNode>> _children = new();

        public IEnumerable<KeyValuePair<string, SectionNode>> Children => _children;

        public bool HasChildren => _children.Count > 0;

        public SchemaSection? Section { get; set; }

        public SectionNode GetOrAddChild(string segment)
        {
            foreach (var child in _children)
            {
                if (string.Equals(child.Key, segment, StringComparison.OrdinalIgnoreCase))
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
