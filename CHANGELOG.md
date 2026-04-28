# Changelog

All notable changes to ConfigContraband will be documented in this file.

## 0.1.1 - 2026-04-28

- Hardened `CFG001` to traverse duplicate JSON section members when resolving nested section paths and typo suggestions.
- Added regression coverage for duplicate-section lookup and nested suggestion behavior.

## 0.1.0 - 2026-04-28

- Initial MVP analyzer package.
- Added `CFG001`, `CFG003`, `CFG004`, `CFG005`, and `CFG006`.
- Added code fixes for section typos, missing `ValidateOnStart()`, missing `ValidateDataAnnotations()`, and recursive validation attributes.
- Added `buildTransitive` props to include `appsettings*.json` as analyzer inputs.
- Hardened `CFG006` to check strongly typed dictionary values while keeping dynamic dictionary entry names quiet.
