# ConfigContraband (aka ConfiContraband / LinqContraband)

Roslyn analyzer for .NET configuration/Options validation. Rule IDs are `CFG0xx` (CFG001–CFG009) —
not `DI0xx`; that prefix belongs to a different project/skill template.

- Source of truth for current rule health, precision/recall scores, and known gaps:
  `analyzer-health.md`. Check it before assuming a limitation is a bug rather than a documented,
  deliberate scope boundary.
- Checklists for adding a new rule vs. hardening an existing one: `CONTRIBUTING.md`.
- Rule behavior/scope is documented per-rule in `README.md` under "Rule Details" and "Current Scope".
- LSP note: this repo has only `ConfigContraband.slnx` (no `.sln`), and csharp-ls mis-resolves
  cross-project references when loading it (phantom CS0246/CS1503 despite a clean build). Use
  per-file `dotnet_diagnostics` (accurate — roots at the nearest `.csproj`) and `dotnet build`
  for solution-wide truth; treat `dotnet_workspace_diagnostics` output here as suspect.
