# ConfigContraband (aka ConfiContraband / LinqContraband)

Roslyn analyzer for .NET configuration/Options validation. Rule IDs are `CFG0xx` (CFG001–CFG007) —
not `DI0xx`; that prefix belongs to a different project/skill template.

- Source of truth for current rule health, precision/recall scores, and known gaps:
  `analyzer-health.md`. Check it before assuming a limitation is a bug rather than a documented,
  deliberate scope boundary.
- Checklists for adding a new rule vs. hardening an existing one: `CONTRIBUTING.md`.
- Rule behavior/scope is documented per-rule in `README.md` under "Rule Details" and "Current Scope".
