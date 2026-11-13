using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;

namespace ConfigContraband;

/// <summary>
/// <see cref="WellKnownFixAllProviders.BatchFixer"/> drops changes to additional documents —
/// its merge only reads changed source documents, so a fix that edits appsettings JSON would
/// silently no-op under Fix All. This provider re-drives <see cref="CodeFixProvider.RegisterCodeFixesAsync"/>
/// per diagnostic and merges the resulting text changes for source AND additional documents.
/// </summary>
internal sealed class ConfigContrabandFixAllProvider : FixAllProvider
{
    public static readonly ConfigContrabandFixAllProvider Instance = new();

    private readonly CodeFixProvider _provider;

    private ConfigContrabandFixAllProvider(CodeFixProvider provider)
    {
        _provider = provider;
    }

    public ConfigContrabandFixAllProvider()
        : this(new ConfigContrabandCodeFixProvider())
    {
    }

    public override async Task<CodeAction?> GetFixAsync(FixAllContext fixAllContext)
    {
        var solution = fixAllContext.Solution;

        // The host sets Document to the first diagnostic's document for every scope, so branch
        // on Scope and enumerate the diagnostics of each document in that scope. Diagnostics
        // anchored in additional files never reach a document and are skipped by construction.
        var documents = fixAllContext.Scope switch
        {
            FixAllScope.Document => ImmutableArray.Create(fixAllContext.Document!),
            FixAllScope.Solution => solution.Projects
                .SelectMany(project => project.Documents)
                .ToImmutableArray(),
            _ => fixAllContext.Project.Documents.ToImmutableArray(),
        };

        var work = ImmutableArray.CreateBuilder<(Document Document, Diagnostic Diagnostic)>();
        foreach (var document in documents)
        {
            foreach (var diagnostic in await fixAllContext
                         .GetDocumentDiagnosticsAsync(document)
                         .ConfigureAwait(false))
            {
                work.Add((document, diagnostic));
            }
        }

        var sourceChanges = new Dictionary<DocumentId, List<TextChange>>();
        var additionalChanges = new Dictionary<DocumentId, List<TextChange>>();
        string? title = null;

        foreach (var (diagnosticDocument, diagnostic) in work)
        {
            fixAllContext.CancellationToken.ThrowIfCancellationRequested();

            var actions = new List<CodeAction>();
            var codeFixContext = new CodeFixContext(
                diagnosticDocument,
                diagnostic,
                (action, _) => actions.Add(action),
                fixAllContext.CancellationToken);
            await _provider.RegisterCodeFixesAsync(codeFixContext).ConfigureAwait(false);

            var matching = actions.FirstOrDefault(action =>
                action.EquivalenceKey == fixAllContext.CodeActionEquivalenceKey);
            if (matching is null)
            {
                continue;
            }

            title ??= matching.Title;
            var operations = await matching
                .GetOperationsAsync(fixAllContext.CancellationToken)
                .ConfigureAwait(false);
            foreach (var operation in operations.OfType<ApplyChangesOperation>())
            {
                CollectTextChanges(
                    solution,
                    operation.ChangedSolution,
                    sourceChanges,
                    additionalChanges,
                    fixAllContext.CancellationToken);
            }
        }

        if (title is null)
        {
            return null;
        }

        var merged = solution;
        foreach (var pair in sourceChanges)
        {
            var text = await merged.GetDocument(pair.Key)!
                .GetTextAsync(fixAllContext.CancellationToken)
                .ConfigureAwait(false);
            merged = merged.WithDocumentText(
                pair.Key,
                text.WithChanges(pair.Value.OrderBy(change => change.Span.Start).ToArray()));
        }

        foreach (var pair in additionalChanges)
        {
            var text = await merged.GetAdditionalDocument(pair.Key)!
                .GetTextAsync(fixAllContext.CancellationToken)
                .ConfigureAwait(false);
            merged = merged.WithAdditionalDocumentText(
                pair.Key,
                text.WithChanges(pair.Value.OrderBy(change => change.Span.Start).ToArray()));
        }

        return CodeAction.Create(
            title,
            _ => Task.FromResult(merged),
            equivalenceKey: fixAllContext.CodeActionEquivalenceKey);
    }

    private static void CollectTextChanges(
        Solution baseSolution,
        Solution changedSolution,
        Dictionary<DocumentId, List<TextChange>> sourceChanges,
        Dictionary<DocumentId, List<TextChange>> additionalChanges,
        CancellationToken cancellationToken)
    {
        foreach (var projectChanges in changedSolution.GetChanges(baseSolution).GetProjectChanges())
        {
            cancellationToken.ThrowIfCancellationRequested();
            CollectDocumentTextChanges(
                baseSolution,
                changedSolution,
                projectChanges.GetChangedDocuments(),
                sourceChanges,
                getDocument: (s, id) => s.GetDocument(id),
                cancellationToken);
            CollectDocumentTextChanges(
                baseSolution,
                changedSolution,
                projectChanges.GetChangedAdditionalDocuments(),
                additionalChanges,
                getDocument: (s, id) => s.GetAdditionalDocument(id),
                cancellationToken);
        }
    }

    private static void CollectDocumentTextChanges(
        Solution baseSolution,
        Solution changedSolution,
        IEnumerable<DocumentId> changedDocuments,
        Dictionary<DocumentId, List<TextChange>> changes,
        Func<Solution, DocumentId, TextDocument?> getDocument,
        CancellationToken cancellationToken)
    {
        foreach (var documentId in changedDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var changed = getDocument(changedSolution, documentId)!;
            var original = getDocument(baseSolution, documentId)!;

            IEnumerable<TextChange> textChanges;
            if (changed is Document changedDocument && original is Document originalDocument)
            {
                // The document-level diff preserves minimal edits for syntax-tree fixes;
                // a raw SourceText diff degenerates to a whole-file replace there.
                textChanges = changedDocument
                    .GetTextChangesAsync(originalDocument, cancellationToken)
                    .GetAwaiter().GetResult();
            }
            else
            {
                var newText = changed.GetTextAsync(cancellationToken).GetAwaiter().GetResult();
                var oldText = original.GetTextAsync(cancellationToken).GetAwaiter().GetResult();
                textChanges = newText.GetTextChanges(oldText);
            }

            if (!changes.TryGetValue(documentId, out var list))
            {
                changes[documentId] = list = new List<TextChange>();
            }

            foreach (var change in textChanges)
            {
                AddChange(list, change);
            }
        }
    }

    // Fixes are computed against the same base text, so overlapping replaces genuinely
    // conflict (two keys inserted into the same "{ }" section cannot both win). Pure
    // inserts at the same position compose; a conflicting replace is left for the next
    // Fix All iteration rather than merged into invalid text.
    private static void AddChange(List<TextChange> changes, TextChange change)
    {
        for (var i = 0; i < changes.Count; i++)
        {
            var existing = changes[i];
            if (!change.Span.IntersectsWith(existing.Span))
            {
                continue;
            }

            if (existing.Span == change.Span && existing.Span.Length == 0 &&
                existing.NewText != change.NewText)
            {
                changes[i] = new TextChange(existing.Span, existing.NewText + change.NewText);
            }

            return;
        }

        changes.Add(change);
    }
}
