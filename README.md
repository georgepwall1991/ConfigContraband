# ConfigContraband

Stop smuggling broken `appsettings` into production.

ConfigContraband is a Roslyn analyser for .NET configuration and the Options pattern. It spots configuration mistakes while you are coding, before the app starts up and falls over.

## Quickstart

```xml
<PackageReference Include="ConfigContraband" Version="0.1.0" PrivateAssets="all" />
```

The package automatically passes `appsettings*.json` files to the analyser through `buildTransitive` props.

## Try The Analyzer

The repo includes a standalone showcase project with one intentional example for each rule:

```bash
dotnet build samples/ConfigContraband.Showcase/ConfigContraband.Showcase.csproj --configuration Release --no-incremental
```

The sample is kept out of the main solution so normal builds stay clean.

## What It Checks

ConfigContraband checks options registrations that use:

```csharp
services.AddOptions<MyOptions>()
    .BindConfiguration("MySection");
```

The section name must be a string the compiler can read. The analyser follows normal fluent chains:

```csharp
services.AddOptions<StripeOptions>()
    .BindConfiguration("Stripe")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

It also follows simple split local chains in the same block:

```csharp
var optionsBuilder = services.AddOptions<StripeOptions>()
    .BindConfiguration("Stripe");

optionsBuilder.ValidateDataAnnotations();
optionsBuilder.ValidateOnStart();
```

## Rules

| ID | Rule | Default | What it means |
|----|------|---------|---------------|
| `CFG001` | Bound configuration section does not exist | Warning | The section passed to `BindConfiguration(...)` was not found in `appsettings*.json`. |
| `CFG003` | Options validation does not run on startup | Warning | Validation is registered, but `ValidateOnStart()` is missing. |
| `CFG004` | DataAnnotations are not enabled for options validation | Warning | The options type uses attributes like `[Required]`, but `ValidateDataAnnotations()` is missing. |
| `CFG005` | Nested options validation is not recursive | Warning | A nested object or collection contains validation attributes, but recursive validation is not enabled. |
| `CFG006` | Unknown configuration key under bound section | Info | A key in `appsettings*.json` does not match a bindable options property. |

## Clear Rules

### `CFG001`: The Section Must Exist

If your code says `BindConfiguration("Stripe")`, there should be a matching `Stripe` section in `appsettings*.json`.

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

ConfigContraband can offer a fix when it can see a likely spelling mistake.

Nested paths use colon-separated section names, the same shape used by .NET configuration:

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

The analyser checks every visible `appsettings*.json` additional file for section existence. It stays quiet when no appsettings files are available, because it cannot prove whether the section exists at runtime.

### `CFG003`: Validation Should Run When The App Starts

Options validation normally runs later, when the options are first used. `ValidateOnStart()` makes the app check them during startup.

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

Attributes such as `[Required]` do nothing for options unless `ValidateDataAnnotations()` is registered.
Inherited bindable properties count too, so a base options class with DataAnnotations still needs validation enabled on the derived options registration.

Before:

```csharp
public sealed class StripeOptions
{
    [Required]
    public string ApiKey { get; set; } = "";
}

services.AddOptions<StripeOptions>()
    .BindConfiguration("Stripe");
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

For arrays and other `IEnumerable<T>` option collections, use `[ValidateEnumeratedItems]`. `CFG005` does not report interface-typed nested properties or system scalar types because the options validator cannot safely infer a concrete object graph for those shapes.

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

`CFG006` is informational because .NET configuration binding allows flexible shapes. It is still useful for catching typos.

The analyser treats visible `appsettings*.json` files as a merged configuration view for unknown-key checks: if a bound section appears in `appsettings.json` and `appsettings.Production.json`, keys from both files are checked. Nested options objects and arrays or lists of nested options objects are checked recursively, so typos under `Servers:0:Port`-style data can still be found.

Dictionary entries and scalar array items are treated as values rather than property names. For example, arbitrary keys under `Dictionary<string, string>` and values inside `string[]` are not reported as unknown options properties.

## Explained Like You're Ten

Think of `appsettings.json` as a list of instructions for your app.

Your C# options class is the checklist that says what instructions are allowed.

ConfigContraband is like a careful teacher checking your work before you hand it in:

- Did you ask for a section called `Strpie` when the list says `Stripe`?
- Did you write rules like `[Required]` but forget to switch the rule checker on?
- Did you switch the checker on, but only after the app has already started doing work?
- Did you put a smaller checklist inside a bigger checklist, then forget to check the smaller one?
- Did you type `WebookSecret` when you meant `WebhookSecret`?

The aim is simple: catch the silly mistakes early, while they are cheap to fix, rather than finding them in production.

## Current Scope

ConfigContraband currently focuses on:

- `appsettings*.json` files.
- `AddOptions<T>().BindConfiguration("Section")` registrations.
- String-literal section names.
- Public bindable properties on options types.
- `[ConfigurationKeyName]` aliases.
- Normal fluent chains and immediate same-block local `OptionsBuilder<T>` chains.

It does not try to prove every possible dynamic configuration shape. When the analyser cannot see enough static information, it stays quiet.
