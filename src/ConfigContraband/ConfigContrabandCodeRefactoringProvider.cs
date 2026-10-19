using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ConfigContraband;

// CFG006/CFG007 diagnostics anchor on the JSON key inside an additional file, which the
// code-fix pipeline cannot route to (an additional file is not a Document). This
// refactoring offers the same rename from the C# registration anchor instead: invoking the
// lightbulb on a bound registration (Bind/BindConfiguration/Configure) lists a rename
// action for each unknown key in the bound section that has a confident bindable-property
// near-match. Synthesized (projected) section nodes have no source location and are
// skipped, so keys written as flat "A:B" pairs stay quiet.
[ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(ConfigContrabandCodeRefactoringProvider)), Shared]
public sealed class ConfigContrabandCodeRefactoringProvider : CodeRefactoringProvider
{
    public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
    {
        var document = context.Document;
        var root = (await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false))!;
        var node = root.FindNode(context.Span, getInnermostNodeForTie: true);
        var invocation = node as InvocationExpressionSyntax ?? node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
        {
            return;
        }

        var semanticModel = (await document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false))!;
        if (!ConfigContrabandAnalyzer.TryCreateRegistration(invocation, semanticModel, out var registration) ||
            !registration.HasBoundSection ||
            registration.SectionPath is null)
        {
            return;
        }

        // The document came from a real project, so a compilation is always available.
        var compilation = (await document.Project.GetCompilationAsync(context.CancellationToken).ConfigureAwait(false))!;

        var metadata = OptionsTypeMetadata.Create(
            registration.OptionsType,
            registration.BindsNonPublicProperties,
            compilation);
        var candidates = metadata.GetConfigurationNames();
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

        var solution = document.Project.Solution;
        foreach (var additionalDocument in document.Project.AdditionalDocuments)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (!ConfigurationSnapshot.IsAppSettingsFile(additionalDocument.FilePath))
            {
                continue;
            }

            var filePath = additionalDocument.FilePath!;
            var text = await additionalDocument.GetTextAsync(context.CancellationToken).ConfigureAwait(false);
            var result = JsonConfigurationParser.ParseDetailed(
                filePath,
                text,
                strictUnknownConfigurationKeySuppressedByAnalyzerConfig: false);
            if (result.Root is not { } fileRoot)
            {
                continue;
            }

            var fileName = System.IO.Path.GetFileName(filePath);
            foreach (var section in ConfigurationSnapshot.FindSections(fileRoot, registration.SectionPath))
            {
                if (!section.IsObject || section.Location is null)
                {
                    continue;
                }

                foreach (var property in section.Properties)
                {
                    if (metadata.TryGetConfigurationProperty(property.Key, out _) ||
                        metadata.TryGetSettableConstructorBoundAlias(property.Key, section, out _))
                    {
                        continue;
                    }

                    var suggestion = ConfigContrabandAnalyzer.FindClosest(property.Key, candidates);
                    if (suggestion is null)
                    {
                        continue;
                    }

                    var keySpan = property.Location.SourceSpan;
                    var replacement = "\"" + EscapeJsonKey(suggestion) + "\"";
                    var documentId = additionalDocument.Id;
                    context.RegisterRefactoring(
                        CodeAction.Create(
                            $"Rename key \"{property.Key}\" to \"{suggestion}\" in {fileName}",
                            _ => Task.FromResult(
                                solution.WithAdditionalDocumentText(
                                    documentId,
                                    text.WithChanges(new TextChange(keySpan, replacement)))),
                            equivalenceKey: $"RenameConfigurationKey:{filePath}:{property.FullPath}"));
                }
            }
        }
    }

    private static string EscapeJsonKey(string key)
    {
        return key.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
