using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ConfigContraband.Core.Tests;

public sealed class ScalarConversionTests
{
    private static readonly Compilation Compilation = CreateCompilation();

    [Theory]
    // Provable failures -> report.
    [InlineData("int", "String", "eighty", true)]
    [InlineData("int", "Bool", "true", true)]
    [InlineData("long", "String", "x", true)]
    [InlineData("double", "String", "abc", true)]
    [InlineData("decimal", "String", "abc", true)]
    [InlineData("bool", "String", "yes", true)]
    [InlineData("enum", "String", "Loud", true)]
    [InlineData("guid", "String", "not-a-guid", true)]
    [InlineData("timespan", "String", "banana", true)]
    [InlineData("datetime", "String", "not-a-date", true)]
    [InlineData("char", "String", "ab", true)]
    [InlineData("int?", "String", "eighty", true)]
    [InlineData("int", "String", "8,080", true)] // integral converter rejects thousands
    // Convertible / skipped -> no report.
    [InlineData("int", "Number", "8080", false)]
    [InlineData("int", "String", "8080", false)]
    [InlineData("int", "String", "0x1F", false)] // hex form the converter accepts
    [InlineData("int", "String", "#FF", false)]
    [InlineData("int", "String", " 80 ", false)] // surrounding whitespace
    [InlineData("bool", "Bool", "true", false)]
    [InlineData("bool", "String", "TRUE", false)] // case-insensitive
    [InlineData("enum", "Number", "3", false)] // numeric even if undefined member
    [InlineData("flags", "String", "Read, Write", false)] // comma-list combination
    [InlineData("char", "String", "a", false)]
    [InlineData("char", "String", "", false)]
    [InlineData("datetime", "String", "", false)]
    [InlineData("datetime", "String", "2020-01-02", false)]
    [InlineData("guid", "String", "d3b07384-d9a0-4c9b-8b5e-000000000000", false)]
    [InlineData("timespan", "String", "00:05:00", false)]
    [InlineData("decimal", "String", "1e2", false)] // exponent notation the converter accepts
    [InlineData("int?", "String", "8080", false)]
    [InlineData("string", "String", "anything", false)]
    [InlineData("object", "String", "anything", false)]
    [InlineData("class", "String", "anything", false)] // nested-object target given a scalar
    [InlineData("intlist", "String", "eighty", false)] // collection target is out of scope
    [InlineData("int", "Null", "null", false)] // JSON null -> CFG002's concern
    [InlineData("int", "None", null, false)] // object/array value
    [InlineData("int", "String", "   ", false)] // whitespace-only
    public void IsProvablyNotConvertible_matches_runtime_binder(
        string typeKey,
        string kindKey,
        string? raw,
        bool expected
    )
    {
        var target = ResolveType(typeKey);
        var kind = System.Enum.Parse<ScalarKind>(kindKey);

        Assert.Equal(expected, ScalarConversion.IsProvablyNotConvertible(target, kind, raw));
    }

    [Fact]
    public void IsProvablyNotConvertible_returns_false_for_null_target()
    {
        Assert.False(ScalarConversion.IsProvablyNotConvertible(null, ScalarKind.String, "eighty"));
    }

    private static ITypeSymbol ResolveType(string key)
    {
        return key switch
        {
            "int" => Compilation.GetSpecialType(SpecialType.System_Int32),
            "long" => Compilation.GetSpecialType(SpecialType.System_Int64),
            "double" => Compilation.GetSpecialType(SpecialType.System_Double),
            "decimal" => Compilation.GetSpecialType(SpecialType.System_Decimal),
            "bool" => Compilation.GetSpecialType(SpecialType.System_Boolean),
            "char" => Compilation.GetSpecialType(SpecialType.System_Char),
            "string" => Compilation.GetSpecialType(SpecialType.System_String),
            "object" => Compilation.GetSpecialType(SpecialType.System_Object),
            "int?" => Compilation
                .GetSpecialType(SpecialType.System_Nullable_T)
                .Construct(Compilation.GetSpecialType(SpecialType.System_Int32)),
            "guid" => Compilation.GetTypeByMetadataName("System.Guid")!,
            "timespan" => Compilation.GetTypeByMetadataName("System.TimeSpan")!,
            "datetime" => Compilation.GetTypeByMetadataName("System.DateTime")!,
            "datetimeoffset" => Compilation.GetTypeByMetadataName("System.DateTimeOffset")!,
            "intlist" => Compilation
                .GetTypeByMetadataName("System.Collections.Generic.List`1")!
                .Construct(Compilation.GetSpecialType(SpecialType.System_Int32)),
            "enum" => Compilation.GetTypeByMetadataName("Color")!,
            "flags" => Compilation.GetTypeByMetadataName("Access")!,
            "class" => Compilation.GetTypeByMetadataName("Nested")!,
            _ => throw new System.ArgumentOutOfRangeException(nameof(key), key, "Unknown type key"),
        };
    }

    private static Compilation CreateCompilation()
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        return CSharpCompilation.Create(
            "ScalarConversionTests",
            [
                CSharpSyntaxTree.ParseText(
                    """
                    public enum Color { Red, Green, Blue }

                    [System.Flags]
                    public enum Access { None = 0, Read = 1, Write = 2 }

                    public sealed class Nested { public string Name { get; set; } = ""; }
                    """
                ),
            ],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }
}
