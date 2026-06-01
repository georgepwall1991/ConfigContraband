using System.Diagnostics.CodeAnalysis;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;

namespace ConfigContraband.Tool;

/// <summary>
/// Thin CLI host for ConfigContraband's schema generator. All schema logic lives in
/// <c>ConfigContraband.Core</c> (and is unit tested there); this shell only parses arguments, loads the
/// project's Roslyn <see cref="Compilation"/>, and writes <c>appsettings.schema.json</c>.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        if (args[0] != "schema")
        {
            Console.Error.WriteLine($"Unknown command '{args[0]}'.");
            PrintUsage();
            return 2;
        }

        if (!TryParseOptions(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            PrintUsage();
            return 2;
        }

        // Must register an MSBuild instance before any MSBuildWorkspace type is touched.
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        return await SchemaCommand.RunAsync(options);
    }

    private static bool TryParseOptions(string[] args, out SchemaOptions options, out string error)
    {
        string? project = null;
        string? solution = null;
        string? output = null;
        var check = false;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--project":
                    if (!TryTakeValue(args, ref i, out project, out error))
                    {
                        options = default!;
                        return false;
                    }

                    break;
                case "--solution":
                    if (!TryTakeValue(args, ref i, out solution, out error))
                    {
                        options = default!;
                        return false;
                    }

                    break;
                case "--output":
                    if (!TryTakeValue(args, ref i, out output, out error))
                    {
                        options = default!;
                        return false;
                    }

                    break;
                case "--check":
                    check = true;
                    break;
                default:
                    options = default!;
                    error = $"Unknown option '{args[i]}'.";
                    return false;
            }
        }

        if (project is not null && solution is not null)
        {
            options = default!;
            error = "Specify only one of --project or --solution.";
            return false;
        }

        if (project is null && solution is null)
        {
            project = TryFindSingleProject(out error);
            if (project is null)
            {
                options = default!;
                return false;
            }
        }

        options = new SchemaOptions(project, solution, output, check);
        error = string.Empty;
        return true;
    }

    private static bool TryTakeValue(string[] args, ref int index, out string? value, out string error)
    {
        if (index + 1 >= args.Length)
        {
            value = null;
            error = $"Missing value for '{args[index]}'.";
            return false;
        }

        value = args[++index];
        error = string.Empty;
        return true;
    }

    private static string? TryFindSingleProject(out string error)
    {
        var projects = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj", SearchOption.TopDirectoryOnly);
        switch (projects.Length)
        {
            case 1:
                error = string.Empty;
                return projects[0];
            case 0:
                error = "No .csproj found in the current directory. Pass --project or --solution.";
                return null;
            default:
                error = "Multiple .csproj files found. Pass --project to choose one.";
                return null;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            ConfigContraband schema generator

            Usage:
              configcontraband schema [options]

            Options:
              --project <path>    Project (.csproj) to analyze. Defaults to the single .csproj in the current directory.
              --solution <path>   Solution to analyze. A schema is written per project.
              --output <path>     Output schema path (single project only). Defaults to <projectDir>/appsettings.schema.json.
              --check             Do not write; exit non-zero if any schema is missing or out of date (for CI).
            """);
    }
}
