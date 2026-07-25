using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;

namespace ConfigContraband.Tool.Tests;

public sealed class SchemaCommandTests
{
    [Fact]
    public async Task Check_fails_closed_when_workspace_reports_project_load_failure()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "configcontraband-schema-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        var projectPath = Path.Combine(testDirectory, "Broken.csproj");

        try
        {
            await File.WriteAllTextAsync(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="Missing.csproj" />
                  </ItemGroup>
                </Project>
                """);

            var exitCode = await SchemaCommand.RunAsync(
                new SchemaOptions(projectPath, Solution: null, Output: null, Check: true));

            Assert.Equal(2, exitCode);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Check_fails_closed_when_project_has_compilation_errors()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "configcontraband-schema-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        var projectPath = Path.Combine(testDirectory, "Broken.csproj");

        try
        {
            await File.WriteAllTextAsync(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(testDirectory, "Broken.cs"),
                "public sealed class Broken { public void MissingClose( }");

            var exitCode = await SchemaCommand.RunAsync(
                new SchemaOptions(projectPath, Solution: null, Output: null, Check: true));

            Assert.Equal(2, exitCode);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Check_fails_closed_when_project_compilation_is_unavailable()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "configcontraband-schema-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        var projectPath = Path.Combine(testDirectory, "Valid.csproj");

        try
        {
            await File.WriteAllTextAsync(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            var exitCode = await SchemaCommand.RunAsync(
                new SchemaOptions(projectPath, Solution: null, Output: null, Check: true),
                _ => Task.FromResult<Compilation?>(null));

            Assert.Equal(2, exitCode);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void Check_returns_load_error_when_analysis_failed()
    {
        var exitCode = SchemaCommand.DetermineExitCode(check: true, anyStale: false, anyAnalysisFailure: true);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void Analysis_failure_takes_precedence_over_stale_schema()
    {
        var exitCode = SchemaCommand.DetermineExitCode(check: true, anyStale: true, anyAnalysisFailure: true);

        Assert.Equal(2, exitCode);
    }

    [Theory]
    [InlineData(false, false, false, 0)]
    [InlineData(false, false, true, 0)]
    [InlineData(true, false, false, 0)]
    [InlineData(true, true, false, 1)]
    public void Existing_exit_codes_are_preserved(
        bool check,
        bool anyStale,
        bool anyAnalysisFailure,
        int expected)
    {
        var exitCode = SchemaCommand.DetermineExitCode(check, anyStale, anyAnalysisFailure);

        Assert.Equal(expected, exitCode);
    }
}
