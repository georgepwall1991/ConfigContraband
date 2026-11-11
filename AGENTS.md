# ConfigContraband (aka ConfiContraband / LinqContraband)

Roslyn analyzer for .NET configuration/Options validation. Rule IDs are `CFG0xx` (CFG001–CFG010) —
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

## Cursor Cloud specific instructions

Environment is a .NET 10 SDK project (pinned to `10.0.203` by `global.json`). The SDK is installed
at `~/.dotnet` and added to `PATH` for interactive shells via `~/.bashrc`. Non-interactive scripts
do not source `~/.bashrc`, so invoke the full path `~/.dotnet/dotnet` when `dotnet` is not already on
`PATH`. The startup update script only refreshes dependencies (`dotnet restore ConfigContraband.slnx`
and `dotnet tool restore`); the SDK itself persists in the VM snapshot.

- Build/test/pack commands are in `CONTRIBUTING.md`; the exact CI sequence (build, verify scripts,
  format, test, pack) lives in `.github/workflows/ci.yml`. Prefer those over ad-hoc commands.
- Gotcha (most important): analyzers are disabled during CLI builds by `Directory.Build.props`
  (`RunAnalyzersDuringBuild=false`) for a fast local loop. To actually see/reproduce `CFG001`–`CFG009`
  diagnostics from the command line you MUST pass `-p:ContinuousIntegrationBuild=true` (CI does this).
  In-editor/LSP builds still run analyzers live.
- Lint gate is `dotnet format ConfigContraband.slnx --verify-no-changes`. CSharpier is present as a
  local tool but is intentionally a no-op here (`.csharpierignore` ignores everything) — do not rely
  on it to format; use `dotnet format`.
- The two `samples/` projects (Showcase, Quickstart) are deliberately outside the solution so normal
  builds stay clean. Exercise the analyzer end-to-end with `scripts/verify-showcase.sh` (expects one
  of each CFG rule) and `scripts/verify-quickstart.sh` (expects zero CFG diagnostics).
- The companion CLI (`ConfigContraband.Tool`, command `configcontraband`) generates
  `appsettings.schema.json` from Options types. Run it in-repo with
  `dotnet run --project src/ConfigContraband.Tool -- schema --project <path-to.csproj>` (add
  `--output <path>` to avoid writing next to the target project, `--check` for CI drift checks).
