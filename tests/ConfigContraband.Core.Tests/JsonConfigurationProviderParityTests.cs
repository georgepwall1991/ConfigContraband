using System.Text;
using Microsoft.Extensions.Configuration;

namespace ConfigContraband.Core.Tests;

/// <summary>
/// Runtime-parity evidence for CFG011: each case is loaded through the real
/// <c>Microsoft.Extensions.Configuration.Json</c> provider and through
/// <see cref="JsonConfigurationParser"/>, and the two must agree on whether the file loads.
/// </summary>
public sealed class JsonConfigurationProviderParityTests
{
    private static string? RuntimeProviderFailure(byte[] bytes)
    {
        var builder = new ConfigurationBuilder();
        builder.AddJsonStream(new MemoryStream(bytes));
        try
        {
            _ = builder.Build();
            return null;
        }
        catch (Exception ex)
        {
            return ex.GetType().FullName;
        }
    }

    public static IEnumerable<object?[]> Cases()
    {
        // Syntax the runtime provider rejects.
        yield return Row("unterminated root object", "{", InvalidSyntax);
        yield return Row("unterminated nested object", "{ \"a\": { \"b\": 1", InvalidSyntax);
        yield return Row("missing colon", "{ \"a\" \"b\" }", InvalidSyntax);
        yield return Row("missing comma", "{ \"a\": 1 \"b\": 2 }", InvalidSyntax);
        yield return Row("unquoted member name", "{ unquoted: true }", InvalidSyntax);
        yield return Row("empty file", "", InvalidSyntax);
        yield return Row("whitespace only", "   ", InvalidSyntax);
        yield return Row("comment only", "// nothing", InvalidSyntax);
        yield return Row("garbage", "not json", InvalidSyntax);
        yield return Row("trailing content", "{ \"a\": 1 } { \"b\": 2 }", InvalidSyntax);
        yield return Row("trailing comma after root", "{ \"a\": 1 },", InvalidSyntax);
        yield return Row("unterminated array", "{ \"a\": [1, 2", InvalidSyntax);
        yield return Row("unterminated string", "{ \"a\": \"x", InvalidSyntax);
        yield return Row("unterminated block comment", "{ \"a\": 1 /* c", InvalidSyntax);
        yield return Row("unterminated block comment after root", "{ } /*", InvalidSyntax);
        yield return Row("unterminated block comment slash tail", "{ } /*/", InvalidSyntax);
        yield return Row("comment-only file", "/* nothing */", InvalidSyntax);
        yield return Row("lone opening array at root", "[1,", InvalidSyntax);
        yield return Row("comma where value expected", "{ \"a\": , }", InvalidSyntax);
        yield return Row("stray comma in object", "{ \"a\": 1, , \"b\": 2 }", InvalidSyntax);
        yield return Row("stray comma in array", "{ \"a\": [1, , 2] }", InvalidSyntax);

        // Scalar tokens the tolerant reader still rejects.
        yield return Row("truncated literal tru", "{ \"a\": tru }", InvalidSyntax);
        yield return Row("uppercase TRUE", "{ \"a\": TRUE }", InvalidSyntax);
        yield return Row("uppercase False", "{ \"a\": False }", InvalidSyntax);
        yield return Row("uppercase Null", "{ \"a\": Null }", InvalidSyntax);
        yield return Row("leading zero 080", "{ \"a\": 080 }", InvalidSyntax);
        yield return Row("leading plus", "{ \"a\": +1 }", InvalidSyntax);
        yield return Row("leading dot", "{ \"a\": .5 }", InvalidSyntax);
        yield return Row("trailing dot", "{ \"a\": 1. }", InvalidSyntax);
        yield return Row("bare exponent", "{ \"a\": 1e }", InvalidSyntax);
        yield return Row("trailing letter", "{ \"a\": 1x }", InvalidSyntax);
        yield return Row("NaN", "{ \"a\": NaN }", InvalidSyntax);
        yield return Row("hex literal", "{ \"a\": 0x10 }", InvalidSyntax);
        yield return Row("colon in scalar", "{ \"a\": 12:34 }", InvalidSyntax);
        yield return Row("minus alone", "{ \"a\": - }", InvalidSyntax);

        // String grammar the tolerant reader still rejects.
        yield return Row("invalid escape \\q", "{ \"a\": \"x\\qy\" }", InvalidSyntax);
        yield return Row("bad \\u hex", "{ \"a\\u00g1\": 1 }", InvalidSyntax);
        yield return Row("truncated \\u escape", "{ \"a\": \"\\u00\" }", InvalidSyntax);
        yield return Row("unescaped control character", "{ \"a\": \"x\ny\" }", InvalidSyntax);
        yield return Row("unescaped tab in string", "{ \"a\": \"x\ty\" }", InvalidSyntax);
        yield return Row("form feed outside string", "{ \"a\": 1 \f }", InvalidSyntax);
        yield return Row("non-object root with trailing content", "[1] extra", InvalidSyntax);
        yield return Row("slash that is not a comment inside scalar", "{ \"a\": 1/x }", InvalidSyntax);
        yield return Row("slash that is not a comment after root", "{ } /x", InvalidSyntax);
        yield return Row("string ends in lone backslash", "{ \"a\": \"x\\", InvalidSyntax);
        yield return Row("high surrogate then malformed escape", "{ \"a\": \"\\uD800\\uZ\" }", InvalidSyntax);
        yield return Row("lone high surrogate escape", "{ \"a\": \"\\uD800\" }", InvalidSyntax);
        yield return Row("lone low surrogate escape", "{ \"a\": \"\\uDC00\" }", InvalidSyntax);
        yield return Row("high surrogate then non-surrogate escape", "{ \"a\": \"\\uD800\\u0041\" }", InvalidSyntax);
        yield return Row("high surrogate then literal char", "{ \"a\": \"\\uD800x\" }", InvalidSyntax);

        // U+2028/U+2029 are invalid inside line comments (the runtime reader does not treat them
        // as comment terminators); they remain legal inside block comments and string literals.
        yield return Row("line comment with U+2028", "{ \"a\": 1 } // x\u2028", InvalidSyntax);
        yield return Row("line comment with U+2029", "{ \"a\": 1 } // x\u2029", InvalidSyntax);
        yield return Row("line comment with U+2028 mid-file", "{ // x\u2028\n \"a\": 1 }", InvalidSyntax);

        // Non-object roots parse as JSON but the provider requires an object.
        yield return Row("array root", "[]", NonObjectRoot);
        yield return Row("populated array root", "[1, 2]", NonObjectRoot);
        yield return Row("number root", "5", NonObjectRoot);
        yield return Row("string root", "\"text\"", NonObjectRoot);
        yield return Row("bool root", "true", NonObjectRoot);
        yield return Row("null root", "null", NonObjectRoot);

        // Case-insensitive duplicate flattened scalar paths.
        yield return Row(
            "duplicate flattened scalar path",
            "{ \"Server\": { \"Value\": \"x\" }, \"server:value\": 80 }",
            DuplicateKey);
        yield return Row(
            "duplicate colon path against nested key",
            "{ \"a\": { \"b\": 1 }, \"a:b\": 2 }",
            DuplicateKey);
        // But the reverse order rejects: the empty container occupies the path, then the
        // scalar's duplicate check trips on it.
        yield return Row(
            "empty container then scalar at same path",
            "{ \"s\": {}, \"S\": 1 }",
            DuplicateKey);
        yield return Row(
            "empty container then scalar at same path with different case",
            "{ \"a\": {}, \"A\": \"x\" }",
            DuplicateKey);
        // The provider's empty-container write is an unconditional overwrite — no duplicate check.
        yield return Row(
            "scalar then empty container at same path",
            "{ \"s\": 1, \"S\": {} }",
            null);
        yield return Row(
            "duplicate flattened path below empty root segment",
            "{ \"\": { \"a\": 1 }, \":a\": 2 }",
            DuplicateKey);
        yield return Row(
            "duplicate flattened path inside array item",
            "{ \"a\": [ { \"x\": { \"y\": 1 }, \"x:y\": 2 } ] }",
            DuplicateKey);

        // Exceeding the runtime reader's default maximum depth — arrays count too.
        var deep65 = "{" + string.Concat(Enumerable.Repeat("\"n\":{", 64)) + "\"v\":1" + new string('}', 65);
        yield return Row("65 nested objects", deep65, DepthExceeded);
        yield return Row(
            "65 nested arrays",
            "{ \"a\": " + new string('[', 65) + new string(']', 65) + " }",
            DepthExceeded);

        // Syntax the runtime provider tolerates — these must stay quiet.
        yield return Row("line comments", "{ // c\n \"a\": 1 }", null);
        yield return Row("block comments", "{ /* c */ \"a\": 1 }", null);
        yield return Row("comment after scalar", "{ \"a\": 1 /* c */, \"b\": 2 }", null);
        yield return Row("line comment after scalar", "{ \"a\": 1 // c\n, \"b\": 2 }", null);
        yield return Row("block comment immediately after scalar", "{ \"a\": 1/* c */, \"b\": 2 }", null);
        yield return Row("line comment immediately after scalar", "{ \"a\": 1// c\n, \"b\": 2 }", null);
        yield return Row("trailing comma in object", "{ \"a\": 1, }", null);
        yield return Row("trailing comma in array", "{ \"a\": [1, 2,] }", null);
        yield return Row("trailing comment after root", "{ \"a\": 1 } // done", null);
        yield return Row("line comment with U+0085", "{ \"a\": 1 } // x\u0085", null);
        yield return Row("block comment with U+2028", "{ \"a\": 1 } /* x\u2028 */", null);
        yield return Row("line comment ends at CRLF", "{ \"a\": 1 } // x\r\n", null);
        yield return Row("double BOM", "\uFEFF\uFEFF{ \"a\": 1 }", InvalidSyntax);
        yield return Row("closed block comment after root", "{ } /* closed */", null);
        yield return Row("empty object root", "{}", null);
        yield return Row("empty object value", "{ \"a\": {} }", null);
        yield return Row("empty array value", "{ \"a\": [] }", null);
        yield return Row("nested array items", "{ \"a\": [[1], [2]] }", null);
        yield return Row("null values", "{ \"a\": null, \"b\": { \"c\": null } }", null);
        yield return Row(
            "duplicate object members merge",
            "{ \"a\": { \"b\": 1 }, \"a\": { \"c\": 2 } }",
            null);
        yield return Row(
            "scalar overwritten by empty container",
            "{ \"a\": \"x\", \"A\": {} }",
            null);
        yield return Row("two empty containers at same path merge", "{ \"a\": {}, \"A\": {} }", null);
        yield return Row("valid escapes", "{ \"a\": \"q\\\"bs\\\\sl\\/b\\bf\\fn\\nr\\rt\\tu\\u0041\" }", null);
        yield return Row("surrogate pair escape", "{ \"a\": \"\\uD83D\\uDE00\" }", null);
        yield return Row("all scalar kinds", "{ \"a\": -5, \"b\": 1.5, \"c\": 1e3, \"d\": 0, \"e\": -0.5E-2, \"f\": true, \"g\": false, \"h\": null, \"i\": \"str\" }", null);
        yield return Row("leading byte order mark", "\uFEFF{ \"a\": 1 }", null);
        yield return Row("empty member name", "{ \"\": { \"a\": 1 } }", null);
        var deep64 = "{" + string.Concat(Enumerable.Repeat("\"n\":{", 63)) + "\"v\":1" + new string('}', 64);
        yield return Row("64 nested objects", deep64, null);
    }

    private const string InvalidSyntax = nameof(ConfigurationFileRejectionKind.InvalidSyntax);
    private const string NonObjectRoot = nameof(ConfigurationFileRejectionKind.NonObjectRoot);
    private const string DuplicateKey = nameof(ConfigurationFileRejectionKind.DuplicateKey);
    private const string DepthExceeded = nameof(ConfigurationFileRejectionKind.DepthExceeded);

    private static object?[] Row(string name, string json, string? expected)
    {
        return [name, json, expected];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Parser_matches_runtime_provider_load_behavior(
        string name,
        string json,
        string? expectedRejection)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var runtimeFailure = RuntimeProviderFailure(bytes);
        var runtimeAccepted = runtimeFailure is null;

        // Decode through a stream like AdditionalText.GetText() does: a physical UTF-8 BOM is
        // stripped by the decoder before the parser sees the text, so a literal U+FEFF in the
        // decoded text is the second BOM of a double-BOM file — which the provider rejects.
        var result = JsonConfigurationParser.ParseDetailed(
            "appsettings.json",
            Microsoft.CodeAnalysis.Text.SourceText.From(new MemoryStream(bytes)),
            strictUnknownConfigurationKeySuppressedByAnalyzerConfig: false);



        Assert.True(
            runtimeAccepted == (result.Rejection is null),
            $"{name}: runtime provider {(runtimeAccepted ? "accepts" : $"rejects ({runtimeFailure})")} the file, " +
            $"but the parser {(result.Rejection is null ? "accepts" : $"rejects with {result.Rejection.Kind}")} it");

        if (expectedRejection is { } expected)
        {
            Assert.False(runtimeAccepted, $"{name}: the runtime provider unexpectedly accepts this file");
            Assert.Equal(expected, result.Rejection!.Kind.ToString());
        }
        else
        {
            Assert.True(runtimeAccepted, $"{name}: the runtime provider unexpectedly rejects this file");
        }
    }

    [Fact]
    public void Utf16_file_loads_like_the_provider()
    {
        // The runtime provider decodes the stream with BOM detection, so a UTF-16 appsettings
        // file loads — and Roslyn's decode produces the same text for the analyzer.
        var bytes = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes("{ \"a\": 1 }"))
            .ToArray();

        Assert.Null(RuntimeProviderFailure(bytes));

        var result = JsonConfigurationParser.ParseDetailed(
            "appsettings.json",
            Microsoft.CodeAnalysis.Text.SourceText.From(new MemoryStream(bytes)),
            strictUnknownConfigurationKeySuppressedByAnalyzerConfig: false);

        Assert.Null(result.Rejection);
    }
}
