# ConfigContraband

Stop smuggling broken appsettings into production.

ConfigContraband is a Roslyn analyzer for .NET configuration and Options pattern usage. It catches mistyped configuration sections, missing startup validation, missing DataAnnotations registration, non-recursive nested validation, and unknown appsettings keys before runtime.

## Quickstart

```xml
<PackageReference Include="ConfigContraband" Version="0.1.0" PrivateAssets="all" />
```

The package automatically includes `appsettings*.json` files as analyzer inputs through `buildTransitive` props.

## MVP Rules

| ID | Title | Default |
|----|-------|---------|
| `CFG001` | Bound configuration section does not exist | Warning |
| `CFG003` | Validation exists but does not run on startup | Warning |
| `CFG004` | DataAnnotations exist but are never enabled | Warning |
| `CFG005` | Nested options are annotated but not recursively validated | Warning |
| `CFG006` | Unknown config key under a bound section | Info |

## Example

```csharp
builder.Services.AddOptions<StripeOptions>()
    .BindConfiguration("Strpie")
    .ValidateDataAnnotations();
```

Given an `appsettings.json` section named `Stripe`, ConfigContraband reports `CFG001` and offers a fix to use `"Stripe"`. If validation is present but no `ValidateOnStart()` call exists, it reports `CFG003` and offers a fix to append it.

