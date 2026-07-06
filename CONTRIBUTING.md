# Contributing

## Prerequisites

- .NET SDK 10.0.203 or newer compatible feature band.
- GitHub CLI for repository publishing and release work.

## Build

```bash
dotnet restore ConfigContraband.slnx
dotnet build ConfigContraband.slnx
dotnet test ConfigContraband.slnx
dotnet pack src/ConfigContraband/ConfigContraband.csproj -c Release
```

## Analyzer Rules

Rules live in `src/ConfigContraband`. Add or change tests in `tests/ConfigContraband.Tests` for every diagnostic and code fix behavior.

When adding a rule, update:

- `DiagnosticIds.cs`
- `DiagnosticDescriptors.cs`
- `AnalyzerReleases.Unshipped.md`
- `README.md`
- analyzer and code-fix tests

## Hardening an Existing Rule

Most changes to this repo aren't new rules — they're precision/recall fixes to an existing `CFG0xx` rule found during a monitor sweep of `analyzer-health.md`. For those:

1. Write a failing test in `tests/ConfigContraband.Tests` that reproduces the false positive/negative.
2. Harden the analyzer (or code fix) with the minimum change needed to make the test pass.
3. Update the rule's entry in `analyzer-health.md` — its score(s) and the relevant `Rule Notes`/`Known gaps` bullets — so the health doc reflects the new behavior.
4. Run a Codex review (`codex review --uncommitted`) before committing; treat actionable findings as blocking, same as a failing test.
