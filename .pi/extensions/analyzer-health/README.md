# Analyzer Health Audit Extension

Project-local pi extension for making `analyzer-health.md` refreshes repeatable and evidence-backed.

## Commands

- `/analyzer-health-audit [none|core|analyzer|codefix|full] [--no-write] [--health=path]`
  - Collects git, package-version, score-math, changed-file, and optional verifier evidence.
  - Writes a markdown report to `.pi/analyzer-health/audit-*.md` unless `--no-write` is passed.
  - Loads the report into the editor so it can be used as the evidence pack for updating `analyzer-health.md`.
- `/analyzer-health-validate`
  - Checks weighted score math and whether `Base audited commit` matches current `HEAD`.

## Tool

- `collect_analyzer_health_evidence`
  - Callable by the agent before rescoring `analyzer-health.md`.
  - Parameters:
    - `healthPath`: optional path to the health file; defaults to `analyzer-health.md`.
    - `verification`: `none`, `core`, `analyzer`, `codefix`, or `full`; defaults to `none`.
    - `writeReport`: defaults to `true`.

## Intended workflow

1. Run `/analyzer-health-audit full` before a release-grade health refresh.
2. Use the generated evidence report to decide whether scores should move.
3. Run `/analyzer-health-validate` after editing `analyzer-health.md`.
4. Do not increase `Test Depth`, `Fix Safety`, or `Release Readiness` without verifier evidence.
