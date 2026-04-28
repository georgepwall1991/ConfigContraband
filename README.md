# ConfigContraband

[![CI](https://github.com/georgepwall1991/ConfigContraband/actions/workflows/ci.yml/badge.svg)](https://github.com/georgepwall1991/ConfigContraband/actions/workflows/ci.yml)
[![CodeQL](https://github.com/georgepwall1991/ConfigContraband/actions/workflows/codeql.yml/badge.svg)](https://github.com/georgepwall1991/ConfigContraband/actions/workflows/codeql.yml)
[![codecov](https://codecov.io/gh/georgepwall1991/ConfigContraband/branch/main/graph/badge.svg)](https://codecov.io/gh/georgepwall1991/ConfigContraband)

Stop smuggling broken `appsettings` into production.

ConfigContraband is a high-signal Roslyn analyzer for .NET configuration, ASP.NET Core Options, `appsettings.json`, `ValidateOnStart()`, and `ValidateDataAnnotations()`. It catches the configuration mistakes that compile cleanly, pass code review, and then fail at startup or, worse, on first use.

It focuses on the boring production failures:

- a section name typo in `BindConfiguration(...)`
- validation that exists but does not run on startup
- `[Required]` properties that are never wired into Options validation
- nested options that look validated but are silently skipped
- misspelled JSON keys hiding under a bound section

Use it when your app relies on strongly typed options and you want configuration validation feedback in the editor, in pull requests, and in CI before a bad setting reaches production.

## Feature Snapshot

| Area | What ConfigContraband does |
|------|----------------------------|
| Section binding | Checks `BindConfiguration("Section")` against visible `appsettings*.json` files. |
| Startup validation | Flags options validation that is registered but not forced to run at startup. |
| DataAnnotations | Finds `[Required]`, `[Range]`, and inherited validation attributes without `ValidateDataAnnotations()`. |
| Nested validation | Detects nested options objects and collections that need recursive validation attributes. |
| JSON key drift | Reports likely misspelled keys under bound sections while staying conservative for flexible binding shapes. |

## Install

```xml
<PackageReference Include="ConfigContraband" Version="0.1.0" PrivateAssets="all" />
```

The package includes `buildTransitive` props that pass visible `appsettings*.json` files to the analyzer automatically. Add the package, build, and let your editor or CI tell you when your options contract and configuration drift apart.

No runtime dependency is added to your app. ConfigContraband runs as an analyzer during build and in supported IDEs.

## What It Looks At

ConfigContraband analyzes options registrations shaped like this:

```csharp
services.AddOptions<StripeOptions>()
    .BindConfiguration("Stripe");
```

The section name must be a compile-time string literal. The analyzer follows normal fluent chains:

```csharp
services.AddOptions<StripeOptions>()
    .BindConfiguration("Stripe")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

It also follows immediate same-block local `OptionsBuilder<T>` chains:

```csharp
var optionsBuilder = services.AddOptions<StripeOptions>()
    .BindConfiguration("Stripe");

optionsBuilder.ValidateDataAnnotations();
optionsBuilder.ValidateOnStart();
```

When the analyzer cannot prove a configuration shape statically, it stays quiet. The goal is high-signal feedback, not noisy guesses.

## Rules

| ID | Rule | Default | Catches |
|----|------|---------|---------|
| `CFG001` | Bound configuration section does not exist | Warning | `BindConfiguration("Strpie")` when only `Stripe` exists. |
| `CFG003` | Options validation does not run on startup | Warning | Validation is registered but `ValidateOnStart()` is missing. |
| `CFG004` | DataAnnotations are not enabled for options validation | Warning | `[Required]`, `[Range]`, and inherited annotations without `ValidateDataAnnotations()`. |
| `CFG005` | Nested options validation is not recursive | Warning | Nested objects or item types with annotations but no recursive validation attribute. |
| `CFG006` | Unknown configuration key under bound section | Info | JSON keys that do not match bindable options properties or aliases. |

## Fast Feedback Loop

The repository includes a showcase project with one intentional example for each rule:

```bash
dotnet build samples/ConfigContraband.Showcase/ConfigContraband.Showcase.csproj --configuration Release --no-incremental
```

The sample stays out of the main solution so normal development builds remain clean.

## Rule Details

### `CFG001`: The Section Must Exist

If your code binds `"Stripe"`, a visible `appsettings*.json` file should contain a matching `Stripe` section.

Before:

```csharp
services.AddOptions<StripeOptions>()
    .BindConfiguration("Strpie")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

```json
{
  "Stripe": {
    "ApiKey": "secret"
  }
}
```

After:

```csharp
services.AddOptions<StripeOptions>()
    .BindConfiguration("Stripe")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

When ConfigContraband sees a likely typo, it can offer a code fix. Nested section paths use the same colon-separated shape as .NET configuration:

```csharp
services.AddOptions<StripeOptions>()
    .BindConfiguration("Features:Stripe")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

```json
{
  "Features": {
    "Stripe": {
      "ApiKey": "secret"
    }
  }
}
```

For nested typos, the fix keeps the parent path and replaces only the bad leaf section. If the code says `Features:Strpie` and the file contains `Features:Stripe`, the fix changes it to `Features:Stripe`.

The analyzer checks every visible `appsettings*.json` additional file for section existence. It stays quiet when no appsettings files are available because it cannot prove what configuration exists at runtime.

### `CFG003`: Validation Should Run When The App Starts

Options validation often runs later, when options are first used. `ValidateOnStart()` moves that failure to startup, where it belongs.

Before:

```csharp
services.AddOptions<StripeOptions>()
    .BindConfiguration("Stripe")
    .ValidateDataAnnotations();
```

After:

```csharp
services.AddOptions<StripeOptions>()
    .BindConfiguration("Stripe")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

### `CFG004`: DataAnnotations Must Be Switched On

Attributes such as `[Required]` do nothing for Options validation unless `ValidateDataAnnotations()` is registered. Inherited bindable properties count too, so a base options class with DataAnnotations still needs validation enabled on the derived options registration.

Before:

```csharp
public class BillingOptions
{
    [Required]
    public string ApiKey { get; set; } = "";
}

public sealed class StripeOptions : BillingOptions
{
    public string WebhookSecret { get; set; } = "";
}

services.AddOptions<StripeOptions>()
    .BindConfiguration("Stripe")
    .ValidateOnStart();
```

After:

```csharp
services.AddOptions<StripeOptions>()
    .BindConfiguration("Stripe")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

`Validate(...)` counts as validation for `CFG003`, but it does not satisfy `CFG004` when DataAnnotations attributes are present.

### `CFG005`: Nested Options Need Recursive Validation

DataAnnotations do not automatically walk into child objects or collection items. If a nested class or collection item has validation attributes anywhere in its bindable object graph, mark each parent property that should be checked recursively.

Before:

```csharp
public sealed class AppOptions
{
    public DatabaseOptions Database { get; set; } = new();
}

public sealed class DatabaseOptions
{
    [Required]
    public string ConnectionString { get; set; } = "";
}
```

After:

```csharp
using Microsoft.Extensions.Options;

public sealed class AppOptions
{
    [ValidateObjectMembers]
    public DatabaseOptions Database { get; set; } = new();
}

public sealed class DatabaseOptions
{
    [Required]
    public string ConnectionString { get; set; } = "";
}
```

For arrays and other `IEnumerable<T>` option collections, use `[ValidateEnumeratedItems]`. `CFG005` does not report interface-typed nested properties or system scalar types because the Options validator cannot safely infer a concrete object graph for those shapes.

### `CFG006`: Config Keys Should Match Options Properties

Keys under a bound section should match public bindable properties, or a `[ConfigurationKeyName]` alias.

Before:

```json
{
  "Stripe": {
    "ApiKey": "secret",
    "WebookSecret": "typo"
  }
}
```

```csharp
public sealed class StripeOptions
{
    public string ApiKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
}
```

After:

```json
{
  "Stripe": {
    "ApiKey": "secret",
    "WebhookSecret": "secret"
  }
}
```

`CFG006` is informational because .NET configuration binding allows flexible shapes. It is still useful for catching the typos that hide in environment-specific settings.

Visible `appsettings*.json` files are treated as a merged configuration view for unknown-key checks. If a bound section appears in `appsettings.json` and `appsettings.Production.json`, keys from both files are checked. Nested options objects and arrays or lists of nested options objects are checked recursively, so typos under `Servers:0:Port`-style data can still be found.

Dictionary entries and scalar array items are treated as values rather than property names. Arbitrary keys under `Dictionary<string, string>` and values inside `string[]` are not reported as unknown options properties.

## Design Principles

- Prefer warnings for configuration failures that are likely to break production.
- Keep flexible binding shapes quiet when static proof is weak.
- Offer fixes only when the rewrite is narrow and deterministic.
- Treat `appsettings*.json` as the contract your options classes are supposed to honor.

## Current Scope

ConfigContraband currently focuses on:

- `appsettings*.json` files.
- `AddOptions<T>().BindConfiguration("Section")` registrations.
- String-literal section names.
- Public bindable properties on options types, including inherited bindable properties.
- `[ConfigurationKeyName]` aliases.
- Normal fluent chains and immediate same-block local `OptionsBuilder<T>` chains.

It does not try to prove every possible dynamic configuration shape. When the analyzer cannot see enough static information, it stays quiet.
