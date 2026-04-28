using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ConfigContraband.Tests;

public sealed class OptionsMetadataCoverageTests
{
    [Fact]
    public void Nested_validation_candidates_include_nested_collection_graphs_and_ignore_cycles()
    {
        var appOptions = CompileType("""
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations;

            public sealed class AppOptions
            {
                public List<WrapperOptions> Wrappers { get; set; } = [];
                public RecursiveOptions Recursive { get; set; } = new();
            }

            public sealed class WrapperOptions
            {
                public List<DatabaseOptions> Databases { get; set; } = [];
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";
            }

            public sealed class RecursiveOptions
            {
                public RecursiveOptions? Next { get; set; }
            }
            """, "AppOptions");

        var candidates = OptionsTypeMetadata.Create(appOptions).GetNestedValidationCandidates();

        var wrappers = Assert.Single(candidates, candidate => candidate.Property.Symbol.Name == "Wrappers");
        Assert.True(wrappers.IsCollection);
        Assert.Equal("ValidateEnumeratedItems", wrappers.AttributeName);
        Assert.DoesNotContain(candidates, candidate => candidate.Property.Symbol.Name == "Recursive");
    }

    private static INamedTypeSymbol CompileType(string source, string typeName)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            "OptionsMetadataCoverage",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetTypeByMetadataName(typeName)!;
    }
}
