# ConfigContraband Showcase

This standalone project is intentionally broken. Build it to see one clear diagnostic for each analyzer rule.

```bash
dotnet build samples/ConfigContraband.Showcase/ConfigContraband.Showcase.csproj --configuration Release --no-incremental
```

Expected diagnostics:

| Rule | What the sample shows |
|---|---|
| `CFG001` | `BindConfiguration("Strpie")` does not match the `Stripe` section in `appsettings.json`. |
| `CFG003` | A custom `Validate(...)` rule exists, but `ValidateOnStart()` is missing. |
| `CFG004` | An options type uses `[Required]`, but `ValidateDataAnnotations()` is missing. |
| `CFG005` | A nested options object has validation attributes, but the parent property is not marked for recursive validation. |
| `CFG006` | `appsettings.json` contains `WebookSecret`, which does not match `WebhookSecret`. |

The sample is not included in the main solution, so normal package and test builds stay clean. Its local `.globalconfig` promotes `CFG006` from `Info` to `warning` so it is visible with the other showcase diagnostics.
