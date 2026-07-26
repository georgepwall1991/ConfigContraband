using Microsoft.CodeAnalysis;

namespace ConfigContraband;

public static class DiagnosticDescriptors
{
    private const string Category = "Configuration";
    private const string DocumentationBaseUrl = "https://github.com/georgepwall1991/ConfigContraband#";

    public static readonly DiagnosticDescriptor MissingConfigurationSection = new(
        id: DiagnosticIds.MissingConfigurationSection,
        title: "Bound configuration section does not exist",
        messageFormat: "Configuration section \"{0}\" was not found{1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The section passed to BindConfiguration should exist in appsettings configuration files.",
        helpLinkUri: DocumentationBaseUrl + "cfg001-the-section-must-exist");

    public static readonly DiagnosticDescriptor MissingRequiredConfigurationKey = new(
        id: DiagnosticIds.MissingRequiredConfigurationKey,
        title: "Required configuration key is missing",
        messageFormat: "Required configuration key \"{0}\" is missing from section \"{1}\"",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "DataAnnotations [Required] properties that can fail validation should be present in the configuration section.",
        helpLinkUri: DocumentationBaseUrl + "cfg002-required-configuration-keys-must-be-present");

    public static readonly DiagnosticDescriptor ValidationNotOnStart = new(
        id: DiagnosticIds.ValidationNotOnStart,
        title: "Options validation does not run on startup",
        messageFormat: "{0} has validation, but it is not configured to validate on startup",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Options validation normally runs when options are first created. Add ValidateOnStart() to fail during startup.",
        helpLinkUri: DocumentationBaseUrl + "cfg003-validation-should-run-when-the-app-starts");

    public static readonly DiagnosticDescriptor DataAnnotationsNotEnabled = new(
        id: DiagnosticIds.DataAnnotationsNotEnabled,
        title: "DataAnnotations are not enabled for options validation",
        messageFormat: "{0} uses DataAnnotations, but ValidateDataAnnotations() is not registered",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Options classes that use DataAnnotations need ValidateDataAnnotations() in the options registration chain.",
        helpLinkUri: DocumentationBaseUrl + "cfg004-dataannotations-must-be-switched-on");

    public static readonly DiagnosticDescriptor NestedValidationNotRecursive = new(
        id: DiagnosticIds.NestedValidationNotRecursive,
        title: "Nested options validation is not recursive",
        messageFormat: "{0}.{1} contains validation attributes, but nested validation is not enabled",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "DataAnnotations validation does not recursively validate nested objects or collection items unless recursive validation attributes are used.",
        helpLinkUri: DocumentationBaseUrl + "cfg005-nested-options-need-recursive-validation");

    public static readonly DiagnosticDescriptor UnknownConfigurationKey = new(
        id: DiagnosticIds.UnknownConfigurationKey,
        title: "Unknown configuration key under bound section",
        messageFormat: "Configuration key \"{0}\" does not match any bindable property on {1}{2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A key under a bound appsettings section does not match any public bindable property on the options type.",
        helpLinkUri: DocumentationBaseUrl + "cfg006-config-keys-should-match-options-properties");

    public static readonly DiagnosticDescriptor UnknownConfigurationKeyWillThrow = new(
        id: DiagnosticIds.UnknownConfigurationKeyWillThrow,
        title: "Unknown configuration key will throw during binding",
        messageFormat: "Configuration key \"{0}\" will fail strict binding for {1}{2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A key under a bound appsettings section will be rejected while BinderOptions.ErrorOnUnknownConfiguration is enabled.",
        helpLinkUri: DocumentationBaseUrl + "cfg007-strict-binding-turns-unknown-keys-into-failures");

    public static readonly DiagnosticDescriptor ConfigurationValueTypeMismatch = new(
        id: DiagnosticIds.ConfigurationValueTypeMismatch,
        title: "Configuration value cannot be bound to the target type",
        messageFormat: "Configuration value for \"{0}\" cannot be bound to {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A scalar value in appsettings cannot be converted to a bound property or direct read's CLR target type, so the configuration binder will throw during binding or reading.",
        helpLinkUri: DocumentationBaseUrl + "cfg008-configuration-values-that-cannot-bind-to-their-target-type");

    public static readonly DiagnosticDescriptor ConfigurationKeyNotFound = new(
        id: DiagnosticIds.ConfigurationKeyNotFound,
        title: "Direct configuration path is unavailable from visible appsettings files",
        messageFormat: "Configuration path \"{0}\" read here is unavailable from visible appsettings files{1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Supported paths read directly from IConfiguration (GetRequiredSection, GetSection().Get<T>()/.Bind(), GetConnectionString) should be available from a visible appsettings configuration file.",
        helpLinkUri: DocumentationBaseUrl + "cfg009-direct-configuration-paths-unavailable-from-visible-appsettings-files");
}
