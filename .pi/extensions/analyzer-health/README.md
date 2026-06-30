# Analyzer Health Audit Extension

Project-local pi extension for making `analyzer-health.md` refreshes repeatable and evidence-backed.

## Commands

- `/analyzer-health-audit [none|core|analyzer|codefix|full] [--no-write] [--health=path]`
  - Collects git, package-version, score-math, changed-file, and optional verifier evidence.
  - Writes a markdown report to `.pi/analyzer-health/audit-*.md` unless `--no-write` is passed.
  - Loads the report into the editor so it can be used as the evidence pack for updating `analyzer-health.md`.
- `/analyzer-health-validate`
  - Checks weighted score math and whether `Base audited commit` is an ancestor of current `HEAD`.
- `/analyzer-health-iterate [--dry-run] [--base=main] [--verification=full|analyzer|codefix|core|none] [--no-pr] [--no-release] [--auto-merge]`
  - Selects the highest-priority work from `Current Shortlist` (falling back to the lowest-score highest-priority Health Baseline row).
  - Requires a clean git tree and authenticated `gh`, updates the base branch, creates an `analyzer-health/...` branch, then sends pi a guarded implementation workflow.
  - The workflow requires evidence collection, targeted/full verification, analyzer-health updates, commit, PR, self-review, merge, and release tagging.
  - The generated prompt tells the agent to use body files or single-quoted heredocs for GitHub CLI markdown bodies so backticks are not executed by the shell.
  - By default it pauses for explicit user confirmation before merge/tag; use `--auto-merge` only when you want unattended merge/release after checks pass.

## Tool

- `collect_analyzer_health_evidence`
  - Callable by the agent before rescoring `analyzer-health.md`.
  - Parameters:
    - `healthPath`: optional path to the health file; defaults to `analyzer-health.md`.
    - `verification`: `none`, `core`, `analyzer`, `codefix`, or `full`; defaults to `none`.
    - `writeReport`: defaults to `true`.

## Intended workflow

1. Run `/analyzer-health-iterate --dry-run` to inspect the generated workflow prompt.
2. Run `/analyzer-health-iterate` to start the branch/implementation/PR/review/merge/release loop for the top `Current Shortlist` item.
3. Run `/analyzer-health-audit full` before a release-grade health refresh, or let the iteration workflow invoke `collect_analyzer_health_evidence`.
4. Use the generated evidence report to decide whether scores should move.
5. Run `/analyzer-health-validate` after editing `analyzer-health.md`.
6. Do not increase `Test Depth`, `Fix Safety`, or `Release Readiness` without verifier evidence.
