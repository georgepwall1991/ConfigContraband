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

## Rule details

`CFG003` reports options registrations that configure validation with `ValidateDataAnnotations()` or `Validate(...)` but do not call `ValidateOnStart()`. The analyzer follows normal fluent chains and immediate same-block local `OptionsBuilder<T>` split chains, so a registration can be written as one chain or as:

```csharp
var optionsBuilder = services.AddOptions<StripeOptions>()
    .BindConfiguration("Stripe");
optionsBuilder.ValidateDataAnnotations();
optionsBuilder.ValidateOnStart();
```

`CFG004` reports options types that use DataAnnotations attributes but whose registration chain does not call `ValidateDataAnnotations()`. Custom `Validate(...)` delegates still count as validation for `CFG003`, but they do not satisfy `CFG004` when DataAnnotations attributes are present.

`CFG005` reports nested object or collection properties whose nested types contain validation attributes but are missing recursive validation attributes such as `[ValidateObjectMembers]` or `[ValidateEnumeratedItems]`.

`CFG006` is informational because configuration binding allows flexible shapes. It flags unknown keys under a bound appsettings section when the key does not match a bindable option property or `[ConfigurationKeyName]` alias.

## Example

```csharp
builder.Services.AddOptions<StripeOptions>()
    .BindConfiguration("Strpie")
    .ValidateDataAnnotations();
```

Given an `appsettings.json` section named `Stripe`, ConfigContraband reports `CFG001` and offers a fix to use `"Stripe"`. If validation is present but no `ValidateOnStart()` call exists, it reports `CFG003` and offers a fix to append it.
