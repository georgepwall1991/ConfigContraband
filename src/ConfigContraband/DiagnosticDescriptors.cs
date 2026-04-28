using Microsoft.CodeAnalysis;

namespace ConfigContraband;

public static class DiagnosticDescriptors
{
    private const string Category = "Configuration";

    public static readonly DiagnosticDescriptor MissingConfigurationSection = new(
        id: DiagnosticIds.MissingConfigurationSection,
        title: "Bound configuration section does not exist",
        messageFormat: "Configuration section \"{0}\" was not found{1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The section passed to BindConfiguration should exist in appsettings configuration files.");

    public static readonly DiagnosticDescriptor ValidationNotOnStart = new(
        id: DiagnosticIds.ValidationNotOnStart,
        title: "Options validation does not run on startup",
        messageFormat: "{0} has validation, but it is not configured to validate on startup",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Options validation normally runs when options are first created. Add ValidateOnStart() to fail during startup.");

    public static readonly DiagnosticDescriptor DataAnnotationsNotEnabled = new(
        id: DiagnosticIds.DataAnnotationsNotEnabled,
        title: "DataAnnotations are not enabled for options validation",
        messageFormat: "{0} uses DataAnnotations, but ValidateDataAnnotations() is not registered",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Options classes that use DataAnnotations need ValidateDataAnnotations() in the options registration chain.");

    public static readonly DiagnosticDescriptor NestedValidationNotRecursive = new(
        id: DiagnosticIds.NestedValidationNotRecursive,
        title: "Nested options validation is not recursive",
        messageFormat: "{0}.{1} contains validation attributes, but nested validation is not enabled",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "DataAnnotations validation does not recursively validate nested objects or collection items unless recursive validation attributes are used.");

    public static readonly DiagnosticDescriptor UnknownConfigurationKey = new(
        id: DiagnosticIds.UnknownConfigurationKey,
        title: "Unknown configuration key under bound section",
        messageFormat: "Configuration key \"{0}\" does not match any bindable property on {1}{2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A key under a bound appsettings section does not match any public bindable property on the options type.");
}
