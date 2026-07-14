# CFG009 Direct Configuration Reads Hardening Design

**Date:** 2026-07-13  
**Status:** Approved for implementation  
**Target release:** 0.7.0

## Purpose

Harden the uncommitted CFG009 implementation before its first release. CFG009 enforces a visible-appsettings contract for direct `IConfiguration` reads while remaining conservative around locally owned or custom configuration providers.

The rule must not claim that a key is absent from every runtime provider. Its public wording will state that the requested path is not declared in the visible appsettings files supplied to the analyzer.

## Scope

This pass covers:

- semantic recognition of supported configuration APIs;
- configuration-receiver provenance;
- runtime section-existence modeling;
- duplicate-diagnostic suppression;
- precision for non-throwing binder reads;
- code-fix eligibility and safety;
- tests and release/trust artifacts for CFG009 and directly coupled CFG001 behavior.

It does not add new diagnostic IDs, change CFG009 severity, analyze dynamic paths, analyze indexer or `GetValue` reads, or broaden support to arbitrary stored `IConfigurationSection` values.

## Supported API behavior

CFG009 recognizes the real Microsoft configuration APIs in reduced extension syntax and legal static syntax:

- `ConfigurationExtensions.GetRequiredSection`;
- `ConfigurationExtensions.GetConnectionString`;
- supported `ConfigurationBinder.Get` overloads;
- supported non-keyed `ConfigurationBinder.Bind` overloads.

Arguments are matched by parameter symbol rather than syntax position, so named and reordered arguments behave consistently.

Every inferred `GetRequiredSection` chain link must resolve semantically to the framework extension. `IConfiguration.GetSection` implementations remain valid path segments, including custom interface implementations, but unrelated same-named methods do not contribute to a configuration path.

## Diagnostic policy

### Injected configuration

Injected `IConfiguration` and `IConfigurationRoot` receivers are evaluated against the visible appsettings files. Diagnostics mean “not declared in visible appsettings,” not “guaranteed absent at runtime.”

### Locally owned and custom configuration

CFG009 remains quiet when a receiver is proved to be locally owned or backed by a concrete custom provider. Proven local roots include:

- direct or local `ConfigurationBuilder.Build()` results;
- direct or local construction of the framework `ConfigurationManager` type;
- same-block assignments that establish one of those roots before the read;
- local alias chains whose source remains unambiguous.

The classifier is deliberately narrow. Ambiguous control flow, multiple reaching definitions, mutation, ref/out escape, closure capture, or cycles suppress CFG009 rather than guessing. Parameters, fields, and properties are not suppressed merely because their static type is `ConfigurationManager`.

### Throwing and non-throwing reads

- `GetRequiredSection` reports when the requested section does not exist under the modeled runtime contract.
- `Get<T>()` and non-keyed `Bind(instance)` report only when a close appsettings sibling provides typo evidence.
- `GetConnectionString` remains typo-evidence gated because environment or external providers commonly supply connection strings.
- Bare `GetSection`, dynamic paths, `GetValue`, indexers, keyed `Bind`, stored or parameter-typed `IConfigurationSection` receivers, and deeper conditional-access chains remain outside the rule’s supported scope.

### Cascades

A missing required parent produces one CFG009 at the earliest missing parent. Immediate conditional access is supported: an existing parent with a missing conditional child reports the child, while a missing parent does not also report its child.

## Analyzer architecture

### Invocation normalization

Add one private operation-based normalizer in `ConfigContrabandAnalyzer`. It consumes `IInvocationOperation` and returns:

- supported API kind;
- original framework method identity;
- effective receiver;
- parameter-matched key/name expression;
- diagnostic source expression;
- supported overload family.

Reduced and unreduced extension calls then share the same downstream analyzer path.

### Receiver provenance

Replace the direct syntax-only local-build guard with a conservative provenance classifier. The classifier follows local declarations, latest dominating same-block assignments, and local aliases with a visited-symbol set. It classifies proven local framework roots, proven concrete custom roots, contract receivers, and ambiguous receivers.

CFG009 is emitted only for contract receivers. Proven local/custom and ambiguous receivers remain quiet.

### Configuration model

Keep structural path lookup separate from runtime existence:

- structural lookup supports traversal and sibling suggestions;
- runtime existence supports `GetRequiredSection` and required-parent decisions.

Preserve whether a parsed node is an object, array, scalar, or explicit null, together with value/descendant information. The existence query must handle referenced configuration-provider versions conservatively. Version-invariant missing shapes may report; version-sensitive null or empty-array shapes remain quiet when the provider version cannot be established.

The dedicated existence query is also applied to the coupled CFG001 registration path so registration deduplication cannot hide an empty-section runtime failure.

## Code-fix design

Continue using the shared CFG001/CFG009 literal-replacement engine, but make fix eligibility explicit in diagnostic properties.

A fix is offered only when:

- a deterministic close sibling exists;
- the replacement differs from the source key under the relevant comparison;
- the invocation and receiver were eligible for automatic repair;
- replacing the key expression preserves the surrounding invocation form.

Static calls anchor the actual key argument. Locally owned/custom-provider diagnostics are suppressed before fix registration. Exact declared paths that fail runtime existence must never produce identity or no-op fixes.

## Test strategy

Use red-green-refactor in focused slices.

1. Add failing semantic-call tests for static `GetRequiredSection`, `GetConnectionString`, `Get`, and `Bind`, including named/reordered arguments and a static-call code fix.
2. Add failing chain-identity tests for user-defined same-named instance and extension methods consumed by real framework calls.
3. Add provenance tests for local/direct `ConfigurationManager`, builder assignments, injected-to-local and local-to-injected reassignments, one- and two-hop aliases, and injected `ConfigurationManager` guardrails.
4. Add immediate conditional-access cascade tests.
5. Add core model tests for object, array, scalar, and null shape plus runtime-existence behavior; add analyzer tests for empty object, null, empty array, and object-with-child.
6. Add a coupled CFG001 regression proving an empty required section produces exactly one diagnostic.
7. Add optional-binding tests proving generic absent names with fallback/default behavior stay quiet while near-match typos still report and fix safely.
8. Add no-op and ineligible-fix guardrails.

Run per-file `dotnet_diagnostics` before builds or tests. Final verification includes the focused slices, full build and test suite, warnings-as-errors CI build, format verification, `git diff --check`, package creation and nuspec inspection, and Codex cross-review.

## Trust and release artifacts

Before release:

- set both package projects to version 0.7.0 consistently;
- make package release notes describe CFG009 rather than CFG008;
- move CFG008 from unshipped to the shipped 0.6.0 analyzer-release record;
- keep CFG009 unshipped until release preparation is complete;
- describe 0.7.0 as unreleased/in development until its tag exists;
- update README feature summaries, supported-scope wording, and installation version;
- refresh analyzer-health metadata only after final verification;
- ensure changelog, package metadata, README, analyzer-release files, and tag agree.

Add or strengthen a publish-time consistency check if the existing workflow can enforce these invariants narrowly without introducing an unrelated release subsystem.

## Completion criteria

The work is complete when:

- all verified CFG009 gaps above have regression coverage and pass;
- no changed C# file has LSP diagnostics;
- full build, full tests, formatting, diff checks, and package inspection pass;
- Codex reports no actionable findings and explicitly judges the result a meaningful precision improvement;
- the reviewed work is committed and merged into `main`;
- release `v0.7.0` is tagged and its publish workflow succeeds.
