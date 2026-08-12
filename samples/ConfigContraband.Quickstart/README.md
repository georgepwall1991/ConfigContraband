# ConfigContraband Quickstart

Minimal green Options registration: build this project and expect **zero** `CFG00x` diagnostics.

```bash
dotnet build samples/ConfigContraband.Quickstart/ConfigContraband.Quickstart.csproj --configuration Release --no-incremental -p:ContinuousIntegrationBuild=true
```

The `ContinuousIntegrationBuild` property is required because normal local command-line builds disable analyzers for fast feedback. The sample stays out of the main solution so normal package and test builds stay clean.

## What it shows

```csharp
services.AddOptions<StripeOptions>()
    .BindConfiguration("Stripe")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

`appsettings.json` contains a matching `Stripe` section with the `[Required]` `ApiKey` key. `appsettings.schema.json` is checked in for editor IntelliSense (regenerate with `ConfigContraband.Tool` if you change the Options shape).

For intentional failures (one diagnostic per rule), see [ConfigContraband.Showcase](../ConfigContraband.Showcase/).

CI runs `bash scripts/verify-quickstart.sh` and fails if any `CFG001`–`CFG009` diagnostic appears.
