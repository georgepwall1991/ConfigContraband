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
                .SelectMany(file => file.Root.Properties.Select(property => property.Key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
        }

        var parentPath = string.Join(":", pathParts.Take(pathParts.Length - 1));
        return _files
            .Select(file => FindSection(file.Root, parentPath))
            .Where(node => node is not null)
            .SelectMany(node => node!.Properties.Select(property => property.Key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    public bool TryFindSection(string sectionPath, out ConfigurationNode section)
    {
        foreach (var file in _files)
        {
            var node = FindSection(file.Root, sectionPath);
            if (node is not null)
            {
                section = node;
                return true;
            }
        }

        section = ConfigurationNode.Empty;
        return false;
    }

    private static ConfigurationNode? FindSection(ConfigurationNode root, string sectionPath)
    {
        var current = root;
        foreach (var part in SplitPath(sectionPath))
        {
            if (!current.TryGetProperty(part, out var property))
            {
                return null;
            }

            current = property.Value;
        }

        return current;
    }

    private static string[] SplitPath(string sectionPath)
    {
        return sectionPath.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool IsAppSettingsFile(string path)
    {
        var fileName = System.IO.Path.GetFileName(path);
        return fileName.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) &&
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
                SkipArray(path);
                return ConfigurationNode.Empty;
            }

            if (Current == '"')
            {
                _ = ParseString();
                return ConfigurationNode.Empty;
            }

            SkipScalar();
            return ConfigurationNode.Empty;
        }

        private void SkipArray(string path)
        {
            Read('[');
            SkipWhitespace();
            while (!IsEnd && Current != ']')
            {
                _ = ParseValue(path);
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
            while (!IsEnd && char.IsWhiteSpace(Current))
            {
                _position++;
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
        private bool IsEnd => _position >= _text.Length;
    }
}
