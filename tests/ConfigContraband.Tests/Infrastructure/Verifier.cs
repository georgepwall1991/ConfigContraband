using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace ConfigContraband.Tests.Infrastructure;

internal static class Verifier
{
    public static ReferenceAssemblies OptionsReferences { get; } =
        ReferenceAssemblies.Net.Net80.AddPackages([
            new PackageIdentity("Microsoft.Extensions.DependencyInjection", "10.0.0"),
            new PackageIdentity("Microsoft.Extensions.Options", "10.0.0"),
            new PackageIdentity("Microsoft.Extensions.Options.ConfigurationExtensions", "10.0.0"),
            new PackageIdentity("Microsoft.Extensions.Options.DataAnnotations", "10.0.0"),
            new PackageIdentity("Microsoft.Extensions.Configuration.Abstractions", "10.0.0")
        ]);

    public static async Task VerifyAnalyzerAsync(
        string source,
        params DiagnosticResult[] expected)
    {
        var test = CreateAnalyzerTest(source);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    public static async Task VerifyAnalyzerAsync(
        string source,
        (string filename, string content) additionalFile,
        params DiagnosticResult[] expected)
    {
        var test = CreateAnalyzerTest(source);
        test.TestState.AdditionalFiles.Add(additionalFile);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    public static async Task VerifyAnalyzerAsync(
        string source,
        (string filename, string content)[] additionalFiles,
        params DiagnosticResult[] expected)
    {
        var test = CreateAnalyzerTest(source);
        foreach (var additionalFile in additionalFiles)
        {
            test.TestState.AdditionalFiles.Add(additionalFile);
        }

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    public static async Task VerifyCodeFixAsync(
        string source,
        string fixedSource,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<ConfigContrabandAnalyzer, ConfigContrabandCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = OptionsReferences
        };
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    public static async Task VerifyCodeFixAsync(
        string source,
        string fixedSource,
        (string filename, string content) additionalFile,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<ConfigContrabandAnalyzer, ConfigContrabandCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = OptionsReferences
        };
        test.TestState.AdditionalFiles.Add(additionalFile);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    public static async Task VerifyCodeFixAsync(
        (string filename, string content)[] sources,
        (string filename, string content)[] fixedSources,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<ConfigContrabandAnalyzer, ConfigContrabandCodeFixProvider, DefaultVerifier>
        {
            ReferenceAssemblies = OptionsReferences
        };

        foreach (var source in sources)
        {
            test.TestState.Sources.Add(source);
        }

        foreach (var fixedSource in fixedSources)
        {
            test.FixedState.Sources.Add(fixedSource);
        }

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    public static DiagnosticResult Diagnostic(Microsoft.CodeAnalysis.DiagnosticDescriptor descriptor)
    {
        return new DiagnosticResult(descriptor);
    }

    private static CSharpAnalyzerTest<ConfigContrabandAnalyzer, DefaultVerifier> CreateAnalyzerTest(string source)
    {
        return new CSharpAnalyzerTest<ConfigContrabandAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = OptionsReferences
        };
    }
}
