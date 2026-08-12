using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ConfigContraband.Tests;

/// <summary>
/// Guards NuGet/GitHub discoverability assets: package description/tags, README funnel,
/// and product-flow visuals that ship with PackageReadmeFile.
/// </summary>
public sealed class DiscoverabilityMetadataTests
{
    private static string RepositoryRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    [Fact]
    public void Analyzer_package_description_and_tags_include_high_intent_options_terms()
    {
        var csproj = XDocument.Load(
            Path.Combine(RepositoryRoot, "src", "ConfigContraband", "ConfigContraband.csproj"));

        var description = Assert.Single(csproj.Descendants("Description")).Value;
        var summary = Assert.Single(csproj.Descendants("PackageSummary")).Value;
        var tags = Assert.Single(csproj.Descendants("PackageTags")).Value;
        var title = Assert.Single(csproj.Descendants("Title")).Value;
        var readmeFile = Assert.Single(csproj.Descendants("PackageReadmeFile")).Value;

        Assert.Equal("README.md", readmeFile);
        Assert.Contains("Options", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("appsettings", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Options", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ValidateOnStart", summary, StringComparison.Ordinal);
        Assert.True(summary.Length < description.Length, "PackageSummary should be shorter than Description.");

        foreach (var term in new[]
                 {
                     "Options",
                     "BindConfiguration",
                     "ValidateOnStart",
                     "ValidateDataAnnotations",
                     "appsettings.json",
                     "IOptions",
                     "Roslyn",
                 })
        {
            Assert.True(
                description.Contains(term, StringComparison.Ordinal),
                $"Analyzer Description must contain '{term}' for NuGet search discoverability.");
        }

        foreach (var tag in new[]
                 {
                     "validation",
                     "ValidateOnStart",
                     "ValidateDataAnnotations",
                     "BindConfiguration",
                     "IOptions",
                     "options-pattern",
                     "DataAnnotations",
                     "roslyn-analyzer",
                     "appsettings",
                 })
        {
            Assert.True(
                tags.Contains(tag, StringComparison.Ordinal),
                $"Analyzer PackageTags must include '{tag}'.");
        }

        Assert.Contains(
            csproj.Descendants("None"),
            n => string.Equals(n.Attribute("Include")?.Value, @"..\..\docs\nuget-analyzer.md", StringComparison.Ordinal)
                && string.Equals(n.Attribute("Pack")?.Value, "true", StringComparison.OrdinalIgnoreCase)
                && string.Equals(n.Attribute("PackagePath")?.Value, @"\README.md", StringComparison.Ordinal));
    }

    [Fact]
    public void Tool_package_description_and_tags_include_schema_and_options_terms()
    {
        var csproj = XDocument.Load(
            Path.Combine(RepositoryRoot, "src", "ConfigContraband.Tool", "ConfigContraband.Tool.csproj"));

        var description = Assert.Single(csproj.Descendants("Description")).Value;
        var summary = Assert.Single(csproj.Descendants("PackageSummary")).Value;
        var tags = Assert.Single(csproj.Descendants("PackageTags")).Value;
        var title = Assert.Single(csproj.Descendants("Title")).Value;
        var icon = Assert.Single(csproj.Descendants("PackageIcon")).Value;
        var releaseNotes = Assert.Single(csproj.Descendants("PackageReleaseNotes")).Value;

        Assert.Contains("schema", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("appsettings.schema.json", description, StringComparison.Ordinal);
        Assert.Contains("ValidateDataAnnotations", description, StringComparison.Ordinal);
        Assert.Contains("BindConfiguration", description, StringComparison.Ordinal);
        Assert.Contains("schema", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("assets/configcontraband-icon.png", icon);
        Assert.False(string.IsNullOrWhiteSpace(releaseNotes));
        Assert.True(summary.Length < description.Length, "PackageSummary should be shorter than Description.");

        foreach (var tag in new[]
                 {
                     "json-schema",
                     "schema",
                     "intellisense",
                     "ValidateDataAnnotations",
                     "IOptions",
                     "BindConfiguration",
                     "appsettings",
                 })
        {
            Assert.True(
                tags.Contains(tag, StringComparison.Ordinal),
                $"Tool PackageTags must include '{tag}'.");
        }

        Assert.Contains(
            csproj.Descendants("None"),
            n => string.Equals(n.Attribute("Include")?.Value, @"..\..\docs\nuget-tool.md", StringComparison.Ordinal)
                && string.Equals(n.Attribute("Pack")?.Value, "true", StringComparison.OrdinalIgnoreCase)
                && string.Equals(n.Attribute("PackagePath")?.Value, @"\README.md", StringComparison.Ordinal));
    }

    [Fact]
    public void Readme_conversion_funnel_and_product_visuals_exist_with_resolvable_paths()
    {
        var readmePath = Path.Combine(RepositoryRoot, "README.md");
        var readme = File.ReadAllText(readmePath);

        foreach (var section in new[]
                 {
                     "## The problem",
                     "## What it catches",
                     "## Install",
                     "## See it work",
                     "## 30-second path",
                     "## Feature snapshot",
                     "## Rule Details",
                     "## Versioning",
                 })
        {
            Assert.Contains(section, readme, StringComparison.Ordinal);
        }

        Assert.Contains("PrivateAssets=\"all\"", readme, StringComparison.Ordinal);
        Assert.Contains("Version=\"0.9.0\"", readme, StringComparison.Ordinal);
        Assert.Contains("CFG001", readme, StringComparison.Ordinal);
        Assert.Contains("CFG010", readme, StringComparison.Ordinal);
        Assert.Contains("stays quiet", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ConfigContraband.Quickstart", readme, StringComparison.Ordinal);

        var analyzerVersion = Assert.Single(
                XDocument.Load(Path.Combine(RepositoryRoot, "src", "ConfigContraband", "ConfigContraband.csproj"))
                    .Descendants("Version"))
            .Value;
        var nugetAnalyzerReadme = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "nuget-analyzer.md"));
        Assert.Contains(
            $"Version=\"{analyzerVersion}\"",
            nugetAnalyzerReadme,
            StringComparison.Ordinal);
        Assert.Contains(
            $"Version=\"{analyzerVersion}\"",
            readme,
            StringComparison.Ordinal);

        // NuGet.org requires absolute HTTPS image URLs in PackageReadmeFile content.
        // GitHub raw URLs keep both NuGet and GitHub README rendering working.
        const string rawBase =
            "https://raw.githubusercontent.com/georgepwall1991/ConfigContraband/main/";

        var visualAssets = new[]
        {
            "assets/flow-ide-diagnostics.svg",
            "assets/flow-before-after-fix.svg",
            "assets/flow-analyzer-schema-loop.svg",
        };

        foreach (var asset in visualAssets)
        {
            Assert.Contains(rawBase + asset, readme, StringComparison.Ordinal);
            var fullPath = Path.Combine(RepositoryRoot, asset);
            Assert.True(File.Exists(fullPath), $"Missing README visual: {asset}");
            Assert.True(new FileInfo(fullPath).Length > 0, $"Empty README visual: {asset}");
        }

        Assert.Contains(rawBase + "assets/configcontraband-icon.png", readme, StringComparison.Ordinal);

        // Relative image paths break NuGet.org README rendering — require HTTPS.
        var imageRefs = Regex.Matches(readme, @"!\[[^\]]*\]\(([^)]+)\)")
            .Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(readme, @"<img[^>]+src=""([^""]+)""")
                .Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal);

        foreach (var imageRef in imageRefs)
        {
            Assert.True(
                imageRef.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
                $"README image must use absolute HTTPS for NuGet rendering: {imageRef}");
        }
    }

    [Fact]
    public void NuGet_package_readmes_are_short_conversion_funnels_with_absolute_image_urls()
    {
        const string rawBase =
            "https://raw.githubusercontent.com/georgepwall1991/ConfigContraband/main/";

        var analyzerReadme = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "nuget-analyzer.md"));
        var toolReadme = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "nuget-tool.md"));
        var githubReadme = File.ReadAllText(Path.Combine(RepositoryRoot, "README.md"));

        Assert.True(analyzerReadme.Length < githubReadme.Length / 2,
            "Analyzer NuGet README should be much shorter than the GitHub reference README.");
        Assert.True(toolReadme.Length < githubReadme.Length / 2,
            "Tool NuGet README should be much shorter than the GitHub reference README.");

        Assert.Contains("## Install", analyzerReadme, StringComparison.Ordinal);
        Assert.Contains("ValidateOnStart", analyzerReadme, StringComparison.Ordinal);
        Assert.Contains("ValidateDataAnnotations", analyzerReadme, StringComparison.Ordinal);
        Assert.Contains("ConfigContraband.Quickstart", analyzerReadme, StringComparison.Ordinal);
        Assert.DoesNotContain("## Rule Details", analyzerReadme, StringComparison.Ordinal);

        Assert.Contains("dotnet tool install", toolReadme, StringComparison.Ordinal);
        Assert.Contains("appsettings.schema.json", toolReadme, StringComparison.Ordinal);
        Assert.Contains("configcontraband schema", toolReadme, StringComparison.Ordinal);

        foreach (var readme in new[] { analyzerReadme, toolReadme })
        {
            Assert.Contains(rawBase + "assets/configcontraband-icon.png", readme, StringComparison.Ordinal);

            var imageRefs = Regex.Matches(readme, @"!\[[^\]]*\]\(([^)]+)\)")
                .Select(m => m.Groups[1].Value)
                .Concat(Regex.Matches(readme, @"<img[^>]+src=""([^""]+)""")
                    .Select(m => m.Groups[1].Value))
                .Distinct(StringComparer.Ordinal);

            foreach (var imageRef in imageRefs)
            {
                Assert.True(
                    imageRef.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
                    $"NuGet README image must use absolute HTTPS: {imageRef}");
            }
        }
    }

    [Fact]
    public void Analyzer_and_tool_pack_all_assets_for_nuget_readme_rendering()
    {
        var analyzer = XDocument.Load(
            Path.Combine(RepositoryRoot, "src", "ConfigContraband", "ConfigContraband.csproj"));
        var tool = XDocument.Load(
            Path.Combine(RepositoryRoot, "src", "ConfigContraband.Tool", "ConfigContraband.Tool.csproj"));

        Assert.Contains(
            analyzer.Descendants("None"),
            n => (n.Attribute("Include")?.Value ?? string.Empty).Contains("assets", StringComparison.Ordinal)
                && string.Equals(n.Attribute("Pack")?.Value, "true", StringComparison.OrdinalIgnoreCase)
                && (n.Attribute("PackagePath")?.Value ?? string.Empty).Contains("assets", StringComparison.Ordinal));

        Assert.Contains(
            tool.Descendants("None"),
            n => (n.Attribute("Include")?.Value ?? string.Empty).Contains("assets", StringComparison.Ordinal)
                && string.Equals(n.Attribute("Pack")?.Value, "true", StringComparison.OrdinalIgnoreCase)
                && (n.Attribute("PackagePath")?.Value ?? string.Empty).Contains("assets", StringComparison.Ordinal));
    }
}
