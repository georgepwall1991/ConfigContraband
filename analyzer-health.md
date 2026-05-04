# Analyzer Health

This file tracks the current ConfigContraband analyzer surface and the next hardening work that is still worth doing. It should stay practical: scores drive priority, notes describe shipped behavior, and gaps should be specific enough to turn into a focused PR.

Last refreshed: 2026-05-04
Package version: `0.1.11`
Base audited commit: `303ace0`

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

The analyzer has a compact, coherent rule set: five diagnostics, four code-fix families, `BindConfiguration(...)`, explicit `Bind(GetSection(...))`, direct `Configure<T>(GetSection(...))` section/key checks, fluent-chain validation checks before and after binding calls, `buildTransitive` appsettings discovery, README rule documentation, changelog/version metadata, and CI that restores, builds, tests, packs, uploads test results, and uploads packages. The best next improvements should now come from real-world edge cases or adoption polish, not speculative rule widening.

## Improvement Changelog

| Date | Iteration | Rules | Finding | Change | Rating impact |
|---|---|---|---|---|---|
| 2026-05-03 | 1 | `CFG003`, `CFG004` | The receiver side of a fluent `OptionsBuilder<T>` chain was not included when collecting validation methods, so `ValidateDataAnnotations()` or `ValidateOnStart()` before `BindConfiguration(...)` could create a false positive or false negative. | Collect same-chain receiver invocations before the binding call and added regression coverage for pre-bind validation/startup calls. | Local pre-fix read: `4.35` for both rules if precision is scored as `4`; post-fix read returns both to `4.60` with precision `5`. |
| 2026-05-03 | 2 | `CFG001`, `CFG006` | The appsettings parser decoded common string escapes but not JSON `\uXXXX` escapes, so escaped section or property names could be treated as missing/unknown even though the runtime provider decodes them. | Decode four-digit unicode escapes in JSON strings and added parser plus analyzer regressions for escaped section and property names. | `CFG001` stays `4.85`; `CFG006` stays `4.50`, but both have stronger parser-edge coverage under Test Depth `5`. |
| 2026-05-03 | 3 | `CFG005`, `CFG006` | The nested-options type filter excluded every namespace beginning with `System`, including user namespaces such as `Systematic.Options`. That could hide recursive-validation and nested-key diagnostics for user types. | Exclude only `System` and `System.*` namespaces, with analyzer regressions for nested user types in a `Systematic.*` namespace. | `CFG005` precision remains `4` and score remains `4.75`; `CFG006` remains `4.50`, with a narrower false-negative boundary. |
| 2026-05-03 | 4 | `CFG001`, `CFG006` | JSON configuration is flattened with `:` as the hierarchy delimiter, but the analyzer only walked object nesting. Colon-delimited appsettings keys such as `"Features:Stripe"` could be treated as missing sections, and flattened leaf keys could hide unknown-key typos. | Project colon-delimited appsettings keys into the same hierarchy used by section lookup, suggestions, and unknown-key analysis, with object-key and leaf-key regressions. | `CFG001` stays `4.85`; `CFG006` stays `4.50`, with stronger alignment to runtime configuration-key semantics. |
| 2026-05-04 | 5 | `CFG003`, `CFG004` | Fluent-chain validation tracking used method names only, so custom same-name extensions could hide missing startup/DataAnnotations diagnostics or make a non-validation helper look like validation. | Symbol-check recognized `OptionsBuilder<TOptions>` validation APIs while preserving receiver, fluent, and local-chain walking; added custom-extension regressions. | Both rules stay at `4.60`, with a tighter precision boundary around framework validation methods. |
| 2026-05-04 | 6 | `CFG005` | The recursive-validation code fix only checked compilation-unit usings, so namespace-local `Microsoft.Extensions.Options` imports could be ignored and namespace-styled files could receive a redundant top-level using. | Reuse applicable namespace-local imports and add `Microsoft.Extensions.Options` to the namespace-local using block when that is the file style, with cross-document code-fix regressions. | `CFG005` stays `4.75`, with stronger fixer polish for real project file layouts. |
| 2026-05-04 | 7 | `CFG005` | The recursive-validation code fix inserted unqualified attribute names, so a project-local `ValidateObjectMembersAttribute` or `ValidateEnumeratedItemsAttribute` could make the fix bind to the wrong attribute. | Detect same-scope type-name conflicts and use `global::Microsoft.Extensions.Options.*Attribute` for the inserted recursive attribute when needed, with object and collection regressions. | `CFG005` stays `4.75`, with safer automatic fixes in conflicted codebases. |
| 2026-05-04 | 8 | `CFG006` | Colon-delimited projection represented sibling flattened keys under the same nested object as repeated one-property nodes, which still reported correctly but caused unnecessary repeated recursive walks. | Merge shared projected prefixes into one logical nested configuration node before unknown-key analysis and added sibling flattened-key regression coverage. | `CFG006` stays `4.50`, with a cleaner configuration model and lower repeated-walk risk. |
| 2026-05-04 | 9 | `CFG001` | The section typo code fix recreated replacements as normal string literals, so verbatim and raw section literals lost their original style. | Preserve verbatim and raw string literal tokens when replacing suggested section names, with focused code-fix regressions. | `CFG001` stays `4.85`, with more style-preserving automatic fixes. |
| 2026-05-04 | 10 | `CFG001`, `CFG006` | File discovery accepted any `appsettings*.json`, so lookalike files such as `appsettingsBackup.json` or `appsettingsSchema.json` could suppress missing-section diagnostics or create unknown-key noise. | Restrict analyzer and buildTransitive discovery to `appsettings.json` plus dot-qualified `appsettings.*.json`, while proving `appsettings.Development.local.json` remains visible. | `CFG001` stays `4.85`; `CFG006` stays `4.50`, with a lower false-positive/false-negative file-selection boundary. |
| 2026-05-04 | 11 | `CFG006` | `[ConfigurationKeyName]` was treated as an extra accepted key instead of the runtime binding key override, so JSON using the CLR property name could hide an unbound option value. | Match the configured key name instead of the CLR property name when an alias is present, with root and nested regressions. | `CFG006` stays `4.50`, with stronger runtime binder alignment. |
| 2026-05-04 | 12 | `CFG004` | Only root-level DataAnnotations triggered missing `ValidateDataAnnotations()`, so a recursively annotated nested graph could still miss the DataAnnotations registration when the root type had no direct annotations. | Reuse the nested validation graph walk for `CFG004` and added analyzer/code-fix regressions for nested-only DataAnnotations. | `CFG004` stays `4.60`, with a closed false-negative around recursive validation setup. |
| 2026-05-04 | 13 | `CFG004`, `CFG005`, `CFG006` | Bindable-property detection required a public setter, but the runtime binder can populate initialized get-only object, collection, and dictionary properties. That could create unknown-key false positives and miss validation gaps on common immutable-wrapper options. | Treat initialized get-only nested objects and mutable collections as bindable for validation rules, and initialized get-only dictionaries as bindable for key analysis, with regressions plus uninitialized get-only guards. | `CFG004` stays `4.60`; `CFG005` stays `4.75`; `CFG006` stays `4.50`, with stronger binder alignment. |
| 2026-05-04 | 14 | `CFG004`, `CFG005`, `CFG006` | Registrations that explicitly set `BindNonPublicProperties = true` can populate private-set options properties at runtime, but the analyzer still treated those properties as non-bindable. | Propagate constant-true binder options into options metadata for validation and unknown-key analysis, with private-set positive and default-off guard regressions. | `CFG004` stays `4.60`; `CFG005` stays `4.75`; `CFG006` stays `4.50`, with fewer opt-in binder false positives. |
| 2026-05-04 | 15 | `CFG003`, `CFG004` | Split local `OptionsBuilder<T>` tracking only started when the binding call was inside the local initializer, so `var builder = AddOptions<T>(); builder.BindConfiguration(...); builder.Validate...;` could miss later validation calls. | Start same-block local-chain scanning from local binding expression statements as well as bound initializers, with analyzer and code-fix regressions plus an unrelated-statement break guard. | Both rules stay at `4.60`, with the documented split-local boundary now covering the common bind-after-declaration style. |

## Health Baseline

| Rule | Severity | Importance | Precision | Test Depth | Fix Safety | Docs | Release | Score | Priority | Current read |
|---|---|---:|---:|---:|---:|---:|---:|---:|---|---|
| CFG001 Missing configuration section | Warning | 5 | 5 | 5 | 4 | 5 | 5 | 4.85 | P3 | Strong current shape. Handles `BindConfiguration(...)`, `Bind(GetSection(...))`, direct `Configure<T>(GetSection(...))`, nested section paths, full-path suggestions, duplicate JSON section members, comments, JSON string escapes, colon-delimited appsettings keys, style-preserving section-literal fixes, and visible `appsettings.json` / `appsettings.*.json` files as one searchable set. |
| CFG003 Validation not on startup | Warning | 4 | 5 | 5 | 4 | 5 | 5 | 4.60 | P3 | Good analyzer boundary for fluent and immediate same-block local `OptionsBuilder<T>` chains, including initializer-bound and later local binding statements plus `Bind(GetSection(...))`; tracks symbol-checked framework validation/startup calls before and after the binding call, honors `AddOptionsWithValidateOnStart<TOptions>()`, and code fixes preserve multiline formatting, comments, split locals, and single-line chains. |
| CFG004 DataAnnotations not enabled | Warning | 4 | 5 | 5 | 4 | 5 | 5 | 4.60 | P3 | Covers root, inherited, and nested bindable DataAnnotations plus `IValidatableObject` on supported `OptionsBuilder<T>` bindings, including initialized get-only nested object/collection properties and private-set properties when `BindNonPublicProperties` is enabled; recognizes the framework `ValidateDataAnnotations()` before and after binding in fluent and immediate same-block local chains, avoids duplicate `ValidateOnStart()` when startup validation already exists, and shares the formatter-safe invocation appender with CFG003. |
| CFG005 Nested validation not recursive | Warning | 5 | 4 | 5 | 5 | 5 | 5 | 4.75 | P3 | Strong current shape. Covers recursive object and collection graphs, including initialized get-only object/collection properties, opt-in private-set nested properties, nested `IValidatableObject` types, and user namespaces that merely start with `System`, on supported `OptionsBuilder<T>` bindings; suppresses unsafe interface/uninitialized get-only/default-private-set cases and proves cross-document recursive-attribute fixes, including namespace-local using handling and local attribute-name conflicts. |
| CFG006 Unknown configuration key | Info | 4 | 4 | 5 | 5 | 5 | 5 | 4.50 | P3 | Broadest test depth. Covers `BindConfiguration(...)`, `Bind(GetSection(...))`, and direct `Configure<T>(GetSection(...))`; recurses through nested objects, initialized get-only objects, object collections, initialized get-only collections, dictionary values, dictionary values containing collections, explicit `BindNonPublicProperties`, comments, JSON string escapes, `[ConfigurationKeyName]` key overrides, merged colon-delimited appsettings keys, dot-qualified appsettings file names, and user namespaces that merely start with `System` while keeping scalar arrays and dictionary entry names quiet. |

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
4. Keep `CFG003` and `CFG004` in monitor mode unless real-world chains expose another receiver/local-chain, formatter, or framework-registration edge case.
5. Keep `CFG001` in monitor mode. Future work should be driven by real appsettings/provider-order bugs, not by widening static inference.

## Rule Notes

### CFG001 Missing Configuration Section

Reports when a supported options binding references a string-literal section path that does not exist in visible `appsettings.json` or `appsettings.*.json` files. Nested section paths are matched segment by segment. The code fix replaces the literal with the corrected section path, or the corrected leaf when the code uses chained `GetSection(...)` calls.

Current behavior:

- Checks top-level and nested section paths across all visible `appsettings.json` and dot-qualified `appsettings.*.json` additional files for `BindConfiguration(...)`, `Bind(GetSection(...))`, `Bind(GetRequiredSection(...))`, and direct `Configure<T>(GetSection(...))`.
- Parses `//` and `/* ... */` comments, JSON string escapes, and colon-delimited JSON keys in appsettings files before resolving section paths.
- Traverses duplicate JSON object members when resolving section existence and suggestions.
- Preserves regular, verbatim, and raw string literal style when applying section typo fixes.
- Ignores non-constant, empty, whitespace-only, root configuration, and stored `IConfigurationSection` values.

Known gaps:

- Treats visible appsettings files as one searchable set and does not model configuration-provider ordering.
- Does not infer dynamic section names.

### CFG003 Validation Not On Startup

Reports when an options registration has validation through `ValidateDataAnnotations()` or `Validate(...)` but no `ValidateOnStart()`.

Current behavior:

- Tracks normal fluent chains before and after `BindConfiguration(...)` and `Bind(GetSection(...))`.
- Tracks immediate same-block local `OptionsBuilder<T>` calls, including binding in the initializer or a later local expression statement, until an unrelated statement breaks the sequence.
- Counts only the framework `OptionsBuilder<TOptions>.Validate(...)`, `ValidateDataAnnotations()`, and `ValidateOnStart()` APIs as validation signals; custom same-name helpers are ignored by name alone.
- Treats `AddOptionsWithValidateOnStart<TOptions>()` as startup validation, so registrations using the framework helper do not need an extra `ValidateOnStart()` call.
- Offers a fix that appends `ValidateOnStart()` while preserving multiline chain indentation, comments, split locals, and single-line chains.

Known gaps:

- Does not infer non-local builder storage, aliases, reassignment, or broader control flow.
- Future code-fix work should be driven by concrete formatter regressions rather than speculative chain shapes.
- Documentation explains the rule and the fluent-chain shapes it recognizes.

### CFG004 DataAnnotations Not Enabled

Reports when an options type has bindable DataAnnotations anywhere in its supported options graph but the options registration does not call `ValidateDataAnnotations()`.

Current behavior:

- Includes root, inherited, and nested public bindable properties, including initialized get-only object/collection properties, plus options types implementing `IValidatableObject`.
- Honors conservative bindable-property boundaries and only includes get-only nested properties when they have an initializer the binder can populate.
- Includes public private-set properties only for registrations that explicitly set `BindNonPublicProperties = true`.
- Treats framework `Validate(...)` predicate registrations as validation for `CFG003`, but not as a substitute for `ValidateDataAnnotations()`.
- Tracks the framework `ValidateDataAnnotations()` before and after the binding call in the same fluent chain or immediate same-block local chain.
- Offers a fix that appends `ValidateDataAnnotations()` and appends `ValidateOnStart()` only when startup validation is missing, using the formatter-safe chain appender shared with `CFG003`.
- Does not append `ValidateOnStart()` when the registration started with `AddOptionsWithValidateOnStart<TOptions>()`.

Known gaps:

- Future code-fix work should be driven by concrete formatter regressions rather than speculative chain shapes.
- Does not infer annotations on non-bindable members, which is intentional but worth keeping explicit in docs.

### CFG005 Nested Validation Not Recursive

Reports when a nested object or collection item type contains validation attributes but the parent property is missing the required recursive validation attribute.

Current behavior:

- Finds nested object graphs and nested collection graphs that contain DataAnnotations or implement `IValidatableObject`.
- Treats initialized get-only object and collection properties as bindable, matching the runtime binder's ability to populate existing instances.
- Includes public private-set nested properties only when `BindNonPublicProperties` is explicitly enabled on the binding call.
- Covers arrays, `IEnumerable<T>` shapes, nullable nested properties, and deep nested properties.
- Treats user namespaces such as `Systematic.Options` as analyzable user code while still excluding BCL `System` / `System.*` types.
- Suppresses interface-typed nested properties and already annotated recursive-validation properties.
- Offers fixes for `[ValidateObjectMembers]` and `[ValidateEnumeratedItems]`, including options properties declared in a different source document from the registration diagnostic, files that use namespace-local using blocks, and files with same-named local attributes that require fully qualified attribute insertion.

Known gaps:

- More complex custom recursive validation patterns are intentionally not inferred.

### CFG006 Unknown Configuration Key

Reports an appsettings key under a bound section when the key does not match a bindable options property or its `[ConfigurationKeyName]` override. This rule stays informational because configuration binding is flexible and false-positive cost is higher.

Current behavior:

- Checks every matching bound section across visible `appsettings.json` and dot-qualified `appsettings.*.json` files for supported `BindConfiguration(...)`, `Bind(GetSection(...))`, and direct `Configure<T>(GetSection(...))` registrations.
- Treats initialized get-only object, collection, and dictionary properties as bindable, while leaving uninitialized get-only properties outside the static bindable graph.
- Honors constant-true `BindNonPublicProperties` binder options for public private-set options properties and stays on the default public-setter boundary otherwise.
- Parses `//` and `/* ... */` comments, JSON string escapes, and colon-delimited JSON keys before walking keys so commented local appsettings files stay analyzable.
- Merges sibling colon-delimited keys under the same nested object into one projected configuration node before recursive unknown-key analysis.
- Recurses into nested object properties, arrays/lists of nested objects, strongly typed dictionary values, and dictionary values containing nested object collections.
- Honors `[ConfigurationKeyName]` overrides at the root and nested levels, matching the configured key instead of the CLR property name when an override is present.
- Treats user namespaces such as `Systematic.Options` as analyzable user code while still avoiding BCL `System` / `System.*` nested types.
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
