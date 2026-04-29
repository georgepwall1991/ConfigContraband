# Analyzer Health

This file tracks the current ConfigContraband analyzer surface and the next hardening work that is still worth doing. It should stay practical: scores drive priority, notes describe shipped behavior, and gaps should be specific enough to turn into a focused PR.

Last refreshed: 2026-04-29
Package version: `0.1.7`
Base audited commit: `9719088`

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

Priority means "next investment priority", not diagnostic severity:

| Priority | Meaning |
|---|---|
| P1 | Best next hardening target. |
| P2 | Worth improving after P1 work or when touching the shared helper. |
| P3 | Healthy enough; monitor for real-world gaps. |

## Current Posture

The analyzer has a compact, coherent rule set: five diagnostics, four code-fix families, `BindConfiguration(...)`, explicit `Bind(GetSection(...))`, direct `Configure<T>(GetSection(...))` section/key checks, `buildTransitive` appsettings discovery, README rule documentation, changelog/version metadata, and CI that restores, builds, tests, packs, uploads test results, and uploads packages. The best next improvements should now come from real-world edge cases or adoption polish, not speculative rule widening.

## Health Baseline

| Rule | Severity | Importance | Precision | Test Depth | Fix Safety | Docs | Release | Score | Priority | Current read |
|---|---|---:|---:|---:|---:|---:|---:|---:|---|---|
| CFG001 Missing configuration section | Warning | 5 | 5 | 5 | 4 | 5 | 5 | 4.85 | P3 | Strong current shape. Handles `BindConfiguration(...)`, `Bind(GetSection(...))`, direct `Configure<T>(GetSection(...))`, nested section paths, full-path suggestions, duplicate JSON section members, commented appsettings files, and visible appsettings files as one searchable set. |
| CFG003 Validation not on startup | Warning | 4 | 5 | 5 | 4 | 5 | 5 | 4.60 | P3 | Good analyzer boundary for fluent and immediate same-block local `OptionsBuilder<T>` chains, including `Bind(GetSection(...))`; honors `AddOptionsWithValidateOnStart<TOptions>()` and code fixes preserve multiline formatting, comments, split locals, and single-line chains. |
| CFG004 DataAnnotations not enabled | Warning | 4 | 5 | 5 | 4 | 5 | 5 | 4.60 | P3 | Covers inherited bindable DataAnnotations on supported `OptionsBuilder<T>` bindings, avoids duplicate `ValidateOnStart()` when startup validation already exists, and shares the formatter-safe invocation appender with CFG003. |
| CFG005 Nested validation not recursive | Warning | 5 | 4 | 5 | 5 | 5 | 5 | 4.75 | P3 | Strong current shape. Covers recursive object and collection graphs on supported `OptionsBuilder<T>` bindings, suppresses unsafe interface cases, and proves cross-document recursive-attribute fixes. |
| CFG006 Unknown configuration key | Info | 4 | 4 | 5 | 5 | 5 | 5 | 4.50 | P3 | Broadest test depth. Covers `BindConfiguration(...)`, `Bind(GetSection(...))`, and direct `Configure<T>(GetSection(...))`; recurses through nested objects, object collections, dictionary values, dictionary values containing collections, and commented appsettings files while keeping scalar arrays and dictionary entry names quiet. |

## Selection Policy

Pick one focused rule per hardening batch unless two rules share the same helper boundary. Prefer:

1. Warning rules before Info rules when scores are close.
2. High `Importance` with low `Precision`, `Test Depth`, or `Fix Safety`.
3. Fixable rules with unproven code fixes before manual-only documentation cleanup.
4. Small changes that can be verified with targeted tests.

Do not raise severity, rename analyzer IDs, or broaden diagnostics unless tests prove the behavior is safe.

## Current Shortlist

1. Keep `CFG006` informational. Only add new binding-shape coverage when there is a concrete, narrow shape that can be proven without reporting dynamic configuration data as an unknown property.
2. Keep direct `Configure<T>(...)` validation diagnostics out of scope until there is a dedicated, conservative design for named options and separate validation registrations.
3. Keep `CFG005` in monitor mode. Future code-fix work should be driven by concrete formatter regressions or new recursive-validation APIs.
4. Keep `CFG003` and `CFG004` in monitor mode unless real-world chains expose another formatter or framework-registration edge case.
5. Keep `CFG001` in monitor mode. Future work should be driven by real appsettings/provider-order bugs, not by widening static inference.

## Rule Notes

### CFG001 Missing Configuration Section

Reports when a supported options binding references a string-literal section path that does not exist in visible `appsettings*.json` files. Nested section paths are matched segment by segment. The code fix replaces the literal with the corrected section path, or the corrected leaf when the code uses chained `GetSection(...)` calls.

Current behavior:

- Checks top-level and nested section paths across all visible `appsettings*.json` additional files for `BindConfiguration(...)`, `Bind(GetSection(...))`, `Bind(GetRequiredSection(...))`, and direct `Configure<T>(GetSection(...))`.
- Parses `//` and `/* ... */` comments in appsettings files before resolving section paths.
- Traverses duplicate JSON object members when resolving section existence and suggestions.
- Ignores non-constant, empty, whitespace-only, root configuration, and stored `IConfigurationSection` values.

Known gaps:

- Treats visible appsettings files as one searchable set and does not model configuration-provider ordering.
- Does not infer dynamic section names.

### CFG003 Validation Not On Startup

Reports when an options registration has validation through `ValidateDataAnnotations()` or `Validate(...)` but no `ValidateOnStart()`.

Current behavior:

- Tracks normal fluent chains after `BindConfiguration(...)` and `Bind(GetSection(...))`.
- Tracks immediate same-block local `OptionsBuilder<T>` calls until an unrelated statement breaks the sequence.
- Treats `AddOptionsWithValidateOnStart<TOptions>()` as startup validation, so registrations using the framework helper do not need an extra `ValidateOnStart()` call.
- Offers a fix that appends `ValidateOnStart()` while preserving multiline chain indentation, comments, split locals, and single-line chains.

Known gaps:

- Does not infer non-local builder storage, aliases, reassignment, or broader control flow.
- Future code-fix work should be driven by concrete formatter regressions rather than speculative chain shapes.
- Documentation explains the rule, but could be clearer about why lazy options validation is operationally risky.

### CFG004 DataAnnotations Not Enabled

Reports when an options type has bindable DataAnnotations properties but the options registration does not call `ValidateDataAnnotations()`.

Current behavior:

- Includes inherited public bindable properties.
- Honors public settable property boundaries to stay aligned with options binding.
- Treats custom `Validate(...)` as validation for `CFG003`, but not as a substitute for `ValidateDataAnnotations()`.
- Offers a fix that appends `ValidateDataAnnotations()` and appends `ValidateOnStart()` only when startup validation is missing, using the formatter-safe chain appender shared with `CFG003`.
- Does not append `ValidateOnStart()` when the registration started with `AddOptionsWithValidateOnStart<TOptions>()`.

Known gaps:

- Future code-fix work should be driven by concrete formatter regressions rather than speculative chain shapes.
- Does not infer annotations on non-bindable members, which is intentional but worth keeping explicit in docs.

### CFG005 Nested Validation Not Recursive

Reports when a nested object or collection item type contains validation attributes but the parent property is missing the required recursive validation attribute.

Current behavior:

- Finds nested object graphs and nested collection graphs that contain DataAnnotations.
- Covers arrays, `IEnumerable<T>` shapes, nullable nested properties, and deep nested properties.
- Suppresses interface-typed nested properties and already annotated recursive-validation properties.
- Offers fixes for `[ValidateObjectMembers]` and `[ValidateEnumeratedItems]`, including options properties declared in a different source document from the registration diagnostic.

Known gaps:

- More complex custom recursive validation patterns are intentionally not inferred.

### CFG006 Unknown Configuration Key

Reports an appsettings key under a bound section when the key does not match a bindable options property or `[ConfigurationKeyName]` alias. This rule stays informational because configuration binding is flexible and false-positive cost is higher.

Current behavior:

- Checks every matching bound section across visible appsettings files for supported `BindConfiguration(...)`, `Bind(GetSection(...))`, and direct `Configure<T>(GetSection(...))` registrations.
- Parses `//` and `/* ... */` comments before walking keys so commented local appsettings files stay analyzable.
- Recurses into nested object properties, arrays/lists of nested objects, strongly typed dictionary values, and dictionary values containing nested object collections.
- Honors `[ConfigurationKeyName]` aliases at the root and nested levels.
- Treats scalar array items and dictionary entry names as values rather than property names.

Known gaps:

- Does not model provider precedence or environment-specific override intent.
- Should not become a warning unless real-world coverage proves the false-positive profile stays low.

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

CI verification is defined in `.github/workflows/ci.yml` and runs restore, build, test with coverage, pack, test-result upload, and package artifact upload against the SDK from `global.json`.
