using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace ConfigContraband.Tool;

internal sealed record SchemaOptions(string? Project, string? Solution, string? Output, bool Check);

/// <summary>
/// Loads the target project(s) with an <see cref="MSBuildWorkspace"/>, asks
/// <see cref="ConfigContraband.RegistrationExtractor"/> for the bound sections, and writes (or verifies)
/// each project's <c>appsettings.schema.json</c>.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class SchemaCommand
{
    private const string DefaultSchemaFileName = "appsettings.schema.json";

    public static async Task<int> RunAsync(SchemaOptions options)
    {
        using var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, e) =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                Console.Error.WriteLine($"warning: {e.Diagnostic.Message}");
            }
        };

        IReadOnlyList<Project> projects;
        try
        {
            projects = await LoadProjectsAsync(workspace, options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: failed to load project: {ex.Message}");
            return 2;
        }

        if (projects.Count == 0)
        {
            Console.Error.WriteLine("error: no projects were loaded.");
            return 2;
        }

        var output = options.Output;
        if (output is not null && projects.Count > 1)
        {
            Console.Error.WriteLine("warning: --output is ignored when multiple projects are processed; using per-project defaults.");
            output = null;
        }

        var anyStale = false;
        foreach (var project in projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation is null)
            {
                Console.Error.WriteLine($"warning: could not compile '{project.Name}'; skipping.");
                continue;
            }

            var sections = RegistrationExtractor.ExtractAll(compilation);
            var json = SchemaDocumentBuilder.Build(sections, compilation).ToJsonString() + "\n";
            var schemaPath = output ?? Path.Combine(ProjectDirectory(project), DefaultSchemaFileName);

            if (options.Check)
            {
                if (!SchemaMatches(schemaPath, json))
                {
                    anyStale = true;
                    Console.Error.WriteLine($"out of date: {schemaPath}");
                }
            }
            else
            {
                File.WriteAllText(schemaPath, json);
                Console.WriteLine($"wrote {schemaPath} ({sections.Count} section(s) from {project.Name})");
            }
        }

        if (options.Check && anyStale)
        {
            Console.Error.WriteLine("Schema is out of date. Run 'configcontraband schema' to regenerate.");
            return 1;
        }

        return 0;
    }

    private static async Task<IReadOnlyList<Project>> LoadProjectsAsync(MSBuildWorkspace workspace, SchemaOptions options)
    {
        if (options.Solution is { } solutionPath)
        {
            var solution = await workspace.OpenSolutionAsync(solutionPath);
            return solution.Projects.ToList();
        }

        var project = await workspace.OpenProjectAsync(options.Project!);
        return [project];
    }

    private static string ProjectDirectory(Project project)
    {
        return Path.GetDirectoryName(project.FilePath) ?? Directory.GetCurrentDirectory();
    }

    private static bool SchemaMatches(string schemaPath, string expected)
    {
        if (!File.Exists(schemaPath))
        {
            return false;
        }

        return Normalize(File.ReadAllText(schemaPath)) == Normalize(expected);
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n").TrimEnd('\n');
    }
}
