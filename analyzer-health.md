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
| CFG001 Missing configuration section | Warning | 5 | 5 | 5 | 4 | 4 | 4 | 4.70 | P1 | Handles nested section-path suggestions with full-path fixes, traverses duplicate JSON section members, and treats appsettings files as a visible set for section lookup. |
| CFG003 Validation not on startup | Warning | 4 | 4 | 4 | 4 | 3 | 4 | 3.90 | P1 | Handles fluent and immediate split local validation chains with deterministic fixes. |
| CFG004 DataAnnotations not enabled | Warning | 4 | 4 | 5 | 4 | 4 | 4 | 4.20 | P1 | Handles inherited bindable DataAnnotations and fluent or immediate split local startup-validation chains with deterministic fixes. |
| CFG005 Nested validation not recursive | Warning | 5 | 4 | 4 | 4 | 4 | 4 | 4.25 | P1 | Recurses through nested object graphs, honors existing recursive attributes, and covers arrays, nullable properties, collections, and interface boundaries. |
| CFG006 Unknown configuration key | Info | 4 | 5 | 5 | 5 | 4 | 5 | 4.65 | P2 | Checks nested object arrays, strongly typed dictionary-value keys, and dictionary values containing nested object collections across every matching appsettings section while leaving scalar arrays and dynamic dictionary entry names conservative. |

## Selection Policy

Pick one focused rule per hardening batch unless two rules share the same helper boundary. Prefer:

1. Warning rules before Info rules when scores are close.
2. High `Importance` with low `Precision` or `Test Depth`.
3. Fixable rules with unproven code fixes before manual-only documentation cleanup.
4. Small changes that can be verified with targeted tests.

Do not raise severity, rename analyzer IDs, or broaden diagnostics unless tests prove the behavior is safe.

## Current Shortlist

1. Re-audit `CFG003` and `CFG004` code-fix formatting across multi-line chains before broadening diagnostics.
2. Keep `CFG006` as Info and add only narrowly proven binding-shape coverage if real-world appsettings shapes expose another safe gap.

## Rule Notes

### CFG001 Missing Configuration Section

Reports when a string literal passed to `BindConfiguration()` does not exist in available `appsettings*.json` files. Nested section paths are matched segment by segment, and the code fix replaces the section literal with the full corrected path when a close sibling section name is found.

Known gaps:

- Multi-file section lookup treats visible appsettings files as one searchable set for section existence; it does not model provider ordering.

Current behavior:

- Duplicate JSON section members are traversed when resolving nested section paths and typo suggestions.

### CFG003 Validation Not On Startup

Reports when an options registration has validation but no `ValidateOnStart()`.

Known gaps:

- More complex control-flow and non-local `OptionsBuilder<T>` storage are intentionally not inferred.
- Code fix formatting is tested for fluent and immediate split chains, but not broadly across every multi-line shape.
- Documentation should explain why lazy validation is risky.

### CFG004 DataAnnotations Not Enabled

Reports when an options type uses DataAnnotations but the registration does not call `ValidateDataAnnotations()`.

Known gaps:

- Inherited bindable properties with DataAnnotations are included when deciding whether `ValidateDataAnnotations()` is required.
- Non-bindable annotated properties remain ignored to match options binding behavior.
- Code fix should continue to avoid adding duplicate `ValidateOnStart()` across more chain shapes.
- Documentation distinguishes DataAnnotations from custom validation delegates and inherited annotation shapes.

### CFG005 Nested Validation Not Recursive

Reports when nested object or collection item types contain validation attributes but the parent property lacks the required recursive validation attribute.

Known gaps:

- Cross-document code-fix behavior is supported by the implementation but still needs a dedicated regression test when the test harness grows multi-document coverage.
- More complex custom recursive validation patterns are intentionally not inferred.

### CFG006 Unknown Configuration Key

Reports an appsettings key under a bound section when it does not match any bindable options property. This rule is currently informational because configuration binding has flexible shapes and false-positive risk is higher.

Known gaps:

- Object arrays, lists, strongly typed dictionary values, and dictionary values containing nested object collections are checked recursively, while scalar arrays are treated as values.
- Visible appsettings files are treated as a merged view for unknown-key checks; every matching bound section can produce diagnostics.
- Dictionary entry names are intentionally treated as dynamic keys and are not reported as unknown properties.

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
