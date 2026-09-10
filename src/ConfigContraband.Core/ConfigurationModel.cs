using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ConfigContraband;

internal sealed class ConfigurationSnapshot
{
    private readonly ImmutableArray<ConfigurationFile> _files;
    private readonly ImmutableArray<RejectedConfigurationFile> _rejectedFiles;

    private ConfigurationSnapshot(
        ImmutableArray<ConfigurationFile> files,
        ImmutableArray<RejectedConfigurationFile> rejectedFiles)
    {
        _files = files;
        _rejectedFiles = rejectedFiles;
    }

    public bool HasFiles => !_files.IsDefaultOrEmpty;

    public ImmutableArray<RejectedConfigurationFile> RejectedFiles => _rejectedFiles;

    public static ConfigurationSnapshot Create(
        ImmutableArray<AdditionalText> additionalFiles,
        Func<AdditionalText, bool> isStrictUnknownConfigurationKeySuppressed,
        System.Threading.CancellationToken cancellationToken)
    {
        var builder = ImmutableArray.CreateBuilder<ConfigurationFile>();
        var rejectedBuilder = ImmutableArray.CreateBuilder<RejectedConfigurationFile>();

        foreach (var file in additionalFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsAppSettingsFile(file.Path))
            {
                continue;
            }

            var text = file.GetText(cancellationToken);
            if (text is null)
            {
                continue;
            }

            var result = JsonConfigurationParser.ParseDetailed(
                file.Path,
                text,
                isStrictUnknownConfigurationKeySuppressed(file));
            if (result.Rejection is { } rejection)
            {
                rejectedBuilder.Add(new RejectedConfigurationFile(file, rejection));
            }
            else if (result.Root is { } root)
            {
                builder.Add(new ConfigurationFile(file.Path, root));
            }
        }

        return new ConfigurationSnapshot(builder.ToImmutable(), rejectedBuilder.ToImmutable());
    }

    public ImmutableArray<string> GetSiblingSectionNames(string sectionPath)
    {
        var pathParts = SplitPath(sectionPath);
        if (pathParts.Length <= 1)
        {
            return _files
                .SelectMany(file => EnumerateProperties(file.Root))
                .SelectMany(property => SplitPath(property.FullPath).Take(1))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
        }

        var parentPathParts = pathParts.Take(pathParts.Length - 1).ToArray();
        return _files
            .SelectMany(file => EnumerateProperties(file.Root))
            .SelectMany(property => TryGetChildPathPart(property.FullPath, parentPathParts, out var childPart)
                ? new[] { childPart }
                : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    public bool TryFindSection(string sectionPath, out ConfigurationNode section)
    {
        var sections = FindSections(sectionPath);
        if (!sections.IsDefaultOrEmpty)
        {
            section = sections[0];
            return true;
        }

        section = ConfigurationNode.Empty;
        return false;
    }

    public ConfigurationSectionExistence GetSectionExistence(
        string sectionPath,
        ConfigurationProviderSemantics providerSemantics)
    {
        var result = ConfigurationSectionExistence.Missing;
        foreach (var section in FindSections(sectionPath))
        {
            var candidate = GetNodeExistence(section, providerSemantics);
            if (candidate == ConfigurationSectionExistence.Exists)
            {
                return candidate;
            }

            if (candidate == ConfigurationSectionExistence.Unknown)
            {
                result = candidate;
            }
        }

        return result;
    }

    private static ConfigurationSectionExistence GetNodeExistence(
        ConfigurationNode node,
        ConfigurationProviderSemantics providerSemantics)
    {
        switch (node.Kind)
        {
            case ConfigurationNodeKind.Scalar:
                return ConfigurationSectionExistence.Exists;
            case ConfigurationNodeKind.Null:
                return providerSemantics switch
                {
                    ConfigurationProviderSemantics.BeforeNet10 => ConfigurationSectionExistence.Exists,
                    ConfigurationProviderSemantics.Net10OrLater => ConfigurationSectionExistence.Missing,
                    _ => ConfigurationSectionExistence.Unknown,
                };
            case ConfigurationNodeKind.Array when node.Properties.IsDefaultOrEmpty:
                return providerSemantics switch
                {
                    ConfigurationProviderSemantics.BeforeNet10 => ConfigurationSectionExistence.Missing,
                    ConfigurationProviderSemantics.Net10OrLater => ConfigurationSectionExistence.Exists,
                    _ => ConfigurationSectionExistence.Unknown,
                };
        }

        // IConfigurationSection.Exists() treats any child key as proof that the
        // parent exists. The child does not itself need to exist: JSON providers
        // still expose a child key for null, empty-object, and empty-array values.
        return node.Properties.IsDefaultOrEmpty
            ? ConfigurationSectionExistence.Missing
            : ConfigurationSectionExistence.Exists;
    }

    public ImmutableArray<ConfigurationNode> FindSections(string sectionPath)
    {
        var builder = ImmutableArray.CreateBuilder<ConfigurationNode>();

        foreach (var file in _files)
        {
            builder.AddRange(FindSections(file.Root, sectionPath));
        }

        return builder.ToImmutable();
    }

    public ImmutableArray<ConfigurationProperty> FindProperties(string fullPath)
    {
        return _files
            .SelectMany(file => EnumerateProperties(file.Root))
            .Where(property => string.Equals(property.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
            .ToImmutableArray();
    }

    private static ImmutableArray<ConfigurationNode> FindSections(ConfigurationNode root, string sectionPath)
    {
        var builder = ImmutableArray.CreateBuilder<ConfigurationNode>();
        var pathParts = SplitPath(sectionPath);
        FindSections(root, pathParts, partIndex: 0, builder);

        var projected = ProjectSection(root, pathParts);
        if (!projected.Properties.IsDefaultOrEmpty)
        {
            builder.Add(projected);
        }

        return builder.ToImmutable();
    }

    private static void FindSections(
        ConfigurationNode current,
        string[] pathParts,
        int partIndex,
        ImmutableArray<ConfigurationNode>.Builder builder)
    {
        if (partIndex >= pathParts.Length)
        {
            builder.Add(current);
            return;
        }

        foreach (var property in current.Properties)
        {
            var keyParts = SplitPath(property.Key);
            if (PathMatchesAt(pathParts, partIndex, keyParts))
            {
                var nextPartIndex = partIndex + keyParts.Length;
                if (nextPartIndex >= pathParts.Length)
                {
                    builder.Add(property.Value);
                    continue;
                }

                FindSections(property.Value, pathParts, nextPartIndex, builder);
            }
        }
    }

    private static ConfigurationNode ProjectSection(ConfigurationNode root, string[] sectionPathParts)
    {
        var properties = ImmutableArray.CreateBuilder<ConfigurationProperty>();
        foreach (var property in EnumerateProperties(root))
        {
            var propertyPathParts = SplitPath(property.FullPath);
            if (propertyPathParts.Length <= sectionPathParts.Length ||
                !PathMatchesAt(propertyPathParts, 0, sectionPathParts))
            {
                continue;
            }

            AddProjectedProperty(
                properties,
                CreateProjectedProperty(
                    propertyPathParts,
                    sectionPathParts.Length,
                    string.Join(":", propertyPathParts.Take(sectionPathParts.Length)),
                    property));
        }

        return new ConfigurationNode(properties.ToImmutable());
    }

    private static void AddProjectedProperty(
        ImmutableArray<ConfigurationProperty>.Builder properties,
        ConfigurationProperty property)
    {
        for (var i = 0; i < properties.Count; i++)
        {
            if (!string.Equals(properties[i].Key, property.Key, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(properties[i].FullPath, property.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            properties[i] = MergeProjectedProperties(properties[i], property);
            return;
        }

        properties.Add(property);
    }

    private static ConfigurationProperty MergeProjectedProperties(
        ConfigurationProperty existing,
        ConfigurationProperty incoming)
    {
        if (existing.Value.Properties.IsDefaultOrEmpty)
        {
            return incoming.Value.Properties.IsDefaultOrEmpty ? existing : incoming;
        }

        if (incoming.Value.Properties.IsDefaultOrEmpty)
        {
            return existing;
        }

        var properties = existing.Value.Properties.ToBuilder();
        foreach (var property in incoming.Value.Properties)
        {
            AddProjectedProperty(properties, property);
        }

        return new ConfigurationProperty(
            existing.Key,
            existing.FullPath,
            new ConfigurationNode(properties.ToImmutable()),
            existing.Location,
            existing.StrictUnknownConfigurationKeySuppressedByAnalyzerConfig);
    }

    private static ConfigurationProperty CreateProjectedProperty(
        string[] propertyPathParts,
        int partIndex,
        string parentPath,
        ConfigurationProperty source)
    {
        var key = propertyPathParts[partIndex];
        var fullPath = string.IsNullOrEmpty(parentPath) ? key : parentPath + ":" + key;
        var isLeaf = partIndex == propertyPathParts.Length - 1;
        var value = isLeaf
            ? source.Value
            : new ConfigurationNode(ImmutableArray.Create(CreateProjectedProperty(
                propertyPathParts,
                partIndex + 1,
                fullPath,
                source)));

        return new ConfigurationProperty(
            key,
            fullPath,
            value,
            source.Location,
            source.StrictUnknownConfigurationKeySuppressedByAnalyzerConfig,
            isLeaf ? source.ScalarKind : ScalarKind.None,
            isLeaf ? source.ScalarValue : null,
            isLeaf ? source.ValueLocation : null);
    }

    private static bool TryGetChildPathPart(string fullPath, string[] parentPathParts, out string childPart)
    {
        var pathParts = SplitPath(fullPath);
        if (pathParts.Length > parentPathParts.Length &&
            PathMatchesAt(pathParts, 0, parentPathParts))
        {
            childPart = pathParts[parentPathParts.Length];
            return true;
        }

        childPart = null!;
        return false;
    }

    private static bool PathMatchesAt(string[] pathParts, int partIndex, string[] candidateParts)
    {
        if (candidateParts.Length == 0 ||
            partIndex + candidateParts.Length > pathParts.Length)
        {
            return false;
        }

        for (var i = 0; i < candidateParts.Length; i++)
        {
            if (!string.Equals(pathParts[partIndex + i], candidateParts[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<ConfigurationProperty> EnumerateProperties(ConfigurationNode node)
    {
        foreach (var property in node.Properties)
        {
            yield return property;

            foreach (var child in EnumerateProperties(property.Value))
            {
                yield return child;
            }
        }
    }

    private static readonly char[] PathSeparator = { ':' };

    private static string[] SplitPath(string sectionPath)
    {
        return sectionPath.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool IsAppSettingsFile(string path)
    {
        var fileName = System.IO.Path.GetFileName(path);
        return (string.Equals(fileName, "appsettings.json", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase)) &&
               fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class ConfigurationFile
{
    public ConfigurationFile(string path, ConfigurationNode root)
    {
        Path = path;
        Root = root;
    }

    public string Path { get; }
    public ConfigurationNode Root { get; }
}

internal enum ConfigurationFileRejectionKind
{
    /// <summary>The file's text is not valid JSON under the runtime provider's tolerant reader
    /// (comments and trailing commas allowed) — syntax errors, unterminated constructs, invalid
    /// escapes, invalid scalar tokens, or trailing content after the root object.</summary>
    InvalidSyntax,

    /// <summary>The file parses as JSON but the root element is not an object.</summary>
    NonObjectRoot,

    /// <summary>A scalar or null value repeats a case-insensitive flattened path already written
    /// by a scalar, null, or empty container.</summary>
    DuplicateKey,

    /// <summary>Nesting exceeds the runtime provider's maximum JSON depth.</summary>
    DepthExceeded,
}

/// <summary>
/// A reason the runtime JSON configuration provider throws <see cref="System.FormatException"/>
/// when loading a visible appsettings file, with the location of the offending content.
/// </summary>
internal sealed class ConfigurationFileRejection
{
    public ConfigurationFileRejection(
        ConfigurationFileRejectionKind kind,
        Location location,
        string? duplicateKey = null)
    {
        Kind = kind;
        Location = location;
        DuplicateKey = duplicateKey;
    }

    public ConfigurationFileRejectionKind Kind { get; }
    public Location Location { get; }

    /// <summary>The flattened configuration path of the repeated key, when <see cref="Kind"/> is
    /// <see cref="ConfigurationFileRejectionKind.DuplicateKey"/>.</summary>
    public string? DuplicateKey { get; }
}

/// <summary>
/// A visible appsettings file the runtime JSON provider rejects on load, with the rejection detail.
/// </summary>
internal sealed class RejectedConfigurationFile
{
    public RejectedConfigurationFile(AdditionalText file, ConfigurationFileRejection rejection)
    {
        File = file;
        Rejection = rejection;
    }

    public AdditionalText File { get; }
    public ConfigurationFileRejection Rejection { get; }
}

internal sealed class ConfigurationFileParseResult
{
    public ConfigurationFileParseResult(ConfigurationNode? root, ConfigurationFileRejection? rejection)
    {
        Root = root;
        Rejection = rejection;
    }

    /// <summary>The parsed root node, or <c>null</c> when the file was rejected.</summary>
    public ConfigurationNode? Root { get; }

    /// <summary>The runtime-load rejection, or <c>null</c> when the file parses cleanly.</summary>
    public ConfigurationFileRejection? Rejection { get; }
}

internal sealed class ConfigurationNode
{
    public static readonly ConfigurationNode Empty = new(
        ImmutableArray<ConfigurationProperty>.Empty,
        ConfigurationNodeKind.Scalar);
    public static readonly ConfigurationNode Null = new(
        ImmutableArray<ConfigurationProperty>.Empty,
        ConfigurationNodeKind.Null);

    public ConfigurationNode(
        ImmutableArray<ConfigurationProperty> properties,
        ConfigurationNodeKind kind = ConfigurationNodeKind.Object)
    {
        Properties = properties;
        Kind = kind;
    }

    public ImmutableArray<ConfigurationProperty> Properties { get; }
    public ConfigurationNodeKind Kind { get; }
    public bool IsObject => Kind == ConfigurationNodeKind.Object;

    public bool TryGetProperty(string key, out ConfigurationProperty property)
    {
        foreach (var candidate in Properties)
        {
            if (string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate;
                return true;
            }
        }

        property = null!;
        return false;
    }
}

internal enum ConfigurationNodeKind
{
    Scalar,
    Null,
    Object,
    Array,
}

internal enum ConfigurationProviderSemantics
{
    Unknown,
    BeforeNet10,
    Net10OrLater,
}

internal enum ConfigurationSectionExistence
{
    Missing,
    Unknown,
    Exists,
}

internal enum ScalarKind
{
    /// <summary>The value is not a scalar (object or array; malformed files are rejected).</summary>
    None,
    String,
    Number,
    Bool,
    Null,
}

internal sealed class ConfigurationProperty
{
    public ConfigurationProperty(
        string key,
        string fullPath,
        ConfigurationNode value,
        Location location,
        bool strictUnknownConfigurationKeySuppressedByAnalyzerConfig,
        ScalarKind scalarKind = ScalarKind.None,
        string? scalarValue = null,
        Location? valueLocation = null)
    {
        Key = key;
        FullPath = fullPath;
        Value = value;
        Location = location;
        StrictUnknownConfigurationKeySuppressedByAnalyzerConfig = strictUnknownConfigurationKeySuppressedByAnalyzerConfig;
        ScalarKind = scalarKind;
        ScalarValue = scalarValue;
        ValueLocation = valueLocation;
    }

    public string Key { get; }
    public string FullPath { get; }
    public ConfigurationNode Value { get; }

    /// <summary>Location of the property key.</summary>
    public Location Location { get; }
    public bool StrictUnknownConfigurationKeySuppressedByAnalyzerConfig { get; }

    /// <summary>Kind of the property's scalar value, or <see cref="ScalarKind.None"/> for object/array values.</summary>
    public ScalarKind ScalarKind { get; }

    /// <summary>The property's scalar value as text (decoded for strings; the literal for other scalars), or <c>null</c> for non-scalar values.</summary>
    public string? ScalarValue { get; }

    /// <summary>Location of the property's scalar value, or <c>null</c> for non-scalar values.</summary>
    public Location? ValueLocation { get; }

    /// <summary>
    /// Prefers the scalar value span when the parser captured one; otherwise the property key.
    /// </summary>
    public Location GetReportedLocation()
    {
        if (ValueLocation is { } valueLocation)
        {
            return valueLocation;
        }

        return Location;
    }
}

internal static class JsonConfigurationParser
{
    public static ConfigurationNode? Parse(string path, SourceText text)
    {
        return Parse(
            path,
            text,
            strictUnknownConfigurationKeySuppressedByAnalyzerConfig: false);
    }

    public static ConfigurationNode? Parse(
        string path,
        SourceText text,
        bool strictUnknownConfigurationKeySuppressedByAnalyzerConfig)
    {
        return ParseDetailed(path, text, strictUnknownConfigurationKeySuppressedByAnalyzerConfig).Root;
    }

    /// <summary>
    /// Parses a visible appsettings file. The result carries either the parsed root node or the
    /// first reason the runtime JSON provider would reject the file on load, matching the tolerant
    /// <c>JsonDocument</c> reader the provider uses (comments and trailing commas allowed, default
    /// maximum depth, strict string/scalar grammar, case-insensitive duplicate flattened scalar
    /// paths rejected).
    /// </summary>
    public static ConfigurationFileParseResult ParseDetailed(
        string path,
        SourceText text,
        bool strictUnknownConfigurationKeySuppressedByAnalyzerConfig)
    {
        var parser = new Parser(path, text, strictUnknownConfigurationKeySuppressedByAnalyzerConfig);
        try
        {
            return new ConfigurationFileParseResult(parser.ParseRoot(), rejection: null);
        }
        catch (ParseRejectedException rejected)
        {
            return new ConfigurationFileParseResult(root: null, rejected.Rejection);
        }
    }

    private sealed class Parser
    {
        // Guards against an uncatchable StackOverflowException on a pathologically
        // nested appsettings file: appsettings arrives as arbitrary AdditionalText, so
        // the parser must survive any input. Matches System.Text.Json's default ceiling.
        private const int MaxDepth = 64;

        private readonly string _path;
        private readonly SourceText _text;
        private readonly bool _strictUnknownConfigurationKeySuppressedByAnalyzerConfig;
        private int _position;

        public Parser(
            string path,
            SourceText text,
            bool strictUnknownConfigurationKeySuppressedByAnalyzerConfig)
        {
            _path = path;
            _text = text;
            _strictUnknownConfigurationKeySuppressedByAnalyzerConfig =
                strictUnknownConfigurationKeySuppressedByAnalyzerConfig;
        }

        public ConfigurationNode ParseRoot()
        {
            // A physical UTF-8 BOM never reaches the parser: Roslyn decodes the file and strips
            // it. A literal U+FEFF inside the decoded text is the second BOM of a double-BOM
            // file, which the runtime provider rejects — it is not JSON whitespace.
            SkipWhitespace();
            if (Current != '{')
            {
                var rootStart = _position;
                _ = ParseValue(string.Empty, depth: 1);
                SkipWhitespace();
                if (!IsEnd)
                {
                    throw Rejection(ConfigurationFileRejectionKind.InvalidSyntax, _position);
                }

                throw Rejection(ConfigurationFileRejectionKind.NonObjectRoot, rootStart);
            }

            // The runtime reader counts the root object as depth one of its default 64-deep ceiling.
            var root = ParseObject(parentPath: string.Empty, depth: 1);
            SkipWhitespace();
            if (!IsEnd)
            {
                throw Rejection(ConfigurationFileRejectionKind.InvalidSyntax, _position);
            }

            var overwrittenScalarPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (HasDuplicateRuntimePath(
                    root,
                    parentRuntimePath: null,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    overwrittenScalarPaths,
                    out var offendingProperty))
            {
                throw new ParseRejectedException(new ConfigurationFileRejection(
                    ConfigurationFileRejectionKind.DuplicateKey,
                    offendingProperty!.Location,
                    offendingProperty.FullPath));
            }

            return overwrittenScalarPaths.Count == 0
                ? root
                : RemoveOverwrittenScalars(root, parentRuntimePath: null, overwrittenScalarPaths);
        }

        private static bool HasDuplicateRuntimePath(
            ConfigurationNode node,
            string? parentRuntimePath,
            HashSet<string> runtimePaths,
            HashSet<string> overwrittenScalarPaths,
            out ConfigurationProperty? offendingProperty)
        {
            offendingProperty = null;
            foreach (var property in node.Properties)
            {
                var runtimePath = CreateRuntimePath(parentRuntimePath, property.Key);
                if (property.ScalarKind != ScalarKind.None &&
                    !runtimePaths.Add(runtimePath))
                {
                    offendingProperty = property;
                    return true;
                }

                if (HasDuplicateRuntimePath(
                        property.Value,
                        runtimePath,
                        runtimePaths,
                        overwrittenScalarPaths,
                        out offendingProperty))
                {
                    return true;
                }

                if (property.ScalarKind == ScalarKind.None &&
                    property.Value.Properties.IsDefaultOrEmpty &&
                    (property.Value.Kind == ConfigurationNodeKind.Object ||
                     property.Value.Kind == ConfigurationNodeKind.Array))
                {
                    // Empty containers write their path through the runtime
                    // provider's dictionary indexer. They overwrite an earlier
                    // value, but make a later scalar at the same path invalid.
                    if (!runtimePaths.Add(runtimePath))
                    {
                        overwrittenScalarPaths.Add(runtimePath);
                    }
                }
            }

            return false;
        }

        private static ConfigurationNode RemoveOverwrittenScalars(
            ConfigurationNode node,
            string? parentRuntimePath,
            HashSet<string> overwrittenScalarPaths)
        {
            var properties = ImmutableArray.CreateBuilder<ConfigurationProperty>(node.Properties.Length);
            var changed = false;

            foreach (var property in node.Properties)
            {
                var runtimePath = CreateRuntimePath(parentRuntimePath, property.Key);
                if (property.ScalarKind != ScalarKind.None &&
                    overwrittenScalarPaths.Contains(runtimePath))
                {
                    changed = true;
                    continue;
                }

                var value = RemoveOverwrittenScalars(
                    property.Value,
                    runtimePath,
                    overwrittenScalarPaths);
                if (ReferenceEquals(value, property.Value))
                {
                    properties.Add(property);
                    continue;
                }

                changed = true;
                properties.Add(new ConfigurationProperty(
                    property.Key,
                    property.FullPath,
                    value,
                    property.Location,
                    property.StrictUnknownConfigurationKeySuppressedByAnalyzerConfig,
                    property.ScalarKind,
                    property.ScalarValue,
                    property.ValueLocation));
            }

            return changed
                ? new ConfigurationNode(properties.ToImmutable(), node.Kind)
                : node;
        }

        private static string CreateRuntimePath(string? parentRuntimePath, string key)
        {
            // Null means the root. An empty string is a real JSON path segment
            // and must retain the leading delimiter for its descendants.
            return parentRuntimePath is null ? key : parentRuntimePath + ":" + key;
        }

        private ConfigurationNode ParseObject(string parentPath, int depth)
        {
            var objectStart = _position;
            var properties = ImmutableArray.CreateBuilder<ConfigurationProperty>();
            Read('{');

            while (true)
            {
                SkipWhitespace();
                if (Current == '}')
                {
                    Read('}');
                    return new ConfigurationNode(properties.ToImmutable());
                }

                if (IsEnd)
                {
                    throw Rejection(ConfigurationFileRejectionKind.InvalidSyntax, objectStart);
                }

                if (Current != '"')
                {
                    throw Rejection(ConfigurationFileRejectionKind.InvalidSyntax, _position);
                }

                var keyStart = _position;
                var key = ParseString();
                var keyEnd = _position;
                var fullPath = string.IsNullOrEmpty(parentPath) ? key : parentPath + ":" + key;

                SkipWhitespace();
                if (Current != ':')
                {
                    throw Rejection(ConfigurationFileRejectionKind.InvalidSyntax, _position);
                }

                Read(':');

                var parsed = ParseValue(fullPath, depth);
                properties.Add(new ConfigurationProperty(
                    key,
                    fullPath,
                    parsed.Node,
                    CreateLocation(TextSpan.FromBounds(keyStart, keyEnd)),
                    _strictUnknownConfigurationKeySuppressedByAnalyzerConfig,
                    parsed.Kind,
                    parsed.Raw,
                    parsed.Kind == ScalarKind.None ? null : CreateLocation(parsed.ValueSpan)));

                SkipWhitespace();
                if (Current == ',')
                {
                    Read(',');
                    continue;
                }

                if (IsEnd)
                {
                    throw Rejection(ConfigurationFileRejectionKind.InvalidSyntax, objectStart);
                }

                if (Current != '}')
                {
                    throw Rejection(ConfigurationFileRejectionKind.InvalidSyntax, _position);
                }
            }
        }

        private readonly struct ParsedValue
        {
            public ParsedValue(ConfigurationNode node, ScalarKind kind, string? raw, TextSpan valueSpan)
            {
                Node = node;
                Kind = kind;
                Raw = raw;
                ValueSpan = valueSpan;
            }

            public ConfigurationNode Node { get; }
            public ScalarKind Kind { get; }
            public string? Raw { get; }
            public TextSpan ValueSpan { get; }
        }

        private ParsedValue ParseValue(string path, int depth)
        {
            SkipWhitespace();
            if (Current == '{')
            {
                if (depth >= MaxDepth)
                {
                    throw Rejection(ConfigurationFileRejectionKind.DepthExceeded, _position);
                }

                return new ParsedValue(ParseObject(path, depth + 1), ScalarKind.None, raw: null, default);
            }

            if (Current == '[')
            {
                if (depth >= MaxDepth)
                {
                    throw Rejection(ConfigurationFileRejectionKind.DepthExceeded, _position);
                }

                return new ParsedValue(ParseArray(path, depth + 1), ScalarKind.None, raw: null, default);
            }

            if (Current == '"')
            {
                var stringStart = _position;
                var decoded = ParseString();
                return new ParsedValue(
                    ConfigurationNode.Empty,
                    ScalarKind.String,
                    decoded,
                    TextSpan.FromBounds(stringStart, Math.Min(_position, _text.Length)));
            }

            var scalarStart = _position;
            var token = ReadScalarToken();
            if (!IsValidScalarToken(token))
            {
                throw Rejection(ConfigurationFileRejectionKind.InvalidSyntax, scalarStart);
            }

            var scalarSpan = TextSpan.FromBounds(scalarStart, _position);
            var kind = token switch
            {
                "true" or "false" => ScalarKind.Bool,
                "null" => ScalarKind.Null,
                _ => ScalarKind.Number,
            };
            return new ParsedValue(
                kind == ScalarKind.Null ? ConfigurationNode.Null : ConfigurationNode.Empty,
                kind,
                token,
                scalarSpan);
        }

        /// <summary>
        /// Reads one unquoted scalar token, stopping at whitespace, a container/scalar delimiter,
        /// or the start of a comment. Anything the tolerant runtime reader would reject is left for
        /// <see cref="IsValidScalarToken"/> to check.
        /// </summary>
        private string ReadScalarToken()
        {
            var start = _position;
            while (!IsEnd)
            {
                var current = Current;
                if (IsJsonWhitespace(current) || current == ',' || current == '}' || current == ']')
                {
                    break;
                }

                if (current == '/' && (Peek(1) == '/' || Peek(1) == '*'))
                {
                    break;
                }

                _position++;
            }

            return _text.ToString(TextSpan.FromBounds(start, _position));
        }

        /// <summary>
        /// The exact scalar grammar the runtime JSON reader accepts: lowercase
        /// <c>true</c>/<c>false</c>/<c>null</c>, or an RFC 8259 number.
        /// </summary>
        private static bool IsValidScalarToken(string token)
        {
            if (token is "true" or "false" or "null")
            {
                return true;
            }

            return IsValidJsonNumber(token);
        }

        private static bool IsValidJsonNumber(string token)
        {
            var index = 0;
            if (index < token.Length && token[index] == '-')
            {
                index++;
            }

            if (index >= token.Length)
            {
                return false;
            }

            if (token[index] == '0')
            {
                index++;
            }
            else if (token[index] >= '1' && token[index] <= '9')
            {
                while (index < token.Length && IsDigit(token[index]))
                {
                    index++;
                }
            }
            else
            {
                return false;
            }

            if (index < token.Length && token[index] == '.')
            {
                index++;
                var fractionStart = index;
                while (index < token.Length && IsDigit(token[index]))
                {
                    index++;
                }

                if (index == fractionStart)
                {
                    return false;
                }
            }

            if (index < token.Length && (token[index] == 'e' || token[index] == 'E'))
            {
                index++;
                if (index < token.Length && (token[index] == '+' || token[index] == '-'))
                {
                    index++;
                }

                var exponentStart = index;
                while (index < token.Length && IsDigit(token[index]))
                {
                    index++;
                }

                if (index == exponentStart)
                {
                    return false;
                }
            }

            return index == token.Length;
        }

        private static bool IsDigit(char value)
        {
            return value >= '0' && value <= '9';
        }

        private ConfigurationNode ParseArray(string path, int depth)
        {
            var arrayStart = _position;
            var properties = ImmutableArray.CreateBuilder<ConfigurationProperty>();
            var index = 0;

            Read('[');

            while (true)
            {
                SkipWhitespace();
                if (Current == ']')
                {
                    Read(']');
                    return new ConfigurationNode(properties.ToImmutable(), ConfigurationNodeKind.Array);
                }

                if (IsEnd)
                {
                    throw Rejection(ConfigurationFileRejectionKind.InvalidSyntax, arrayStart);
                }

                var itemStart = _position;
                var itemKey = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var itemPath = path + ":" + itemKey;
                var parsed = ParseValue(itemPath, depth);
                properties.Add(new ConfigurationProperty(
                    itemKey,
                    itemPath,
                    parsed.Node,
                    CreateLocation(TextSpan.FromBounds(itemStart, Math.Min(_position, _text.Length))),
                    _strictUnknownConfigurationKeySuppressedByAnalyzerConfig,
                    parsed.Kind,
                    parsed.Raw,
                    parsed.Kind == ScalarKind.None ? null : CreateLocation(parsed.ValueSpan)));
                index++;

                SkipWhitespace();
                if (Current == ',')
                {
                    Read(',');
                    continue;
                }

                if (IsEnd)
                {
                    throw Rejection(ConfigurationFileRejectionKind.InvalidSyntax, arrayStart);
                }

                if (Current != ']')
                {
                    throw Rejection(ConfigurationFileRejectionKind.InvalidSyntax, _position);
                }
            }
        }

        private string ParseString()
        {
            var stringStart = _position;
            Read('"');
            var chars = new List<char>();

            while (!IsEnd)
            {
                var ch = Current;
                _position++;

                if (ch == '"')
                {
                    return new string(chars.ToArray());
                }

                if (ch < 0x20)
                {
                    throw Rejection(ConfigurationFileRejectionKind.InvalidSyntax, _position - 1);
                }

                if (ch != '\\')
                {
                    chars.Add(ch);
                    continue;
                }

                if (IsEnd)
                {
                    throw Rejection(ConfigurationFileRejectionKind.InvalidSyntax, stringStart);
                }

                var escaped = Current;
                _position++;
                if (escaped == 'u')
                {
                    if (!TryReadUnicodeEscape(out var unicodeChar))
                    {
                        throw Rejection(ConfigurationFileRejectionKind.InvalidSyntax, _position - 2);
                    }

                    if (char.IsHighSurrogate(unicodeChar))
                    {
                        // A high surrogate is only legal when immediately followed by a
                        // \uDC00-\uDFFF escape — the provider's string materialization throws
                        // on unpaired surrogates.
                        if (Current != '\\' || Peek(1) != 'u')
                        {
                            throw Rejection(
                                ConfigurationFileRejectionKind.InvalidSyntax,
                                _position - 6);
                        }

                        var secondEscapeStart = _position;
                        _position += 2;
                        if (!TryReadUnicodeEscape(out var lowSurrogate) ||
                            !char.IsLowSurrogate(lowSurrogate))
                        {
                            throw Rejection(
                                ConfigurationFileRejectionKind.InvalidSyntax,
                                secondEscapeStart);
                        }

                        chars.Add(unicodeChar);
                        chars.Add(lowSurrogate);
                        continue;
                    }

                    if (char.IsLowSurrogate(unicodeChar))
                    {
                        throw Rejection(ConfigurationFileRejectionKind.InvalidSyntax, _position - 6);
                    }

                    chars.Add(unicodeChar);
                    continue;
                }

                switch (escaped)
                {
                    case '"': chars.Add('"'); break;
                    case '\\': chars.Add('\\'); break;
                    case '/': chars.Add('/'); break;
                    case 'b': chars.Add('\b'); break;
                    case 'f': chars.Add('\f'); break;
                    case 'n': chars.Add('\n'); break;
                    case 'r': chars.Add('\r'); break;
                    case 't': chars.Add('\t'); break;
                    default:
                        throw Rejection(ConfigurationFileRejectionKind.InvalidSyntax, _position - 2);
                }
            }

            throw Rejection(ConfigurationFileRejectionKind.InvalidSyntax, stringStart);
        }

        private bool TryReadUnicodeEscape(out char value)
        {
            value = '\0';
            if (_position + 4 > _text.Length)
            {
                return false;
            }

            var codePoint = 0;
            for (var i = 0; i < 4; i++)
            {
                var hexValue = HexValue(Peek(i));
                if (hexValue < 0)
                {
                    return false;
                }

                codePoint = (codePoint * 16) + hexValue;
            }

            _position += 4;
            value = (char)codePoint;
            return true;
        }

        private static int HexValue(char value)
        {
            if (value >= '0' && value <= '9')
            {
                return value - '0';
            }

            if (value >= 'a' && value <= 'f')
            {
                return value - 'a' + 10;
            }

            if (value >= 'A' && value <= 'F')
            {
                return value - 'A' + 10;
            }

            return -1;
        }

        private ParseRejectedException Rejection(
            ConfigurationFileRejectionKind kind,
            int position,
            string? duplicateKey = null)
        {
            var clamped = Math.Min(position, _text.Length);
            var span = clamped < _text.Length
                ? TextSpan.FromBounds(clamped, clamped + 1)
                : TextSpan.FromBounds(clamped, clamped);
            return new ParseRejectedException(
                new ConfigurationFileRejection(kind, CreateLocation(span), duplicateKey));
        }

        /// <summary>The four whitespace characters JSON allows outside strings (space, tab, CR, LF).
        /// Form feed, vertical tab, and unicode spaces are rejected by the runtime reader.</summary>
        private static bool IsJsonWhitespace(char value)
        {
            return value == ' ' || value == '\t' || value == '\r' || value == '\n';
        }

        private void SkipWhitespace()
        {
            while (!IsEnd)
            {
                if (IsJsonWhitespace(Current))
                {
                    _position++;
                    continue;
                }

                if (Current == '/' && Peek(1) == '/')
                {
                    _position += 2;
                    while (!IsEnd && Current != '\r' && Current != '\n')
                    {
                        // The runtime reader rejects the unicode line/paragraph separators inside
                        // line comments rather than treating them as terminators.
                        if (Current == '\u2028' || Current == '\u2029')
                        {
                            throw Rejection(ConfigurationFileRejectionKind.InvalidSyntax, _position);
                        }

                        _position++;
                    }

                    continue;
                }

                if (Current == '/' && Peek(1) == '*')
                {
                    var commentStart = _position;
                    _position += 2;
                    var closed = false;
                    while (!IsEnd)
                    {
                        if (Current == '*' && Peek(1) == '/')
                        {
                            _position += 2;
                            closed = true;
                            break;
                        }

                        _position++;
                    }

                    if (!closed)
                    {
                        throw Rejection(ConfigurationFileRejectionKind.InvalidSyntax, commentStart);
                    }

                    continue;
                }

                break;
            }
        }

        private Location CreateLocation(TextSpan span)
        {
            return Location.Create(_path, span, _text.Lines.GetLinePositionSpan(span));
        }

        private void Read(char expected)
        {
            if (Current == expected)
            {
                _position++;
            }
        }

        private char Current => _position < _text.Length ? _text[_position] : '\0';
        private char Peek(int offset)
        {
            var position = _position + offset;
            return position < _text.Length ? _text[position] : '\0';
        }

        private bool IsEnd => _position >= _text.Length;
    }

    /// <summary>
    /// Internal bail-out for the first construct the runtime JSON provider would reject on load.
    /// Only the first rejection matters: the file is excluded from the configuration model either
    /// way, and the analyzer reports a single diagnostic per file.
    /// </summary>
    private sealed class ParseRejectedException : Exception
    {
        public ParseRejectedException(ConfigurationFileRejection rejection)
        {
            Rejection = rejection;
        }

        public ConfigurationFileRejection Rejection { get; }
    }
}
