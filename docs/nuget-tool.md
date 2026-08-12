<p align="center">
  <img src="https://raw.githubusercontent.com/georgepwall1991/ConfigContraband/main/assets/configcontraband-icon.png" width="96" height="96" alt="ConfigContraband icon — appsettings JSON Schema generator">
</p>

# ConfigContraband.Tool

Generate `appsettings.schema.json` from your .NET Options model for editor IntelliSense, type checking, and required-key hints that match `ValidateDataAnnotations` and `BindConfiguration`.

Requires a **.NET 10 SDK**. Pair with the [ConfigContraband](https://www.nuget.org/packages/ConfigContraband) analyzer for compile-time Options validation.

## Install

```bash
dotnet tool install --global ConfigContraband.Tool
```

## Generate a schema

```bash
configcontraband schema --project src/MyApp/MyApp.csproj
```

Point your settings file at the schema:

```json
{
  "$schema": "appsettings.schema.json",
  "Stripe": {
    "ApiKey": "sk_live_..."
  }
}
```

## Dual loop

Use the analyzer for build/IDE diagnostics and this tool for live appsettings completion:

![ConfigContraband dual loop: Roslyn analyzer and schema IntelliSense](https://raw.githubusercontent.com/georgepwall1991/ConfigContraband/main/assets/flow-analyzer-schema-loop.svg)

## Docs

- Analyzer package: [ConfigContraband](https://www.nuget.org/packages/ConfigContraband)
- Full docs and samples: [GitHub repository](https://github.com/georgepwall1991/ConfigContraband)
