using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandCodeFixTests
{
    [Fact]
    public async Task Cfg005_fix_adds_validate_object_members()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions {|#1:Database|} { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";
            }
            """);

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
                }
            }

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
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg005_fix_adds_validate_object_members_for_type_level_validation_attribute()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, extraUsings: "using System;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions {|#1:Database|} { get; set; } = new();
            }

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class ValidDatabaseOptionsAttribute : ValidationAttribute
            {
                protected override ValidationResult IsValid(object value, ValidationContext validationContext)
                {
                    return ValidationResult.Success!;
                }
            }

            [ValidDatabaseOptions]
            public sealed class DatabaseOptions
            {
                public string ConnectionString { get; set; } = "";
            }
            """);

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using System;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
                }
            }

            public sealed class AppOptions
            {
                [ValidateObjectMembers]
                public DatabaseOptions Database { get; set; } = new();
            }

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class ValidDatabaseOptionsAttribute : ValidationAttribute
            {
                protected override ValidationResult IsValid(object value, ValidationContext validationContext)
                {
                    return ValidationResult.Success!;
                }
            }

            [ValidDatabaseOptions]
            public sealed class DatabaseOptions
            {
                public string ConnectionString { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg005_fix_adds_validate_object_members_to_initialized_get_only_property()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions {|#1:Database|} { get; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";
            }
            """);

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
                }
            }

            public sealed class AppOptions
            {
                [ValidateObjectMembers]
                public DatabaseOptions Database { get; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg005_fix_adds_validate_object_members_to_constructor_bound_record_property()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, optionsTypes: """
            public sealed record AppOptions(DatabaseOptions {|#1:Database|});

            public sealed record DatabaseOptions([property: Required] string ConnectionString);
            """);

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
                }
            }

            public sealed record AppOptions([property: ValidateObjectMembers] DatabaseOptions Database);

            public sealed record DatabaseOptions([property: Required] string ConnectionString);
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg005_fix_adds_validate_object_members_to_constructor_bound_inherited_property()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, optionsTypes: """
            public abstract class BaseAppOptions
            {
                protected BaseAppOptions(DatabaseOptions database)
                {
                    Database = database;
                }

                public DatabaseOptions {|#1:Database|} { get; }
            }

            public sealed class AppOptions : BaseAppOptions
            {
                public AppOptions(DatabaseOptions database)
                    : base(database)
                {
                }
            }

            public sealed record DatabaseOptions([property: Required] string ConnectionString);
            """);

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
                }
            }

            public abstract class BaseAppOptions
            {
                protected BaseAppOptions(DatabaseOptions database)
                {
                    Database = database;
                }

                [ValidateObjectMembers]
                public DatabaseOptions Database { get; }
            }

            public sealed class AppOptions : BaseAppOptions
            {
                public AppOptions(DatabaseOptions database)
                    : base(database)
                {
                }
            }

            public sealed record DatabaseOptions([property: Required] string ConnectionString);
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("BaseAppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg005_fix_adds_validate_enumerated_items_to_constructor_bound_record_collection()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed record AppOptions(List<ServerOptions> {|#1:Servers|});

            public sealed record ServerOptions([property: Required] string Host);
            """);

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using System.Collections.Generic;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart();
                }
            }

            public sealed record AppOptions([property: ValidateEnumeratedItems] List<ServerOptions> Servers);

            public sealed record ServerOptions([property: Required] string Host);
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Servers");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg005_fix_adds_validate_object_members_to_private_set_property_when_bind_non_public_properties_enabled()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.BindNonPublicProperties = true)
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, optionsTypes: """
            public sealed class AppOptions
            {
                public DatabaseOptions {|#1:Database|} { get; private set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; private set; } = "";
            }
            """);

        var fixedSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<AppOptions>()
                .BindConfiguration("App", options => options.BindNonPublicProperties = true)
                .ValidateDataAnnotations()
                .ValidateOnStart();
                }
            }

            public sealed class AppOptions
            {
                [ValidateObjectMembers]
                public DatabaseOptions Database { get; private set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; private set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }
}
