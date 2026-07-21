<p align="center">
  <img src="assets/configcontraband-icon.png" width="96" height="96" alt="ConfigContraband icon">
</p>

# ConfigContraband

[![CI](https://github.com/georgepwall1991/ConfigContraband/actions/workflows/ci.yml/badge.svg)](https://github.com/georgepwall1991/ConfigContraband/actions/workflows/ci.yml)
[![CodeQL](https://github.com/georgepwall1991/ConfigContraband/actions/workflows/codeql.yml/badge.svg)](https://github.com/georgepwall1991/ConfigContraband/actions/workflows/codeql.yml)
[![codecov](https://codecov.io/gh/georgepwall1991/ConfigContraband/branch/main/graph/badge.svg)](https://codecov.io/gh/georgepwall1991/ConfigContraband)

Stop smuggling broken `appsettings` into production.

ConfigContraband is a high-signal Roslyn analyzer for .NET configuration, ASP.NET Core Options, `appsettings.json`, `ValidateOnStart()`, and `ValidateDataAnnotations()`. It catches the configuration mistakes that compile cleanly, pass code review, and then fail at startup or, worse, on first use.

It focuses on the boring production failures:

- a section name typo in `BindConfiguration(...)`
- a required configuration key missing from all visible `appsettings*.json` files
- validation that exists but does not run on startup
- `[Required]` properties that are never wired into Options validation
- nested options that look validated but are silently skipped
- misspelled JSON keys hiding under a bound section
- strict binding that will throw because an unknown key is present
- scalar values that the configuration binder cannot convert to the target CLR type
- direct configuration reads whose path is unavailable from visible appsettings files

Use it when your app relies on strongly typed options and you want configuration validation feedback in the editor, in pull requests, and in CI before a bad setting reaches production.

## Feature Snapshot

| Area | What ConfigContraband does |
|------|----------------------------|
| Section binding | Checks supported options bindings against visible `appsettings.json` and `appsettings.*.json` files. |
| Required keys | Warns when a DataAnnotations-required key is missing from all visible configuration files. |
| Startup validation | Flags options validation that is registered but not forced to run at startup. |
| DataAnnotations | Finds `[Required]`, `[Range]`, and inherited validation attributes without `ValidateDataAnnotations()`. |
| Nested validation | Detects nested options objects and collections that need recursive validation attributes. |
| JSON key drift | Reports likely misspelled keys under bound sections while staying conservative for flexible binding shapes. |
| Strict binding | Warns when `ErrorOnUnknownConfiguration` makes an unknown key a binding failure. |
| Value conversion | Warns when a visible appsettings scalar provably cannot convert to a bound property or direct `GetValue<T>` target type. |
| Direct reads | Checks supported direct `IConfiguration` reads against visible appsettings paths. |

## Install

```xml
  <PackageReference Include="ConfigContraband" Version="0.7.21" PrivateAssets="all" />
```

The package includes `buildTransitive` props that pass visible `appsettings.json` and `appsettings.*.json` files to the analyzer automatically. Add the package, build, and let your editor or CI tell you when your options contract and configuration drift apart.

No runtime dependency is added to your app. ConfigContraband runs as an analyzer during build and in supported IDEs.

## What It Looks At

ConfigContraband analyzes options registrations shaped like this:

```csharp
services.AddOptions<StripeOptions>()
    .BindConfiguration("Stripe");
```

Named options use the same supported `OptionsBuilder<T>` shape:

```csharp
services.AddOptions<StripeOptions>("tenant")
    .BindConfiguration("Stripe")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

It also recognizes the common explicit-section style:

```csharp
services.AddOptions<StripeOptions>()
    .Bind(configuration.GetSection("Stripe"));

services.Configure<StripeOptions>(
    configuration.GetSection("Stripe"));
```

The section name must resolve to a compile-time constant string. Literals, `const` values, and `nameof(...)` are supported; code-fix availability depends on whether the anchored expression can be rewritten safely. The analyzer follows normal fluent chains:

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

The same tracking works when binding happens after the builder is declared:

```csharp
var optionsBuilder = services.AddOptions<StripeOptions>();
optionsBuilder.BindConfiguration("Stripe");
optionsBuilder.ValidateDataAnnotations();
optionsBuilder.ValidateOnStart();
```

Validation calls on that same local builder may appear in the builder initializer, before the bind statement, or after it. Looking forward from the bind, the scan skips *inert* intervening statements — an unrelated call or assignment (for example an interleaved `services.AddSingleton<T>()`) or a local declaration — so a later `ValidateOnStart()` is still recognized. It stops at anything that could keep the later call from running on every path (control flow such as `if`/`return`/`throw`/loops) or that reassigns the builder variable. It does not guess across wider control flow, aliases, or non-local storage.

When the analyzer cannot prove a configuration shape statically, it stays quiet. The goal is high-signal feedback, not noisy guesses.

## Rules

| ID | Rule | Default | Catches |
|----|------|---------|---------|
| `CFG001` | Bound configuration section does not exist | Warning | `BindConfiguration("Strpie")` when only `Stripe` exists. |
| `CFG002` | Required configuration key is missing | Warning | `[Required]` reference, string, or nullable value property missing from all visible `appsettings*.json` sections when DataAnnotations validation is enabled and no compile-time default already satisfies the attribute. |
| `CFG003` | Options validation does not run on startup | Warning | Validation is registered but `ValidateOnStart()` is missing. |
| `CFG004` | DataAnnotations are not enabled for options validation | Warning | `[Required]`, `[Range]`, inherited annotations, or `IValidatableObject` without `ValidateDataAnnotations()`. |
| `CFG005` | Nested options validation is not recursive | Warning | Nested objects or item types with annotations or `IValidatableObject`, but no recursive validation attribute. |
| `CFG006` | Unknown configuration key under bound section | Info | JSON keys that do not match bindable options properties or aliases. |
| `CFG007` | Unknown configuration key will throw during binding | Warning | JSON keys that do not match bindable options properties while `ErrorOnUnknownConfiguration` is enabled. |
| `CFG008` | Configuration value cannot be bound to the target type | Warning | Scalar values that provably cannot convert to a bound property or direct generic/non-generic `GetValue` target type, e.g. `"Port": "eighty"` for an `int`. |
| `CFG009` | Direct configuration path is unavailable from visible appsettings files | Warning | `configuration.GetRequiredSection("Strpie")` (throws at runtime), near-miss `GetSection("Strpie").Get<T>()`/`.Bind(instance)` typos (bind nothing), and provable `GetConnectionString` typos. |

## appsettings IntelliSense (schema generation)

ConfigContraband also works the other way around. Instead of only flagging `appsettings.json` mistakes
after the fact, it can **generate a JSON Schema from your options types** so your editor gives you
autocomplete, type checking, required-key hints, and unknown-key warnings *while you type*.

Install the companion tool and generate the schema:

```bash
dotnet tool install --global ConfigContraband.Tool
configcontraband schema --project src/MyApp/MyApp.csproj
```

That writes `appsettings.schema.json` next to your project. Point your settings file at it:

```json
{
  "$schema": "appsettings.schema.json",
  "Stripe": {
    "ApiKey": "sk_live_..."
  }
}
```

Now VS Code, Rider, and Visual Studio give you, live as you edit JSON:

- **Key completion** for every bound section and property, derived from your options classes.
- **Type checking** (string vs number vs boolean) and **enum value completion**.
- **Required-field hints** for `[Required]` properties — the same contract `CFG002` enforces.
- **Value constraints from DataAnnotations.** `[Range]` becomes `minimum`/`maximum` (honoring
  `MinimumIsExclusive`/`MaximumIsExclusive`), and `[MaxLength]`/`[StringLength]` become `maxLength`. So an
  out-of-range port or an over-long value is flagged in the editor — the same `ValidateDataAnnotations()`
  failure, caught while typing instead of at startup.
- **Hover documentation.** Your `///` XML doc comments — or `[Description]`/`[DisplayName]` — on options
  properties and types become JSON Schema `description`s, so each setting explains itself on hover.
- **Unknown-key warnings** in the JSON itself. For bindings that set `ErrorOnUnknownConfiguration = true`,
  the schema marks the section `additionalProperties: false`, so the editor flags the typo before the
  app ever starts — the `CFG007` failure, caught while typing.

For example, these options:

```csharp
public sealed class ServerOptions
{
    /// <summary>TCP port the server listens on.</summary>
    [Range(1, 65535)]
    public int Port { get; set; }

    /// <summary>API key used to authenticate outbound calls.</summary>
    [StringLength(64)]
    public string ApiKey { get; set; } = "";
}
```

generate this schema fragment, so the editor enforces the range and maximum length and shows each
setting's documentation on hover:

```json
"Port": {
  "type": "integer",
  "description": "TCP port the server listens on.",
  "minimum": 1,
  "maximum": 65535
},
"ApiKey": {
  "type": "string",
  "description": "API key used to authenticate outbound calls.",
  "maxLength": 64
}
```

The generator reuses the same bindable-property model as the analyzer, including `[ConfigurationKeyName]`
aliases, nested objects, collections, and dictionaries. Every emitted constraint mirrors what
`Microsoft.Extensions.Options` validation actually enforces and is conservative by design: constraints are
only written for bindings that call `ValidateDataAnnotations()` (so loose configuration is never
over-constrained), the generator never emits a constraint that could reject a value the runtime binder
accepts, and loose bindings stay open (`additionalProperties` is not set) so flexible configuration
remains valid. For that reason a few attributes are intentionally left unconstrained: `[RegularExpression]`
(.NET regex differs from JSON Schema's ECMA-262 `pattern`), `[EmailAddress]`/`[Url]` (the strict `format`
grammars are stricter than the attributes' lenient checks), and `[MinLength]` (JSON Schema counts Unicode
code points while DataAnnotations counts UTF-16 units) — each could otherwise flag configuration the runtime
accepts.

Keep the committed schema honest in CI with `--check`, which regenerates in memory and exits non-zero
when the schema is out of date:

```bash
configcontraband schema --project src/MyApp/MyApp.csproj --check
```

## Fast Feedback Loop

The repository includes a showcase project with one intentional example for each rule:

```bash
dotnet build samples/ConfigContraband.Showcase/ConfigContraband.Showcase.csproj --configuration Release --no-incremental
```

The sample stays out of the main solution so normal development builds remain clean.

## Rule Details

### `CFG001`: The Section Must Exist

If your code binds `"Stripe"`, a visible `appsettings.json` or `appsettings.*.json` file should contain a matching `Stripe` section.

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

When ConfigContraband sees a likely typo, it can offer a code fix. The fix keeps regular, verbatim, and raw string literal style when replacing the section name, falling back to an escaped string literal if a raw replacement would need line breaks. Nested section paths use the same colon-separated shape as .NET configuration:

`BindConfiguration(...)` arguments are matched to their semantic parameters, so reordered named arguments such as `configureBinder: ..., configSectionPath: "Strpie"` retain the same diagnostic and section-literal fix.

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

The analyzer checks every visible `appsettings.json` and `appsettings.*.json` additional file for section existence, including commented files, JSON string escapes, colon-delimited keys such as `"Features:Stripe"`, and duplicate JSON section members when resolving nested section paths. Lookalike files such as `appsettingsBackup.json` are ignored. It stays quiet when no appsettings files are available because it cannot prove what configuration exists at runtime.

### `CFG002`: Required Configuration Keys Must Be Present

`CFG002` runs when a supported binding has a visible DataAnnotations validation path. That includes `OptionsBuilder<TOptions>` chains with `ValidateDataAnnotations()` and direct `Configure<TOptions>(GetSection(...))` or `Configure<TOptions>(GetRequiredSection(...))` calls when the same top-level block also registers matching `AddOptions<TOptions>().ValidateDataAnnotations()`. It reports `[Required]` reference, string, or nullable value properties that are missing from every visible `appsettings.json` and `appsettings.*.json` section for that binding.

Before:

```csharp
public sealed class StripeOptions
{
    [Required]
    public string ApiKey { get; set; } = "";
}

services.AddOptions<StripeOptions>()
    .BindConfiguration("Stripe")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

```json
{
  "Stripe": {
  }
}
```

After:

```json
{
  "Stripe": {
    "ApiKey": "secret"
  }
}
```

The rule follows the same runtime validation boundaries as Options validation. C# `required` members are compile-time object-initializer checks, not DataAnnotations validation, so they are not reported. `[Required]` on non-nullable value types is also ignored because the default value is not null. Nested object and collection items are checked only when recursive validation attributes make Options validation walk those values; dictionary value objects stay quiet.

Properties whose compile-time default already satisfies `RequiredAttribute` — and that carry no other validation constraint (another validator on the property, a type-level validation attribute, or `IValidatableObject` on the options type still validates the default, so any of those keeps the key required) — are not reported, because the missing key cannot fail validation: a compile-time constant such as `= "sk_default"`, `= -1`, a `const` field, or `nameof(...)`, an object-creation initializer such as `= new EndpointOptions()` (including target-typed `new()` on a nullable value type, which constructs the underlying value), or a constructor-bound parameter default such as `(string apiKey = "sk_default")` when the constructor provably assigns that parameter to the property — positional records, or a constructor body containing nothing but simple parameter-or-literal assignments to fields or auto-implemented properties including `Property = parameter;` (helper calls, custom setters on assigned members, or other statements could mutate the property and invalidate the proof). A constructor-bound property whose parameter default is not satisfying still counts as defaulted when a satisfying initializer survives a constructor that provably never writes it. A `[Required]` property with recursive validation keeps reporting unless its walked default graph provably passes — only graphs whose constraints are satisfied `[Required]` members qualify; other validation attributes, `IValidatableObject`, polymorphic creations, or instance mutations keep the parent key required in both the diagnostic and the generated schema, because the missing section means startup validation runs against the default instance. An absent non-nullable struct property is evaluated as `default(T)`, so the rule correctly ignores that struct type's own constructor and member initializers when checking nested required members. Empty or whitespace-only string defaults still report (unless the attribute sets `AllowEmptyStrings = true`, where any non-null string default — including a constructed string — satisfies validation), `null!`/`default` initializers still report, parameterless `Nullable<T>` construction still reports regardless of the declared property type or syntax used (`new int?()`, or through a type alias — the empty `Nullable<T>` boxes to null), constructed strings such as `new string(' ', 3)` still report without `AllowEmptyStrings` because the result can be empty or whitespace, initializers still report when the runtime-selected constructor chain could overwrite the property (constructors run after initializers; unused overloads and private factory constructors the binder never executes are ignored), recursive defaults whose object creation uses initializer expressions or constructor arguments still report, constructor defaults still report when the property name is hidden in the type chain, properties with a custom getter still report (validation reads the getter, which may not return the initialized backing value), and non-constant initializers such as method calls stay on the conservative reporting path because the analyzer cannot prove the runtime value. The generated `appsettings.schema.json` `required` array follows the same boundary.

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

The analyzer tracks validation calls on the same fluent chain whether they appear before or after the binding call. The code fix appends `ValidateOnStart()` in the same style as the existing registration chain, including multiline chains and immediate same-block local `OptionsBuilder<T>` chains where binding happens in the initializer or a later local statement. For later local bind statements, adjacent validation calls on the same local are recognized from the builder initializer, before the bind, and after the bind. Registrations that start with `AddOptionsWithValidateOnStart<TOptions>()` already run validation at startup, so `CFG003` stays quiet for that shape.

`CFG003` only treats the framework `OptionsBuilder<TOptions>.Validate(...)`, `ValidateDataAnnotations()`, and `ValidateOnStart()` APIs as validation signals. Custom extension methods with the same names are ignored unless they call the framework APIs in a shape the analyzer can see.

### `CFG004`: DataAnnotations Must Be Switched On

Attributes such as `[Required]` do nothing for Options validation unless `ValidateDataAnnotations()` is registered. Inherited bindable properties count too, including inherited get-only properties populated through a derived constructor, so a base options class with property-level DataAnnotations still needs validation enabled on the derived options registration. Type-level validation attributes declared anywhere in the registered options type's base chain are included as well, because DataAnnotations evaluates inherited `ValidationAttribute`s on the options object itself by default. Nested options graphs count too: if a nested object or list-style collection item has DataAnnotations and is part of the bindable options graph, including constructor-bound records/classes or initialized get-only object or collection properties, the root registration still needs `ValidateDataAnnotations()`. Constructor-bound properties are included only for the single-public-parameterized-constructor shape the runtime binder supports. If a binding call explicitly sets `BindNonPublicProperties = true` on the actual binder-options lambda parameter, public properties with private setters are counted too. `IValidatableObject` is also part of DataAnnotations validation, so options types that implement it need the same registration.

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

The analyzer recognizes `ValidateDataAnnotations()` on the same fluent chain before or after the binding call. The code fix preserves existing fluent-chain formatting, adds `ValidateDataAnnotations()`, and only adds `ValidateOnStart()` when startup validation is not already present, including registrations started with `AddOptionsWithValidateOnStart<TOptions>()`.

Like `CFG003`, `CFG004` symbol-checks the framework validation extension methods. A project-local helper named `ValidateDataAnnotations(...)` does not satisfy the rule by name alone.

### `CFG005`: Nested Options Need Recursive Validation

DataAnnotations do not automatically walk into child objects or collection items. If a nested class or list-style collection item has property-level or type-level validation attributes, or implements `IValidatableObject` anywhere in its bindable object graph, mark each parent property that should be checked recursively. Initialized get-only object and mutable collection properties count because the configuration binder can populate their existing instances. Public private-set nested properties also count when the binding call opts into `BindNonPublicProperties` on the actual binder-options lambda parameter.

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

For arrays and other `IEnumerable<T>` option collections, use `[ValidateEnumeratedItems]`. Constructor-bound nested records/classes are included when there is exactly one public parameterized constructor and its parameters map to public properties, including inherited public properties. The code fix updates the file that owns the options property, uses a `property:` attribute target for record constructor parameters, including `[property: ValidateEnumeratedItems]` on constructor-bound collection parameters, adds `using Microsoft.Extensions.Options;` when needed, respects namespace-local using blocks, avoids project-local attribute name conflicts, and keeps existing property comments in place. `CFG005` does not report interface-typed nested properties, dictionary value objects, or system scalar types because the Options validator cannot safely infer a concrete object graph for those shapes.

### `CFG006`: Config Keys Should Match Options Properties

Keys under a bound section should match public bindable properties. Public settable properties are bindable, constructor-bound records/classes are bindable when there is exactly one public parameterized constructor and its parameters map to public properties, including inherited public properties, and initialized get-only object or mutable collection properties are treated as bindable because the runtime binder can populate them. Public private-set properties are treated as bindable only when the registration explicitly sets `BindNonPublicProperties = true` on the actual binder-options lambda parameter. If a property-bound option uses `[ConfigurationKeyName]`, that configured name replaces the CLR property name for matching; for an overridden virtual setter, the base-most property declaration supplies the runtime name, matching `ConfigurationBinder`. Constructor-bound properties use constructor parameter keys, matching the runtime binder; if the property is also settable after construction, including a private setter enabled by `BindNonPublicProperties`, a `[ConfigurationKeyName]` alias is accepted when the constructor key is present or the constructor parameter has a default value. JSON string escapes are decoded before matching, so escaped property names are treated the same as their runtime configuration keys.

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

Visible `appsettings.json` and `appsettings.*.json` files are treated as a merged configuration view for unknown-key checks, including files with `//` or `/* ... */` comments and files that use colon-delimited keys such as `"Features:Stripe:WebhookSecret"`. Sibling flattened keys under the same nested object are projected into one logical configuration node before analysis. If a bound section appears in `appsettings.json` and `appsettings.Production.json`, keys from both files are checked. Nested options objects, arrays or lists of nested options objects, strongly typed dictionary values, and dictionary values that bind to collections of nested options objects are checked recursively, so typos under `Servers:0:Port`, `Servers:primary:Port`, or `ServersByRegion:eu:0:Port`-style data can still be found. Private-set properties are included for registrations that opt into `BindNonPublicProperties`.

Dictionary entry names and scalar array items are treated as values rather than property names. Arbitrary keys under `Dictionary<string, string>` and values inside `string[]` are not reported as unknown options properties.

Dictionary recursion only applies to key types the real `ConfigurationBinder` actually binds — `string`, an enum, or an integral type (`sbyte` through `ulong`). A dictionary keyed by anything else (`Guid`, `double`, `bool`, `TimeSpan`, a custom struct, ...) is never bound at runtime, so its values are treated as fully opaque: no recursion and no `CFG006`/`CFG007` reporting underneath it, even though the property name itself is still checked normally.

### `CFG007`: Strict Binding Turns Unknown Keys Into Failures

`CFG006` is informational by default because .NET configuration binding is flexible. When a binding call explicitly enables `BinderOptions.ErrorOnUnknownConfiguration`, the same unknown-key shape becomes a binding exception instead of harmless drift.

Before:

```csharp
services.AddOptions<StripeOptions>()
    .BindConfiguration(
        "Stripe",
        options => options.ErrorOnUnknownConfiguration = true)
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

```json
{
  "Stripe": {
    "ApiKey": "secret",
    "WebookSecret": "typo"
  }
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

`CFG007` mostly follows the same property graph as `CFG006`, but only reports when the final value of `ErrorOnUnknownConfiguration` is provably constant `true` on the actual binder-options lambda parameter. It also catches strict-mode failures that loose binding allows, including `[ConfigurationKeyName]` alias keys rejected by the current strict binder, object-shaped data under scalar properties such as `"ApiKey": { "Foo": "x" }`, null/default-initialized settable nested objects, constructor-initialized get-only object values, rejected object-shaped entries inside scalar collections or dictionaries, and unknown object keys behind nested dictionaries, including object collections. CLR property names on scalar objects, null CLR-only nullable values, open interface/object declared or value shapes, property- or constructor-initialized polymorphic reference shapes, matching initializer- or constructor-prepopulated polymorphic dictionary entries including ignore-case dictionary comparers, and nested dictionary entries that the strict binder accepts, unrelated `BinderOptions` instances, escaped binder-options helper calls, non-constant assignments, compound writes, assignments reset to `false`, early-return/control-flow cases, and default binding behaviour stay quiet or on the existing `CFG006` informational path.

### `CFG008`: Configuration Values That Cannot Bind To Their Target Type

The runtime `ConfigurationBinder` stores every configuration value as a string and converts it through `TypeDescriptor.GetConverter(type).ConvertFromInvariantString(value)` under the invariant culture. When that conversion can't succeed — `"Port": "eighty"` for an `int`, `"Level": "Verbos"` for an enum, `"Enabled": "yes"` for a `bool` — the binder throws `InvalidOperationException` while binding or reading the value, before your options ever validate. `CFG008` catches that at build time and points at the offending value in the `appsettings` file. What matters is the value's **string content, not its JSON kind**: `"Port": 8080` and `"Port": "8080"` both bind fine, while `"Port": true` (stored as the string `"True"`) does not.

In addition to options bindings, the rule checks direct calls to the framework generic `ConfigurationBinder.GetValue<T>` API and non-generic `GetValue(Type, ...)` overloads whose target is a direct `typeof(...)` expression when the receiver can be proven to be the host configuration contract and the path is statically known:

```csharp
var port = configuration.GetValue<int>("Server:Port");
var chainedPort = configuration.GetSection("Server").GetValue<int>("Port");
var legacyPort = configuration.GetValue(typeof(int), "Server:Port");
```

Instance and static calls are supported, including named arguments and the default-value overload when its default is a compile-time constant. Non-generic calls whose `Type` flows through a variable, user-defined conversion, or other dynamic expression stay quiet because evaluating it may have side effects and the target is not directly provable. Repeated reads and a matching options-binding diagnostic are deduplicated. The method must come from the real signed Microsoft binder assembly; same-FQN source shadows and unsigned replacement assemblies stay quiet. The same conservative direct-read boundaries as `CFG009` apply: non-constant paths, effectful or otherwise unprovable default expressions, stored `IConfigurationSection` receivers, fields/properties whose configuration origin is not visible (including a `null!` placeholder overwritten in a constructor), locally constructed or mutated configuration, and concrete custom providers. Missing paths, JSON `null`, and object/array values also stay quiet because no scalar conversion failure is statically proven.

The rule fires only on a **provable** conversion failure and is deliberately conservative everywhere the invariant `TryParse` is stricter than the runtime converter:

- **Covered target types:** the integral types (`sbyte`–`ulong`), `float`/`double`/`decimal`, `bool`, `char`, enums, `Guid`, `TimeSpan`, `DateTime`, and `DateTimeOffset` (each unwrapped from `Nullable<T>`).
- **Left alone (never reported):** `string`/`object` targets, JSON `null` (that is `CFG002`'s concern), object/array values under a scalar-typed property (a shape mismatch, not a conversion one), and collection- or dictionary-*element* mismatches such as `List<int>` given `[1, "x"]`.
- **Precision boundaries matched to the binder:** empty or whitespace-only strings are reported for non-nullable numeric, Boolean, enum, `Guid`, and `TimeSpan` targets because their converters throw. An exactly empty nullable value stays quiet because `NullableConverter` maps it to null, while nullable whitespace is delegated to the underlying converter and reports when that converter throws. `char`/`DateTime`/`DateTimeOffset` remain quiet for accepted empty/whitespace forms. `#`/`0x`/`&h`-prefixed hex integers, enum comma-lists (`"Read, Write"`) and numeric enum values within the enum's declared backing-type range, decimal exponent notation, and case-insensitive `bool` values are accepted because the runtime converter accepts them; floating-point and decimal thousands separators report because their runtime converters reject those styles, as do decimal trailing signs.

There is no automatic code fix — like `CFG006`/`CFG007`, the diagnostic points at a JSON additional file rather than at C# the analyzer can rewrite.

### `CFG009`: Direct Configuration Paths Unavailable from Visible Appsettings Files

`CFG001` only sees sections consumed through an options registration, but plenty of code reads configuration directly. A typo there is just as fatal and even quieter: `GetRequiredSection("Strpie")` throws `InvalidOperationException` at runtime, while `GetSection("Strpie").Get<ServerOptions>()` or `.Bind(instance)` silently binds nothing. `CFG009` extends the same missing-section check (including the "Did you mean" suggestion and the code fix that rewrites the literal) to direct reads:

- `configuration.GetRequiredSection("Section")` — reported when the section is missing from every `appsettings*.json` file. Chained paths (`GetSection("Parent").GetRequiredSection("Child")`), constant and `nameof` keys, `?.`/parenthesized/null-forgiving receivers, and host `IConfiguration`/`IConfigurationRoot`/`ConfigurationManager` contracts are resolved through the same path machinery as `CFG001`.
- `configuration.GetSection("Section").Get<T>()` / `.Bind(instance)` — reported only when the missing path is a near-miss of a declared sibling. A `Bind` instance argument and any `Get`/`Bind` binder-options callback must be provably free of configuration side effects; simple constant assignments to the real `BinderOptions` parameter are supported, while helper calls, property getters, captures, receiver-aliasing bind targets, and other effectful or unproven expressions stay quiet because they can mutate configuration before binding begins. Plain misses stay quiet because environment providers may supply them. A bare `GetSection(...)` with no binder consumer also stays quiet: probing with `.Exists()` is idiomatic and `GetSection` never throws.
- `configuration.Bind("Section", instance)` — follows the same suggestion-gated policy for a root key or a key relative to a known `GetSection(...)` chain. Instance and static calls are supported, including named arguments, when evaluating the instance argument is provably side-effect free; helper calls, property getters, and other effectful or unproven expressions stay quiet because they can mutate configuration before binding begins.
- `configuration.GetConnectionString("Name")` — connection strings are routinely supplied by environment variables or secret stores, so a plain miss is not reported. At the root or after a statically known `GetSection(...)` chain, the rule fires only when the corresponding `ConnectionStrings` section exists in appsettings and the name is a near-miss of a declared entry — a provable typo. Instance and static named calls share this relative-path behavior; calls through a stored `IConfigurationSection` stay quiet because its origin is no longer visible.

The rule stays quiet whenever the absolute path or receiver provenance cannot be proven: non-constant keys, reads off a stored or parameter-typed `IConfigurationSection` (its own path is invisible), concrete custom `IConfiguration` implementations, locally constructed `ConfigurationBuilder`/`ConfigurationManager` roots, and receiver locals that are conditionally reassigned, mutated, escaped, or captured. Same-block straight-line assignments and aliases are followed — including harmless non-user-defined interface casts — so a local that ultimately points back to the host contract is still checked. Framework direct-read methods require the signed Microsoft symbol, so same-FQN `ConfigurationExtensions` and `ConfigurationBinder` source shadows stay quiet. Constant signed-framework `GetSection(...)` chains feeding `Get<T>()` or either section-based or keyed `Bind(...)` are reconstructed through conditional access, including a single root link, multiple nested links, or a statically known ordinary `GetSection` prefix; dynamic keys, stored sections, mixed framework methods, and other effectful or unprovable conditional shapes remain quiet. Reads that feed a recognized options registration — `services.Configure<T>(configuration.GetRequiredSection("X"))` — are left to `CFG001` so the same miss is not reported twice, and a chain whose `GetRequiredSection` parent is already missing reports only once, at the parent. Runtime section existence follows the referenced JSON provider: .NET 10 empty objects and explicit `null` are missing, while empty arrays exist; unknown version-sensitive shapes stay quiet. The `configuration["key"]` indexer remains deliberately out of scope.

## Design Principles

- Prefer warnings for configuration failures that are likely to break production.
- Keep flexible binding shapes quiet when static proof is weak.
- Offer fixes only when the rewrite is narrow and deterministic.
- Treat `appsettings.json` and `appsettings.*.json` as the contract your options classes are supposed to honor.

## Current Scope

ConfigContraband currently focuses on:

- `appsettings.json` and `appsettings.*.json` files.
- `AddOptions<T>().BindConfiguration("Section")` registrations.
- `AddOptions<T>().Bind(configuration.GetSection("Section"))` and `GetRequiredSection(...)` registrations.
- Direct `Configure<T>(configuration.GetSection("Section"))` and `GetRequiredSection(...)` registrations for section and JSON-key drift.
- Direct framework generic `ConfigurationBinder.GetValue<T>` and non-generic `GetValue(typeof(T), ...)` reads for provable scalar conversion failures (`CFG008`).
- Direct configuration reads: standalone `GetRequiredSection(...)`, suggestion-gated `GetSection(...).Get<T>()`/`.Bind(instance)`, keyed `Bind("key", instance)`, and suggestion-gated `GetConnectionString(...)` (`CFG009`).
- Strict `ErrorOnUnknownConfiguration` binder options for unknown-key failures.
- Compile-time constant section names, including literals, `const` values, and `nameof` expressions.
- Public bindable properties on options types, including inherited and constructor-bound bindable properties.
- `[ConfigurationKeyName]` key-name overrides.
- Normal fluent chains and immediate same-block local `OptionsBuilder<T>` chains.

It does not try to prove every possible dynamic configuration shape. When the analyzer cannot see enough static information, it stays quiet.
