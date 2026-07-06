# Changelog

All notable changes to ConfigContraband will be documented in this file.

## 0.5.11 - 2026-07-06

- Fixed a `CFG007` **false positive** found by evidence-based audit: a reset delegate/lambda that sets `ErrorOnUnknownConfiguration = false` and is passed as a call *argument* to a helper that invokes it — `RunNow(disableStrict)` or the inline `RunNow(() => options.ErrorOnUnknownConfiguration = false)` — escaped the strict-binding escape analysis, so strict mode was treated as still enabled and CFG007 reported a false Warning even though the helper turns strict mode off before binding and the runtime binder would not throw. The escape analysis now also treats a lambda/anonymous-method or local-delegate argument whose body references the runtime binder options as an escape (the same way a directly-invoked reset delegate already is), so the analyzer stays conservative and reports the softer `CFG006` informational diagnostic instead. Diagnostic IDs, severities, and unrelated inference boundaries are unchanged.

## 0.5.10 - 2026-07-06

- Fixed a `CFG001` **unsafe code fix** found by evidence-based audit: when the reported section literal was a chained non-root `GetSection(...)` argument that itself contained a colon (for example `.GetSection("Features").GetSection("Sub:Strpie")`), the "Use \"…\"" fix substituted only the corrected leaf for the whole multi-segment literal, silently dropping the `Sub:` segment and producing a still-broken binding (`.GetSection("Stripe")`, path `Features:Stripe`) that contradicted the fix's own "Did you mean `Features:Sub:Stripe`?" label. The fix now rewrites only the anchored literal's leaf and preserves any leading colon segments (`.GetSection("Sub:Stripe")`), and is suppressed — the diagnostic and suggestion message still appear, but no auto-fix is offered — when the anchored section expression is not a plain string literal whose segments can be reproduced safely. Diagnostic IDs, severities, and unrelated inference boundaries are unchanged.

## 0.5.9 - 2026-07-06

- Fixed a shared `CFG003`/`CFG004` precision gap found by evidence-based audit: the forward same-block split-statement local-chain scanner stopped at the first unrelated statement, so a genuine `ValidateOnStart()` (`CFG003`) or `ValidateDataAnnotations()` (`CFG004`) called on a local `OptionsBuilder<T>` *after* an intervening unrelated statement (for example `var b = services.AddOptions<T>().BindConfiguration("Section"); services.AddSingleton<X>(); b.ValidateOnStart();`) was never collected, producing a false positive even though the validation is genuinely registered and effective at runtime. The scan now skips *inert* intervening statements (an unrelated call/assignment or a local declaration) while still stopping at a reassignment of the tracked builder local — so a later call on a different builder bound to the same variable is not mis-attributed — and at control flow (`if`/`return`/`throw`/loops) that could keep the later validation call from running on every path. The rarer backward mirror (a validation call placed *before* the bind, separated by an unrelated statement) remains a documented, deliberately-deferred gap. Diagnostic IDs, severities, and unrelated inference boundaries are unchanged.

## 0.5.8 - 2026-07-06

- Fixed a `CFG002` precision gap found by evidence-based audit: a primary-constructor (C# 12) parameter default flowing into a property initializer (e.g. `public sealed class Options(string apiKey = "sk_default") { [Required] public string ApiKey { get; set; } = apiKey; }`) was not recognized as a satisfying default, producing a false positive even though the runtime default provably satisfies `RequiredAttribute`. Diagnostic IDs, severities, and unrelated inference boundaries are unchanged.

## 0.5.7 - 2026-07-06

- Fixed a `CFG007` precision gap found by evidence-based audit: a tuple-deconstruction assignment to `ErrorOnUnknownConfiguration` (for example `(options.ErrorOnUnknownConfiguration, options.BindNonPublicProperties) = (true, false);`) was invisible to the strict-binding escape-analysis proof, so strict binding that was genuinely enabled at runtime silently fell back to the softer `CFG006` Info diagnostic instead of the `CFG007` Warning. Diagnostic IDs, severities, and unrelated inference boundaries are unchanged.

## 0.5.6 - 2026-07-06

- Fixed a `CFG001` precision gap found by evidence-based audit: conditional access (`?.`) anywhere in a `GetSection(...)`/`GetRequiredSection(...)` chain — a non-chained call such as `configuration?.GetSection("Strpie")`, `?.` immediately after the root, or `?.` before a further chained call off a genuine root configuration chain — was not unwrapped by the section-path resolver, so the analyzer silently skipped the section-existence check for that registration entirely instead of checking it. Diagnostic IDs, severities, and unrelated inference boundaries are unchanged.

## 0.5.5 - 2026-07-01

- Fixed a shared `CFG004`/`CFG005` precision gap: a type-level (class) `ValidationAttribute` declared only on a base options class (or, for `CFG005`, on a nested type's base class) is now detected, because both rules' shared `ContainsValidationAttributes` helper now reuses the same inheritance-aware `HasTypeLevelValidationInChain` helper `CFG002`'s required-key proof already relies on for this shape. Previously only attributes declared directly on the exact registered/nested type were seen, missing a real validation constraint the runtime validator evaluates by default (`AttributeUsageAttribute.Inherited` defaults to `true`). Diagnostic IDs, severities, and unrelated inference boundaries are unchanged.

## 0.5.4 - 2026-07-01

- Added regression coverage proving `CFG002` already stays quiet for `[Required]` non-array collection/dictionary properties default-initialized via explicit or target-typed object-creation (`= new List<T>()`, `= new()`). This was shipped, correct behavior that had no test pinning it. No analyzer, code-fix, or diagnostic behavior changed.

## 0.5.3 - 2026-07-01

- Fixed a `CFG001` precision gap found by evidence-based audit: chaining a further `GetSection(...)` call off a stored/received `IConfigurationSection` (for example `var section = configuration.GetSection("Features"); ...Bind(section.GetSection("Stripe"))`), including through a nullable-annotated `IConfigurationSection?` parameter or return value, is now ignored instead of being checked against the root configuration namespace. Previously the analyzer treated the chained literal as a brand-new top-level path, which could both false-positive (warn on a key that exists under the real nested path) and false-negative (miss a typo checked against the wrong namespace). Diagnostic IDs, severities, and unrelated inference boundaries are unchanged.

## 0.5.2 - 2026-06-30

- Added focused regression coverage and documentation for the already-supported `GetRequiredSection(...)` binding shape: `CFG006` unknown-key Info diagnostics now explicitly cover `OptionsBuilder<T>.Bind(configuration.GetRequiredSection("Section"))` and direct `Configure<T>(configuration.GetRequiredSection("Section"))`, and schema registration extraction now explicitly covers `OptionsBuilder<T>.Bind(GetRequiredSection(...))`. Diagnostic IDs, severities, and static inference boundaries are unchanged.

## 0.5.1 - 2026-06-10

- Hardened `CFG002` so `[Required]` properties whose compile-time default already satisfies
  `RequiredAttribute` — and whose only validation constraint is that `[Required]` (other validators
  on the property, type-level validation, or `IValidatableObject` still validate the default and
  keep the key required) — no longer warn when the key is missing: compile-time constants resolved by
  value (non-empty strings, signed numerics such as `= -1`, `const` fields, `nameof`),
  object-creation initializers (including target-typed `new()` on nullable value types, which
  constructs the underlying value), and constructor-bound parameter defaults that provably reach the
  property cannot fail validation, so the missing key is not a startup failure. Initializer-based
  suppression also requires that no declared constructor in the type chain could overwrite the
  property, because constructors run after property initializers. Empty or whitespace-only string defaults still warn (honoring
  `AllowEmptyStrings = true`), `null!`/`default` initializers still warn, parameterless `Nullable<T>`
  construction still warns regardless of the declared property type or syntax (including type
  aliases) because it produces an empty `Nullable<T>`, constructed strings still warn because the
  result can be empty or whitespace, constructor defaults still warn when the parameter is never
  assigned to the property, is reassigned before reaching it, or the constructor body does anything
  beyond simple assignments to fields or auto-implemented properties (a custom setter could mutate
  the required value), recursive-validation parents still warn when their default instance fails on
  nested required members, and non-literal initializers such as method calls stay on the conservative
  reporting path. The generated `appsettings.schema.json` `required` array
  follows the same boundary, so editors no longer demand keys the runtime binder and validator
  accept as absent.

## 0.5.0 - 2026-06-02

- Generated `appsettings.schema.json` now carries **DataAnnotations validation constraints**, so editors
  enforce them while you type instead of waiting for `ValidateDataAnnotations()` to fail at startup:
  `[Range]` → `minimum`/`maximum` (honoring `MinimumIsExclusive`/`MaximumIsExclusive`) and
  `[MaxLength]`/`[StringLength]` → `maxLength`. Every constraint mirrors what `Microsoft.Extensions.Options`
  actually enforces and is only emitted for bindings that validate (matching CFG002/CFG004), so loose
  configuration is never over-constrained and the schema never rejects a value the runtime binder would
  accept. Numeric bounds use the invariant culture, combined `maxLength` validators keep the strictest bound,
  and non-finite/negative-sentinel bounds are skipped. `[RegularExpression]`, `[EmailAddress]`/`[Url]`, and
  `[MinLength]` are intentionally not mapped, because .NET regex / strict `format` grammars / UTF-16 length
  counting differ from JSON Schema's ECMA-262 and code-point semantics enough that a translation could reject
  runtime-valid configuration.
- The generated schema now emits a **`description`** for each option from its `///` XML doc `<summary>` —
  or a `[Description]`/`[DisplayName]` attribute, which take priority — on both properties and option types,
  giving hover documentation for every setting. The schema CLI forces documentation parsing, so descriptions
  appear even when the project does not set `<GenerateDocumentationFile>`.
- These additions are purely additive annotations on existing scalar/object nodes; structure, required
  keys, `additionalProperties`, and the `CFG001`-`CFG007` analyzer diagnostics are unchanged.

## 0.4.0 - 2026-06-01

- Added `appsettings.json` schema generation: the new `ConfigContraband.Tool` dotnet tool
  (`configcontraband schema`) emits an `appsettings.schema.json` from your options types, so editors
  give live autocomplete, type checking, required-key hints, enum completion, and unknown-key warnings
  while editing configuration. `--check` fails CI when the committed schema is stale.
- The generated schema mirrors the analyzer's runtime semantics: required keys from `[Required]`,
  `[ConfigurationKeyName]` aliases, nested objects, collections, and dictionaries, and
  `additionalProperties: false` only for strict (`ErrorOnUnknownConfiguration`) bindings so loose
  configuration stays valid.
- Extracted the analyzer's bindable-property and configuration model into a shared
  `ConfigContraband.Core` library (bundled inside the analyzer package). This is a behavior-preserving
  refactor; `CFG001`-`CFG007` diagnostics are unchanged.

## 0.3.1 - 2026-05-19

- Hardened `CFG002` to match runtime DataAnnotations validation behaviour: C# `required` members, non-nullable value types, direct `Configure<T>(GetSection(...))` bindings without validation, and dictionary value object graphs no longer produce required-key false positives, while separately validated direct bindings still report missing required keys.
- Updated package release notes and README guidance so the required-key rule documents its DataAnnotations and recursive-validation boundaries.

## 0.3.0 - 2026-05-19

- Added `CFG002`, a warning for required configuration keys missing from `appsettings.json` files.
- Implemented detection for `[Required]` (DataAnnotations) and the C# 11 `required` keyword on options properties.
- Added support for merging configuration sections across all visible `appsettings*.json` files before reporting missing keys, ensuring environment-specific overrides do not cause false positives.
- Implemented recursive required key analysis for nested options objects and dictionaries.
- Suppressed `CFG002` when the parent section is missing (`CFG001`) to reduce diagnostic noise.

## 0.2.0 - 2026-05-19

- Added `CFG007`, a warning for unknown appsettings keys when the binding call explicitly enables `BinderOptions.ErrorOnUnknownConfiguration`.
- Reused the existing `CFG006` bindable-property model for strict binding so `BindConfiguration(...)`, `Bind(GetSection(...))`, direct `Configure<T>(GetSection(...))`, nested object keys, `[ConfigurationKeyName]` aliases rejected by strict binding, object-shaped scalar values, null/default-initialized settable nested objects, constructor-initialized get-only object values, rejected object-shaped scalar collection/dictionary entries, and nested dictionary scalar, object, or object-collection values report only when the key would fail binding, while scalar CLR property names, null CLR-only nullable values, interface/object declared or value shapes, property- or constructor-initialized polymorphic reference shapes, matching initializer- or constructor-prepopulated polymorphic dictionary entries including ignore-case dictionary comparers, escaped binder-options helpers or non-local aliases, and nested dictionary entries accepted by strict binding stay quiet or informational.
- Tightened review blockers so constructor/object-creation `BinderOptions` escapes, field/property alias storage, and invoked local binder-options helpers including delegate `Invoke()` forms invalidate strict proof, constructor-initialized get-only nested objects stay visible to validation diagnostics, constructor-assigned dictionary comparers are preserved, empty collection-expression dictionary initializers are not treated as prepopulated, nested prepopulated polymorphic dictionary entries honor case-insensitive comparers, including `IEqualityComparer<string>` aliases, per dictionary level instead of applying ignore-case matching to the whole path, creatable reference collection/dictionary item CLR members accepted by strict binding do not warn, and strict-only registrations fall back to `CFG006` when `CFG007` is disabled through compilation diagnostic suppression.

## 0.1.11 - 2026-05-04

- Hardened `CFG003` and `CFG004` chain analysis so only the real framework `OptionsBuilder<TOptions>` validation APIs count as validation or startup validation.
- Added regression coverage proving named `AddOptions<TOptions>("name")` builder chains keep section, validation, and unknown-key diagnostics.
- Added code-fix regression coverage proving named `AddOptions<TOptions>("name")` builder chains receive the same `ValidateOnStart()` and `ValidateDataAnnotations()` fixes.
- Hardened constructor-bound bindable-property detection so derived options constructors can map to inherited public properties, matching runtime binder behaviour for validation and unknown-key analysis.
- Hardened constructor-bound bindable-property detection to ignore ambiguous types with multiple public parameterized constructors, matching the runtime binder.
- Hardened `CFG004` and `CFG005` so type-level `ValidationAttribute`s on root or nested options types are treated as DataAnnotations validation.
- Added `CFG005` guard coverage proving dictionary value objects stay quiet because recursive Options validation attributes do not validate dictionary values directly.
- Hardened `CFG006` so `[ConfigurationKeyName]` aliases on settable constructor-bound properties are accepted only when the constructor parameter key is present in the same section.
- Hardened `CFG006` so `[ConfigurationKeyName]` aliases on settable constructor-bound properties are also accepted when the matching constructor parameter has a default value.
- Added `CFG006` regression coverage for private-set constructor-bound aliases with and without `BindNonPublicProperties`.
- Hardened `BindNonPublicProperties` detection so only assignments on the actual binder-options lambda parameter make private-set properties analyzable.
- Added regression coverage for custom same-name extension methods that previously could hide missing `ValidateOnStart()` / `ValidateDataAnnotations()` diagnostics or create a validation false positive.
- Hardened split local `OptionsBuilder<TOptions>` chain analysis so validation calls after a separate local `BindConfiguration(...)` / `Bind(...)` statement are recognized.
- Hardened split local `OptionsBuilder<TOptions>` chain analysis so adjacent validation calls before a later local bind statement are recognized without scanning past unrelated statements.
- Hardened split local `OptionsBuilder<TOptions>` chain analysis so adjacent builder initializer calls such as `AddOptionsWithValidateOnStart<TOptions>()` and `.ValidateDataAnnotations()` are recognized before a later local bind statement.
- Added code-fix regression coverage for the expanded split local validation-chain shapes, including pre-bind validation and initializer startup validation.
- Synced NuGet package release notes with the `0.1.11` analyzer hardening surface.
- Added formatting verification to PR CI so whitespace and formatter drift fail before merge.
- Added formatting verification to the NuGet publish workflow so release packaging uses the same formatter gate as PR CI.
- Synced analyzer release tracking so diagnostics shipped since `0.1.0` are recorded in `AnalyzerReleases.Shipped.md` instead of remaining marked as unshipped.
- Added regression coverage proving analyzer diagnostics stay quiet in generated source files.
- Added regression coverage proving analyzer diagnostics stay quiet in generated `.g.cs` source files.
- Hardened `BindNonPublicProperties` handling so explicit binder options make private-set options properties visible to validation and unknown-key analysis.
- Hardened bindable-property detection so constructor-bound options records and immutable classes align with the runtime configuration binder, including nested validation and unknown-key analysis.
- Hardened the `CFG005` recursive-validation code fix so constructor-bound record properties receive property-targeted recursive validation attributes.
- Added `CFG005` code-fix regression coverage proving constructor-bound record collections receive property-targeted `[ValidateEnumeratedItems]`.
- Added `CFG005` code-fix regression coverage proving constructor-bound record collection fixes fully qualify `[ValidateEnumeratedItems]` when a local attribute name conflicts.
- Hardened bindable-property detection so initialized get-only object, collection, and dictionary properties align with the runtime configuration binder.
- Hardened `CFG004` so nested object graphs that contain DataAnnotations still require `ValidateDataAnnotations()` even when the root options type has no direct annotations.
- Hardened the `CFG005` recursive-validation code fix to reuse namespace-local `Microsoft.Extensions.Options` imports and add new imports to the namespace-local using block when that is the file style.
- Hardened the `CFG005` recursive-validation code fix to fully qualify inserted attributes when a project-local attribute name would otherwise shadow `ValidateObjectMembersAttribute` or `ValidateEnumeratedItemsAttribute`.
- Hardened `CFG006` colon-delimited appsettings projection so sibling flattened keys under the same nested object are merged into one logical configuration node before unknown-key analysis.
- Hardened `CFG006` to treat `[ConfigurationKeyName]` as the runtime configuration key override instead of also accepting the CLR property name.
- Hardened the `CFG001` section-suggestion code fix to preserve verbatim and raw string literal style when replacing misspelled section names.
- Hardened the `CFG001` raw string section-suggestion code fix to fall back to an escaped string literal when the suggested section contains decoded line breaks.
- Tightened appsettings file discovery to `appsettings.json` and dot-qualified `appsettings.*.json` files, avoiding lookalike files such as `appsettingsBackup.json` or `appsettingsSchema.json`.

## 0.1.10 - 2026-05-03

- Hardened `CFG001` and `CFG006` to recognize colon-delimited appsettings keys such as `"Features:Stripe"` and `"Features:Stripe:ApiKey"` as normal configuration hierarchy.
- Added regression coverage for colon-delimited section existence, typo suggestions, and unknown-key diagnostics in flattened appsettings shapes.

## 0.1.9 - 2026-05-03

- Hardened `CFG003` and `CFG004` fluent-chain analysis so validation calls are recognized whether they appear before or after `BindConfiguration(...)` / `Bind(...)`.
- Added regression coverage for pre-bind `ValidateDataAnnotations()` and `ValidateOnStart()` chains.
- Hardened appsettings parsing to decode JSON `\uXXXX` string escapes for section and key matching.
- Added regression coverage so `CFG001` and `CFG006` honor escaped section names and property names.
- Tightened the nested-options namespace filter so user namespaces like `Systematic.Options` are still analyzed.
- Added regression coverage for `CFG005` and `CFG006` nested user types whose namespaces start with `System` but are not part of the BCL.

## 0.1.8 - 2026-04-29

- Hardened `CFG004` to treat `IValidatableObject` implementations as DataAnnotations validation that needs `ValidateDataAnnotations()`.
- Hardened `CFG005` so nested option objects that implement `IValidatableObject` still require recursive validation.

## 0.1.7 - 2026-04-29

- Hardened `CFG003` and `CFG004` to honor `AddOptionsWithValidateOnStart<TOptions>()` as startup validation.
- Added regression coverage so the analyzer stays quiet for startup-validated registrations and the `CFG004` fix does not append a redundant `ValidateOnStart()`.

## 0.1.6 - 2026-04-29

- Hardened appsettings parsing to handle `//` and `/* ... */` comments when resolving sections and unknown keys.
- Added regression coverage for commented configuration files so `CFG001` and `CFG006` keep matching the configuration shape developers build with.

## 0.1.5 - 2026-04-29

- Added analyzer coverage for `AddOptions<T>().Bind(configuration.GetSection(...))` and `GetRequiredSection(...)` registrations.
- Added section and unknown-key coverage for direct `Configure<T>(configuration.GetSection(...))` registrations while keeping validation diagnostics scoped to `OptionsBuilder<T>` chains.

## 0.1.4 - 2026-04-29

- Hardened `CFG005` recursive-validation code fixes to update the source document that owns the target options property.
- Added regression coverage for cross-document `[ValidateObjectMembers]` and `[ValidateEnumeratedItems]` fixes, including existing `Microsoft.Extensions.Options` imports and property comments.

## 0.1.3 - 2026-04-29

- Hardened the shared `CFG003` and `CFG004` code-fix appender to preserve multiline fluent-chain formatting.
- Added regression coverage for split local chains, custom validation chains, comments inside chains, and single-line chains.

## 0.1.2 - 2026-04-29

- Hardened `CFG006` to recurse through dictionary values that bind to collections of nested option objects.
- Added regression coverage for unknown and valid keys under `Dictionary<string, List<TOptions>>` configuration shapes.

## 0.1.1 - 2026-04-28

- Hardened `CFG001` to traverse duplicate JSON section members when resolving nested section paths and typo suggestions.
- Added regression coverage for duplicate-section lookup and nested suggestion behavior.

## 0.1.0 - 2026-04-28

- Initial MVP analyzer package.
- Added `CFG001`, `CFG003`, `CFG004`, `CFG005`, and `CFG006`.
- Added code fixes for section typos, missing `ValidateOnStart()`, missing `ValidateDataAnnotations()`, and recursive validation attributes.
- Added `buildTransitive` props to include `appsettings*.json` as analyzer inputs.
- Hardened `CFG006` to check strongly typed dictionary values while keeping dynamic dictionary entry names quiet.
