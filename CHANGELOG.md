# Changelog

All notable changes to ConfigContraband will be documented in this file.

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
