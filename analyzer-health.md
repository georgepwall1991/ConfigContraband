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
| CFG001 Missing configuration section | Warning | 5 | 4 | 3 | 4 | 2 | 4 | 4.00 | P1 | Useful and shippable, but needs more section-path and multi-file tests. |
| CFG003 Validation not on startup | Warning | 4 | 3 | 2 | 3 | 2 | 4 | 3.15 | P1 | Needs chain-shape coverage and fix formatting hardening. |
| CFG004 DataAnnotations not enabled | Warning | 4 | 3 | 2 | 3 | 2 | 4 | 3.15 | P1 | Needs boundary tests for non-options annotations and existing `ValidateOnStart()`. |
| CFG005 Nested validation not recursive | Warning | 5 | 3 | 3 | 3 | 2 | 4 | 3.55 | P1 | High value; needs recursive/nested depth and existing-attribute safety coverage. |
| CFG006 Unknown configuration key | Info | 4 | 3 | 4 | 5 | 2 | 4 | 3.65 | P2 | Good test base after nested-key work; needs arrays, dictionaries, and multi-file merge semantics. |

## Selection Policy

Pick one focused rule per hardening batch unless two rules share the same helper boundary. Prefer:

1. Warning rules before Info rules when scores are close.
2. High `Importance` with low `Precision` or `Test Depth`.
3. Fixable rules with unproven code fixes before manual-only documentation cleanup.
4. Small changes that can be verified with targeted tests.

Do not raise severity, rename analyzer IDs, or broaden diagnostics unless tests prove the behavior is safe.

## Current Shortlist

1. `CFG003` and `CFG004` invocation-chain hardening:
   - Add tests for registrations split across fluent chains and terminal invocations.
   - Ensure fix output remains compilable and deterministic.
   - Confirm custom validation delegates count for `CFG003` without triggering `CFG004`.

2. `CFG005` nested validation precision:
   - Add tests for already annotated nested object and collection properties.
   - Add tests for recursive depth, nullable nested properties, arrays, and interfaces.
   - Keep fixer narrow to adding only the missing recursive validation attribute.

3. `CFG001` section lookup hardening:
   - Add nested section-path tests such as `Features:Stripe`.
   - Add multi-file appsettings coverage and duplicate-section behavior.
   - Preserve conservative diagnostics when no appsettings files are available.

4. `CFG006` nested key follow-up:
   - Add array and dictionary boundary tests.
   - Decide and document whether multiple appsettings files are treated as merged configuration or independent snapshots.
   - Keep as Info until false-positive boundaries are stronger.

## Rule Notes

### CFG001 Missing Configuration Section

Reports when a string literal passed to `BindConfiguration()` does not exist in available `appsettings*.json` files. The code fix replaces the section literal when a close sibling section name is found.

Known gaps:

- Multi-file configuration behavior is not documented.
- Nested section suggestions need explicit tests.
- The README summarizes the rule but does not document safe and risky shapes.

### CFG003 Validation Not On Startup

Reports when an options registration has validation but no `ValidateOnStart()`.

Known gaps:

- Fluent chain parsing needs more coverage for unusual but valid chain shapes.
- Code fix formatting is tested, but not broadly across multi-line and already chained invocations.
- Documentation should explain why lazy validation is risky.

### CFG004 DataAnnotations Not Enabled

Reports when an options type uses DataAnnotations but the registration does not call `ValidateDataAnnotations()`.

Known gaps:

- More tests are needed for inherited properties and non-bindable annotated properties.
- Code fix should continue to avoid adding duplicate `ValidateOnStart()`.
- Documentation should distinguish DataAnnotations from custom validation delegates.

### CFG005 Nested Validation Not Recursive

Reports when nested object or collection item types contain validation attributes but the parent property lacks the required recursive validation attribute.

Known gaps:

- Existing `ValidateObjectMembers` and `ValidateEnumeratedItems` safe cases need explicit regression tests.
- Recursive depth and nullable nested object behavior need coverage.
- Fixer should be verified for properties declared in separate documents when supported by tests.

### CFG006 Unknown Configuration Key

Reports an appsettings key under a bound section when it does not match any bindable options property. This rule is currently informational because configuration binding has flexible shapes and false-positive risk is higher.

Known gaps:

- Arrays and dictionaries need explicit boundary tests.
- Multi-file configuration merge semantics need a design decision.
- Documentation should explain why the rule is Info and when it may be suppressed.

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
