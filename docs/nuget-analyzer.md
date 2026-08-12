<p align="center">
  <img src="https://raw.githubusercontent.com/georgepwall1991/ConfigContraband/main/assets/configcontraband-icon.png" width="96" height="96" alt="ConfigContraband icon — Roslyn analyzer for .NET Options and appsettings validation">
</p>

# ConfigContraband

**Compile-time Options validation for .NET** — a Roslyn analyzer that checks `BindConfiguration`, `ValidateOnStart`, `ValidateDataAnnotations`, and `appsettings.json` against your `IOptions` types so broken configuration fails in the editor and CI, not production.

## Install

```xml
  <PackageReference Include="ConfigContraband" Version="0.7.28" PrivateAssets="all" />
```

```bash
dotnet add package ConfigContraband
```

No runtime dependency. Analyzers run at build time and in supported IDEs. Visible `appsettings*.json` files are passed automatically via `buildTransitive` props.

## What it catches

- Section typos in `BindConfiguration(...)` / `Configure<T>(GetSection(...))`
- Missing `[Required]` keys in visible appsettings
- Validation without `ValidateOnStart()` or `ValidateDataAnnotations()`
- Nested options that look validated but are skipped
- Misspelled JSON keys, strict unknown-key failures, and scalar conversion errors
- Direct `IConfiguration` reads whose path is missing from visible appsettings

When the analyzer cannot prove a configuration shape statically, it **stays quiet**.

## See it work

![ConfigContraband Roslyn analyzer warnings for Options validation and appsettings](https://raw.githubusercontent.com/georgepwall1991/ConfigContraband/main/assets/flow-ide-diagnostics.svg)

## Next steps

- Copy-paste green sample: [ConfigContraband.Quickstart](https://github.com/georgepwall1991/ConfigContraband/tree/main/samples/ConfigContraband.Quickstart)
- Full rule reference (`CFG001`–`CFG009`): [README on GitHub](https://github.com/georgepwall1991/ConfigContraband#rule-details)
- Optional IntelliSense schema tool: [ConfigContraband.Tool](https://www.nuget.org/packages/ConfigContraband.Tool)
