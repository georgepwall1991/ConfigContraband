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
