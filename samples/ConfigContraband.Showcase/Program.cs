using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// CFG001: "Strpie" is a typo. appsettings.json contains "Stripe".
services.AddOptions<MissingSectionOptions>()
    .BindConfiguration("Strpie");

// CFG002: the "RequiredKey" section exists but is missing the [Required] ApiKey key.
services.AddOptions<RequiredKeyOptions>()
    .BindConfiguration("RequiredKey")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// CFG003: custom validation is registered, but startup validation is missing.
services.AddOptions<StartupValidationOptions>()
    .BindConfiguration("StartupValidation")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Endpoint));

// CFG004: DataAnnotations attributes are present, but ValidateDataAnnotations() is missing.
services.AddOptions<DataAnnotationOptions>()
    .BindConfiguration("DataAnnotations");

// CFG005: DatabaseOptions has validation attributes, but the parent property is not recursive.
services.AddOptions<NestedValidationOptions>()
    .BindConfiguration("NestedValidation")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// CFG006: appsettings.json contains "WebookSecret", which does not match the options type.
services.AddOptions<UnknownKeyOptions>()
    .BindConfiguration("UnknownKey")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// CFG007: strict binding turns the same unknown-key shape into a startup failure.
services.AddOptions<StrictUnknownKeyOptions>()
    .BindConfiguration("StrictUnknownKey", options => options.ErrorOnUnknownConfiguration = true)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// CFG008: "eighty" cannot be converted to the int Port property; the binder throws at bind time.
services.AddOptions<ConversionOptions>()
    .BindConfiguration("Conversion");

_ = services;

// CFG009: a direct read of a section path that is not in appsettings.json throws at runtime.
public sealed class DirectReader
{
    public void Read(IConfiguration configuration)
    {
        _ = configuration.GetRequiredSection("Strpie");
    }
}

public sealed class MissingSectionOptions
{
    public string ApiKey { get; set; } = "";
}

public sealed class RequiredKeyOptions
{
    [Required]
    public string ApiKey { get; set; } = "";
}

public sealed class StartupValidationOptions
{
    public string Endpoint { get; set; } = "";
}

public sealed class DataAnnotationOptions
{
    [Required]
    public string ApiKey { get; set; } = "";
}

public sealed class NestedValidationOptions
{
    public DatabaseOptions Database { get; set; } = new();
}

public sealed class DatabaseOptions
{
    [Required]
    public string ConnectionString { get; set; } = "";
}

public sealed class UnknownKeyOptions
{
    [Required]
    public string ApiKey { get; set; } = "";

    public string WebhookSecret { get; set; } = "";
}

public sealed class StrictUnknownKeyOptions
{
    [Required]
    public string ApiKey { get; set; } = "";

    public string WebhookSecret { get; set; } = "";
}

public sealed class ConversionOptions
{
    public int Port { get; set; }
}
