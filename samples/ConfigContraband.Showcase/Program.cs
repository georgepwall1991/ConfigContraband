using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// CFG001: "Strpie" is a typo. appsettings.json contains "Stripe".
services.AddOptions<MissingSectionOptions>()
    .BindConfiguration("Strpie");

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

_ = services;

public sealed class MissingSectionOptions
{
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
