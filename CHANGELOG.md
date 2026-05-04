# Changelog

All notable changes to ConfigContraband will be documented in this file.

## 0.1.11 - 2026-05-04

- Hardened `CFG003` and `CFG004` chain analysis so only the real framework `OptionsBuilder<TOptions>` validation APIs count as validation or startup validation.
- Added regression coverage for custom same-name extension methods that previously could hide missing `ValidateOnStart()` / `ValidateDataAnnotations()` diagnostics or create a validation false positive.
- Hardened `BindNonPublicProperties` handling so explicit binder options make private-set options properties visible to validation and unknown-key analysis.
- Hardened bindable-property detection so initialized get-only object, collection, and dictionary properties align with the runtime configuration binder.
- Hardened `CFG004` so nested object graphs that contain DataAnnotations still require `ValidateDataAnnotations()` even when the root options type has no direct annotations.
- Hardened the `CFG005` recursive-validation code fix to reuse namespace-local `Microsoft.Extensions.Options` imports and add new imports to the namespace-local using block when that is the file style.
- Hardened the `CFG005` recursive-validation code fix to fully qualify inserted attributes when a project-local attribute name would otherwise shadow `ValidateObjectMembersAttribute` or `ValidateEnumeratedItemsAttribute`.
- Hardened `CFG006` colon-delimited appsettings projection so sibling flattened keys under the same nested object are merged into one logical configuration node before unknown-key analysis.
- Hardened `CFG006` to treat `[ConfigurationKeyName]` as the runtime configuration key override instead of also accepting the CLR property name.
- Hardened the `CFG001` section-suggestion code fix to preserve verbatim and raw string literal style when replacing misspelled section names.
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
