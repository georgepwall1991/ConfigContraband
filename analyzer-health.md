# Analyzer Health

This file tracks ConfigContraband rule maturity and drives iterative hardening work. Use it to choose the next focused analyzer improvement, update the score after each change, and keep release readiness visible.

## Scoring Rubric

Each category is scored from 1 to 5. Higher is better except `Importance`, where higher means the rule is more valuable to harden.

| Category | Weight | 1 | 3 | 5 |
|---|---:|---|---|---|
| Importance | 25% | Cosmetic or rare case | Common correctness issue | High-impact production failure class |
| Precision | 25% | Broad or likely false positives | Conservative for common cases | Strong safe/unsafe boundaries |
| Test Depth | 20% | Happy path only | Main positive and negative cases | Edge cases, aliases, duplicates, and regressions |
| Fix Safety | 15% | Unsafe or missing where expected | Fix exists but needs edge coverage, or manual rule is documented | Fix is deterministic, narrow, and regression-tested |
| Documentation | 10% | Undocumented behavior | README/catalog summary exists | Safe/risky shapes and no-fix rationale are documented |
| Release Readiness | 5% | Blocks shipping | Minor gaps only | CI/verifier/docs are current |

Overall score:

```text
Importance * 0.25 + Precision * 0.25 + TestDepth * 0.20 + FixSafety * 0.15 + Documentation * 0.10 + ReleaseReadiness * 0.05
```

## Health Baseline

| Rule | Severity | Importance | Precision | Test Depth | Fix Safety | Docs | Release | Score | Priority | Status |
|---|---|---:|---:|---:|---:|---:|---:|---:|---|---|
| CFG001 Missing configuration section | Warning | 5 | 4 | 4 | 4 | 4 | 4 | 4.25 | P1 | Handles nested section-path suggestions with full-path fixes and treats appsettings files as a visible set for section lookup. |
| CFG003 Validation not on startup | Warning | 4 | 4 | 4 | 4 | 3 | 4 | 3.90 | P1 | Handles fluent and immediate split local validation chains with deterministic fixes. |
| CFG004 DataAnnotations not enabled | Warning | 4 | 4 | 4 | 4 | 3 | 4 | 3.90 | P1 | Handles fluent and immediate split local startup-validation chains with deterministic fixes. |
| CFG005 Nested validation not recursive | Warning | 5 | 4 | 4 | 4 | 4 | 4 | 4.25 | P1 | Recurses through nested object graphs, honors existing recursive attributes, and covers arrays, nullable properties, collections, and interface boundaries. |
| CFG006 Unknown configuration key | Info | 4 | 4 | 5 | 5 | 4 | 4 | 4.35 | P2 | Checks nested object-array keys across every matching appsettings section while leaving scalar arrays and dictionary entries conservative. |

## Selection Policy

Pick one focused rule per hardening batch unless two rules share the same helper boundary. Prefer:

1. Warning rules before Info rules when scores are close.
2. High `Importance` with low `Precision` or `Test Depth`.
3. Fixable rules with unproven code fixes before manual-only documentation cleanup.
4. Small changes that can be verified with targeted tests.

Do not raise severity, rename analyzer IDs, or broaden diagnostics unless tests prove the behavior is safe.

## Current Shortlist

1. `CFG004` inherited DataAnnotations follow-up:
   - Add inherited-property coverage.
   - Keep code fixes narrow when validation chains are split across locals.

2. `CFG001` duplicate-section follow-up:
   - Add explicit duplicate-section tests if future merge semantics become more precise.
   - Preserve conservative diagnostics when no appsettings files are available.

3. `CFG006` dictionary-value follow-up:
   - Consider whether strongly typed dictionary values can be checked without reporting arbitrary dictionary entry names.
   - Keep as Info because configuration binding remains intentionally flexible.

## Rule Notes

### CFG001 Missing Configuration Section

Reports when a string literal passed to `BindConfiguration()` does not exist in available `appsettings*.json` files. Nested section paths are matched segment by segment, and the code fix replaces the section literal with the full corrected path when a close sibling section name is found.

Known gaps:

- Multi-file section lookup treats visible appsettings files as one searchable set for section existence; it does not model provider ordering.
- Explicit duplicate-section tests can be added if future merge semantics become more precise.

### CFG003 Validation Not On Startup

Reports when an options registration has validation but no `ValidateOnStart()`.

Known gaps:

- More complex control-flow and non-local `OptionsBuilder<T>` storage are intentionally not inferred.
- Code fix formatting is tested for fluent and immediate split chains, but not broadly across every multi-line shape.
- Documentation should explain why lazy validation is risky.

### CFG004 DataAnnotations Not Enabled

Reports when an options type uses DataAnnotations but the registration does not call `ValidateDataAnnotations()`.

Known gaps:

- More tests are needed for inherited properties and non-bindable annotated properties.
- Code fix should continue to avoid adding duplicate `ValidateOnStart()` across more chain shapes.
- Documentation distinguishes DataAnnotations from custom validation delegates, but inherited DataAnnotations need examples.

### CFG005 Nested Validation Not Recursive

Reports when nested object or collection item types contain validation attributes but the parent property lacks the required recursive validation attribute.

Known gaps:

- Cross-document code-fix behavior is supported by the implementation but still needs a dedicated regression test when the test harness grows multi-document coverage.
- More complex custom recursive validation patterns are intentionally not inferred.

### CFG006 Unknown Configuration Key

Reports an appsettings key under a bound section when it does not match any bindable options property. This rule is currently informational because configuration binding has flexible shapes and false-positive risk is higher.

Known gaps:

- Object arrays and lists are checked recursively, and scalar arrays are treated as values.
- Visible appsettings files are treated as a merged view for unknown-key checks; every matching bound section can produce diagnostics.
- Dictionary entry names are intentionally treated as dynamic keys and are not reported as unknown properties.
- Strongly typed dictionary values may become checkable later if the analyzer can avoid flagging dynamic entry names.

## Verification Baseline

Focused analyzer test command:

```bash
dotnet test tests/ConfigContraband.Tests/ConfigContraband.Tests.csproj --configuration Release --filter ConfigContrabandAnalyzerTests
```

Focused code-fix test command:

```bash
dotnet test tests/ConfigContraband.Tests/ConfigContraband.Tests.csproj --configuration Release --filter ConfigContrabandCodeFixTests
```

Full local verification:

```bash
dotnet test ConfigContraband.slnx --configuration Release
git diff --check
```

CI verification is defined in `.github/workflows/ci.yml` and runs restore, build, test, pack, and artifact upload against the SDK from `global.json`.
