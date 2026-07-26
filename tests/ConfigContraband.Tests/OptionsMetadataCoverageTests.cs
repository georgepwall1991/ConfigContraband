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

    [Fact]
    public void Nested_validation_candidates_are_stable_when_syntax_tree_order_changes()
    {
        const string AlphaSource = """
            using System.ComponentModel.DataAnnotations;

            public sealed partial class AppOptions
            {
                public AlphaOptions Alpha { get; set; } = new();
            }

            public sealed class AlphaOptions
            {
                [Required]
                public string Value { get; set; } = "";
            }
            """;
        const string ZebraSource = """
            using System.ComponentModel.DataAnnotations;

            public sealed partial class AppOptions
            {
                public ZebraOptions Zebra { get; set; } = new();
            }

            public sealed class ZebraOptions
            {
                [Required]
                public string Value { get; set; } = "";
            }
            """;

        var reversePathOrder = CompileType(
            [
                ("Zebra.cs", ZebraSource),
                ("Alpha.cs", AlphaSource)
            ],
            "AppOptions");
        var forwardPathOrder = CompileType(
            [
                ("Alpha.cs", AlphaSource),
                ("Zebra.cs", ZebraSource)
            ],
            "AppOptions");

        var reverseCandidates = OptionsTypeMetadata.Create(reversePathOrder)
            .GetNestedValidationCandidates()
            .Select(candidate => candidate.Property.Symbol.Name);
        var forwardCandidates = OptionsTypeMetadata.Create(forwardPathOrder)
            .GetNestedValidationCandidates()
            .Select(candidate => candidate.Property.Symbol.Name);

        Assert.Equal(["Alpha", "Zebra"], reverseCandidates);
        Assert.Equal(["Alpha", "Zebra"], forwardCandidates);
    }

    [Fact]
    public void Nested_validation_candidates_are_stable_for_metadata_properties_without_source_locations()
    {
        var options = CompileMetadataType("""
            using System.ComponentModel.DataAnnotations;

            public sealed class MetadataOptions
            {
                public ZebraOptions Zebra { get; set; } = new();

                public AlphaOptions Alpha { get; set; } = new();
            }

            public sealed class ZebraOptions
            {
                [Required]
                public string Value { get; set; } = "";
            }

            public sealed class AlphaOptions
            {
                [Required]
                public string Value { get; set; } = "";
            }
            """, "MetadataOptions");

        var candidates = OptionsTypeMetadata.Create(options)
            .GetNestedValidationCandidates()
            .Select(candidate => candidate.Property.Symbol.Name);

        Assert.Equal(["Alpha", "Zebra"], candidates);
    }

    [Fact]
    public void Metadata_creation_honors_pre_canceled_token()
    {
        var appOptions = CompileType("""
            using System.ComponentModel.DataAnnotations;

            public sealed class AppOptions
            {
                public DatabaseOptions Database { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string Value { get; set; } = "";
            }
            """, "AppOptions");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => OptionsTypeMetadata.Create(
                appOptions,
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public void Nested_validation_candidates_honor_pre_canceled_token()
    {
        var appOptions = CompileType("""
            using System.ComponentModel.DataAnnotations;

            public sealed class AppOptions
            {
                public DatabaseOptions Database { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string Value { get; set; } = "";
            }
            """, "AppOptions");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => OptionsTypeMetadata.Create(appOptions)
                .GetNestedValidationCandidates(cancellation.Token));
    }

    [Fact]
    public void Nested_validation_candidates_do_not_duplicate_self_recursive_property()
    {
        var options = CompileType("""
            using System.ComponentModel.DataAnnotations;

            public sealed class RecursiveOptions
            {
                [Required]
                public string Name { get; set; } = "";

                public RecursiveOptions? Next { get; set; }
            }
            """, "RecursiveOptions");

        var candidates = OptionsTypeMetadata.Create(options)
            .GetNestedValidationCandidates();

        var next = Assert.Single(
            candidates,
            candidate => candidate.Property.Symbol.Name == "Next");
        Assert.Equal("ValidateObjectMembers", next.AttributeName);
        Assert.False(next.IsCollection);
    }

    private static INamedTypeSymbol CompileType(string source, string typeName)
    {
        return CompileType([("Options.cs", source)], typeName);
    }

    private static INamedTypeSymbol CompileType(
        (string path, string source)[] sources,
        string typeName)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            "OptionsMetadataCoverage",
            sources.Select(source => CSharpSyntaxTree.ParseText(source.source, path: source.path)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetTypeByMetadataName(typeName)!;
    }

    private static INamedTypeSymbol CompileMetadataType(string source, string typeName)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
        var metadataCompilation = CSharpCompilation.Create(
            "OptionsMetadataAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var assembly = new MemoryStream();
        var emitResult = metadataCompilation.Emit(assembly);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics));

        var consumerCompilation = CSharpCompilation.Create(
            "OptionsMetadataConsumer",
            references:
            [
                .. references,
                MetadataReference.CreateFromImage(assembly.ToArray())
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return consumerCompilation.GetTypeByMetadataName(typeName)!;
    }
}
