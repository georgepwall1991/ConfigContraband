using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandCodeFixTests
{
    [Fact]
    public async Task Cfg005_fix_reuses_existing_options_using()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, extraUsings: "using Microsoft.Extensions.Options;", optionsTypes: """
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
    public async Task Cfg005_fix_adds_validate_enumerated_items()
    {
        var source = OptionsSource("""
            {|#0:services.AddOptions<AppOptions>()
                .BindConfiguration("App")
                .ValidateDataAnnotations()
                .ValidateOnStart()|};
            """, extraUsings: "using System.Collections.Generic;\n", optionsTypes: """
            public sealed class AppOptions
            {
                public List<ServerOptions> {|#1:Servers|} { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                [Required]
                public string Host { get; set; } = "";
            }
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

            public sealed class AppOptions
            {
                [ValidateEnumeratedItems]
                public List<ServerOptions> Servers { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                [Required]
                public string Host { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Servers");

        await Verifier.VerifyCodeFixAsync(source, fixedSource, expected);
    }

    [Fact]
    public async Task Cfg005_fix_updates_nested_object_property_in_target_document()
    {
        var startupSource = """
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart()|};
                }
            }
            """;

        var optionsSource = """
            using System.ComponentModel.DataAnnotations;

            public sealed class AppOptions
            {
                // Primary database settings.
                public DatabaseOptions {|#1:Database|} { get; set; } = new();
            }

            public sealed class DatabaseOptions
            {
                [Required]
                public string ConnectionString { get; set; } = "";
            }
            """;

        var fixedStartupSource = """
            using Microsoft.Extensions.DependencyInjection;

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
            """;

        var fixedOptionsSource = """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.Options;

            public sealed class AppOptions
            {
                // Primary database settings.
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

        await Verifier.VerifyCodeFixAsync(
            [
                ("Startup.cs", startupSource),
                ("Options.cs", optionsSource)
            ],
            [
                ("Startup.cs", fixedStartupSource),
                ("Options.cs", fixedOptionsSource)
            ],
            expected);
    }

    [Fact]
    public async Task Cfg005_fix_updates_collection_property_in_target_document_without_duplicate_using()
    {
        var startupSource = """
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart()|};
                }
            }
            """;

        var optionsSource = """
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.Options;

            public sealed class AppOptions
            {
                public List<ServerOptions> {|#1:Servers|} { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                [Required]
                public string Host { get; set; } = "";
            }
            """;

        var fixedStartupSource = """
            using Microsoft.Extensions.DependencyInjection;

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
            """;

        var fixedOptionsSource = """
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.Options;

            public sealed class AppOptions
            {
                [ValidateEnumeratedItems]
                public List<ServerOptions> Servers { get; set; } = [];
            }

            public sealed class ServerOptions
            {
                [Required]
                public string Host { get; set; } = "";
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Servers");

        await Verifier.VerifyCodeFixAsync(
            [
                ("Startup.cs", startupSource),
                ("Options.cs", optionsSource)
            ],
            [
                ("Startup.cs", fixedStartupSource),
                ("Options.cs", fixedOptionsSource)
            ],
            expected);
    }

    [Fact]
    public async Task Cfg005_fix_reuses_namespace_local_options_using()
    {
        var startupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart()|};
                }
            }
            """;

        var optionsSource = """
            namespace MyApp
            {
                using System.ComponentModel.DataAnnotations;
                using Microsoft.Extensions.Options;

                public sealed class AppOptions
                {
                    public DatabaseOptions {|#1:Database|} { get; set; } = new();
                }

                public sealed class DatabaseOptions
                {
                    [Required]
                    public string ConnectionString { get; set; } = "";
                }
            }
            """;

        var fixedStartupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

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
            """;

        var fixedOptionsSource = """
            namespace MyApp
            {
                using System.ComponentModel.DataAnnotations;
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
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(
            [
                ("Startup.cs", startupSource),
                ("Options.cs", optionsSource)
            ],
            [
                ("Startup.cs", fixedStartupSource),
                ("Options.cs", fixedOptionsSource)
            ],
            expected);
    }

    [Fact]
    public async Task Cfg005_fix_adds_options_using_to_namespace_local_usings()
    {
        var startupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart()|};
                }
            }
            """;

        var optionsSource = """
            namespace MyApp
            {
                using System.ComponentModel.DataAnnotations;

                public sealed class AppOptions
                {
                    public DatabaseOptions {|#1:Database|} { get; set; } = new();
                }

                public sealed class DatabaseOptions
                {
                    [Required]
                    public string ConnectionString { get; set; } = "";
                }
            }
            """;

        var fixedStartupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

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
            """;

        var fixedOptionsSource = """
            namespace MyApp
            {
                using System.ComponentModel.DataAnnotations;
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
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(
            [
                ("Startup.cs", startupSource),
                ("Options.cs", optionsSource)
            ],
            [
                ("Startup.cs", fixedStartupSource),
                ("Options.cs", fixedOptionsSource)
            ],
            expected);
    }

    [Fact]
    public async Task Cfg005_fix_qualifies_attribute_when_local_attribute_name_conflicts()
    {
        var startupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart()|};
                }
            }
            """;

        var optionsSource = """
            namespace MyApp
            {
                using System;
                using System.ComponentModel.DataAnnotations;

                public sealed class ValidateObjectMembersAttribute : Attribute
                {
                }

                public sealed class AppOptions
                {
                    public DatabaseOptions {|#1:Database|} { get; set; } = new();
                }

                public sealed class DatabaseOptions
                {
                    [Required]
                    public string ConnectionString { get; set; } = "";
                }
            }
            """;

        var fixedStartupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

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
            """;

        var fixedOptionsSource = """
            namespace MyApp
            {
                using System;
                using System.ComponentModel.DataAnnotations;

                public sealed class ValidateObjectMembersAttribute : Attribute
                {
                }

                public sealed class AppOptions
                {
                    [global::Microsoft.Extensions.Options.ValidateObjectMembersAttribute]
                    public DatabaseOptions Database { get; set; } = new();
                }

                public sealed class DatabaseOptions
                {
                    [Required]
                    public string ConnectionString { get; set; } = "";
                }
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(
            [
                ("Startup.cs", startupSource),
                ("Options.cs", optionsSource)
            ],
            [
                ("Startup.cs", fixedStartupSource),
                ("Options.cs", fixedOptionsSource)
            ],
            expected);
    }

    [Fact]
    public async Task Cfg005_fix_qualifies_constructor_bound_attribute_when_local_attribute_name_conflicts()
    {
        var startupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart()|};
                }
            }
            """;

        var optionsSource = """
            namespace MyApp
            {
                using System;
                using System.ComponentModel.DataAnnotations;

                public sealed class ValidateObjectMembersAttribute : Attribute
                {
                }

                public sealed record AppOptions(DatabaseOptions {|#1:Database|});

                public sealed record DatabaseOptions([property: Required] string ConnectionString);
            }
            """;

        var fixedStartupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

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
            """;

        var fixedOptionsSource = """
            namespace MyApp
            {
                using System;
                using System.ComponentModel.DataAnnotations;

                public sealed class ValidateObjectMembersAttribute : Attribute
                {
                }

                public sealed record AppOptions([property: global::Microsoft.Extensions.Options.ValidateObjectMembersAttribute] DatabaseOptions Database);

                public sealed record DatabaseOptions([property: Required] string ConnectionString);
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Database");

        await Verifier.VerifyCodeFixAsync(
            [
                ("Startup.cs", startupSource),
                ("Options.cs", optionsSource)
            ],
            [
                ("Startup.cs", fixedStartupSource),
                ("Options.cs", fixedOptionsSource)
            ],
            expected);
    }

    [Fact]
    public async Task Cfg005_fix_qualifies_constructor_bound_collection_attribute_when_local_attribute_name_conflicts()
    {
        var startupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart()|};
                }
            }
            """;

        var optionsSource = """
            namespace MyApp
            {
                using System;
                using System.Collections.Generic;
                using System.ComponentModel.DataAnnotations;

                public sealed class ValidateEnumeratedItemsAttribute : Attribute
                {
                }

                public sealed record AppOptions(List<ServerOptions> {|#1:Servers|});

                public sealed record ServerOptions([property: Required] string Host);
            }
            """;

        var fixedStartupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

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
            """;

        var fixedOptionsSource = """
            namespace MyApp
            {
                using System;
                using System.Collections.Generic;
                using System.ComponentModel.DataAnnotations;

                public sealed class ValidateEnumeratedItemsAttribute : Attribute
                {
                }

                public sealed record AppOptions([property: global::Microsoft.Extensions.Options.ValidateEnumeratedItemsAttribute] List<ServerOptions> Servers);

                public sealed record ServerOptions([property: Required] string Host);
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Servers");

        await Verifier.VerifyCodeFixAsync(
            [
                ("Startup.cs", startupSource),
                ("Options.cs", optionsSource)
            ],
            [
                ("Startup.cs", fixedStartupSource),
                ("Options.cs", fixedOptionsSource)
            ],
            expected);
    }

    [Fact]
    public async Task Cfg005_fix_qualifies_collection_attribute_when_local_attribute_name_conflicts()
    {
        var startupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {|#0:services.AddOptions<AppOptions>()
                        .BindConfiguration("App")
                        .ValidateDataAnnotations()
                        .ValidateOnStart()|};
                }
            }
            """;

        var optionsSource = """
            namespace MyApp
            {
                using System;
                using System.Collections.Generic;
                using System.ComponentModel.DataAnnotations;

                public sealed class ValidateEnumeratedItemsAttribute : Attribute
                {
                }

                public sealed class AppOptions
                {
                    public List<ServerOptions> {|#1:Servers|} { get; set; } = [];
                }

                public sealed class ServerOptions
                {
                    [Required]
                    public string Host { get; set; } = "";
                }
            }
            """;

        var fixedStartupSource = """
            using Microsoft.Extensions.DependencyInjection;
            using MyApp;

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
            """;

        var fixedOptionsSource = """
            namespace MyApp
            {
                using System;
                using System.Collections.Generic;
                using System.ComponentModel.DataAnnotations;

                public sealed class ValidateEnumeratedItemsAttribute : Attribute
                {
                }

                public sealed class AppOptions
                {
                    [global::Microsoft.Extensions.Options.ValidateEnumeratedItemsAttribute]
                    public List<ServerOptions> Servers { get; set; } = [];
                }

                public sealed class ServerOptions
                {
                    [Required]
                    public string Host { get; set; } = "";
                }
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.NestedValidationNotRecursive)
            .WithLocation(0)
            .WithLocation(1)
            .WithArguments("AppOptions", "Servers");

        await Verifier.VerifyCodeFixAsync(
            [
                ("Startup.cs", startupSource),
                ("Options.cs", optionsSource)
            ],
            [
                ("Startup.cs", fixedStartupSource),
                ("Options.cs", fixedOptionsSource)
            ],
            expected);
    }
}
