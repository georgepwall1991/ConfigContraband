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

    private ConfigurationSnapshot(ImmutableArray<ConfigurationFile> files)
    {
        _files = files;
    }

    public bool HasFiles => !_files.IsDefaultOrEmpty;

    public static ConfigurationSnapshot Create(ImmutableArray<AdditionalText> additionalFiles, System.Threading.CancellationToken cancellationToken)
    {
        var builder = ImmutableArray.CreateBuilder<ConfigurationFile>();

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

            var root = JsonConfigurationParser.Parse(file.Path, text);
            if (root is not null)
            {
                builder.Add(new ConfigurationFile(file.Path, root));
            }
        }

        return new ConfigurationSnapshot(builder.ToImmutable());
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

    public ImmutableArray<ConfigurationNode> FindSections(string sectionPath)
    {
        var builder = ImmutableArray.CreateBuilder<ConfigurationNode>();

        foreach (var file in _files)
        {
            builder.AddRange(FindSections(file.Root, sectionPath));
        }

        return builder.ToImmutable();
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
            existing.Location);
    }

    private static ConfigurationProperty CreateProjectedProperty(
        string[] propertyPathParts,
        int partIndex,
        string parentPath,
        ConfigurationProperty source)
    {
        var key = propertyPathParts[partIndex];
        var fullPath = string.IsNullOrEmpty(parentPath) ? key : parentPath + ":" + key;
        var value = partIndex == propertyPathParts.Length - 1
            ? source.Value
            : new ConfigurationNode(ImmutableArray.Create(CreateProjectedProperty(
                propertyPathParts,
                partIndex + 1,
                fullPath,
                source)));

        return new ConfigurationProperty(key, fullPath, value, source.Location);
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

    private static string[] SplitPath(string sectionPath)
    {
        return sectionPath.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
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

internal sealed class ConfigurationNode
{
    public static readonly ConfigurationNode Empty = new(ImmutableArray<ConfigurationProperty>.Empty);

    public ConfigurationNode(ImmutableArray<ConfigurationProperty> properties)
    {
        Properties = properties;
    }

    public ImmutableArray<ConfigurationProperty> Properties { get; }
    public bool IsObject => !Properties.IsDefault;

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

internal sealed class ConfigurationProperty
{
    public ConfigurationProperty(string key, string fullPath, ConfigurationNode value, Location location)
    {
        Key = key;
        FullPath = fullPath;
        Value = value;
        Location = location;
    }

    public string Key { get; }
    public string FullPath { get; }
    public ConfigurationNode Value { get; }
    public Location Location { get; }
}

internal static class JsonConfigurationParser
{
    public static ConfigurationNode? Parse(string path, SourceText text)
    {
        var parser = new Parser(path, text);
        return parser.ParseRoot();
    }

    private sealed class Parser
    {
        private readonly string _path;
        private readonly SourceText _text;
        private int _position;

        public Parser(string path, SourceText text)
        {
            _path = path;
            _text = text;
        }

        public ConfigurationNode? ParseRoot()
        {
            SkipWhitespace();
            return Current == '{' ? ParseObject(parentPath: string.Empty) : null;
        }

        private ConfigurationNode ParseObject(string parentPath)
        {
            var properties = ImmutableArray.CreateBuilder<ConfigurationProperty>();
            Read('{');
            SkipWhitespace();

            while (!IsEnd && Current != '}')
            {
                SkipWhitespace();
                if (Current != '"')
                {
                    SkipMalformedValue();
                    break;
                }

                var keyStart = _position;
                var key = ParseString();
                var keyEnd = _position;
                var fullPath = string.IsNullOrEmpty(parentPath) ? key : parentPath + ":" + key;

                SkipWhitespace();
                if (Current == ':')
                {
                    Read(':');
                }

                SkipWhitespace();
                var value = ParseValue(fullPath);
                properties.Add(new ConfigurationProperty(
                    key,
                    fullPath,
                    value,
                    CreateLocation(TextSpan.FromBounds(keyStart, keyEnd))));

                SkipWhitespace();
                if (Current == ',')
                {
                    Read(',');
                    SkipWhitespace();
                    continue;
                }

                break;
            }

            if (Current == '}')
            {
                Read('}');
            }

            return new ConfigurationNode(properties.ToImmutable());
        }

        private ConfigurationNode ParseValue(string path)
        {
            SkipWhitespace();
            if (Current == '{')
            {
                return ParseObject(path);
            }

            if (Current == '[')
            {
                return ParseArray(path);
            }

            if (Current == '"')
            {
                _ = ParseString();
                return ConfigurationNode.Empty;
            }

            SkipScalar();
            return ConfigurationNode.Empty;
        }

        private ConfigurationNode ParseArray(string path)
        {
            var properties = ImmutableArray.CreateBuilder<ConfigurationProperty>();
            var index = 0;

            Read('[');
            SkipWhitespace();
            while (!IsEnd && Current != ']')
            {
                var itemStart = _position;
                var itemKey = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var itemPath = path + ":" + itemKey;
                var value = ParseValue(itemPath);
                properties.Add(new ConfigurationProperty(
                    itemKey,
                    itemPath,
                    value,
                    CreateLocation(TextSpan.FromBounds(itemStart, Math.Min(_position, _text.Length)))));
                index++;

                SkipWhitespace();
                if (Current == ',')
                {
                    Read(',');
                    SkipWhitespace();
                    continue;
                }

                break;
            }

            if (Current == ']')
            {
                Read(']');
            }

            return new ConfigurationNode(properties.ToImmutable());
        }

        private string ParseString()
        {
            Read('"');
            var chars = new List<char>();

            while (!IsEnd)
            {
                var ch = Current;
                _position++;

                if (ch == '"')
                {
                    break;
                }

                if (ch != '\\' || IsEnd)
                {
                    chars.Add(ch);
                    continue;
                }

                var escaped = Current;
                _position++;
                if (escaped == 'u' && TryReadUnicodeEscape(out var unicodeChar))
                {
                    chars.Add(unicodeChar);
                    continue;
                }

                chars.Add(escaped switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    '/' => '/',
                    'b' => '\b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => escaped
                });
            }

            return new string(chars.ToArray());
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

        private void SkipScalar()
        {
            while (!IsEnd && Current != ',' && Current != '}' && Current != ']')
            {
                _position++;
            }
        }

        private void SkipMalformedValue()
        {
            while (!IsEnd && Current != '}')
            {
                _position++;
            }
        }

        private void SkipWhitespace()
        {
            while (!IsEnd)
            {
                if (char.IsWhiteSpace(Current))
                {
                    _position++;
                    continue;
                }

                if (Current == '/' && Peek(1) == '/')
                {
                    _position += 2;
                    while (!IsEnd && Current != '\r' && Current != '\n')
                    {
                        _position++;
                    }

                    continue;
                }

                if (Current == '/' && Peek(1) == '*')
                {
                    _position += 2;
                    while (!IsEnd)
                    {
                        if (Current == '*' && Peek(1) == '/')
                        {
                            _position += 2;
                            break;
                        }

                        _position++;
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
}
