using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ConfigContraband.Core.Tests;

public sealed class OptionsValidatorSchemaTests
{
    [Fact]
    public void Named_bind_is_validated_because_ivalidate_options_applies_to_every_name()
    {
        var section = Assert.Single(Extract("""
            services.AddOptions<StripeOptions>("tenant")
                .BindConfiguration("Stripe");
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """));

        Assert.Equal("Stripe", section.SectionPath);
        Assert.True(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Bind_get_section_is_validated_when_options_validator_is_registered()
    {
        var section = Assert.Single(Extract("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetSection("Stripe"));
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Configuration;"));

        Assert.True(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Bind_get_required_section_is_validated_when_options_validator_is_registered()
    {
        var section = Assert.Single(Extract("""
            IConfiguration configuration = null!;
            services.AddOptions<StripeOptions>()
                .Bind(configuration.GetRequiredSection("Stripe"));
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Configuration;"));

        Assert.True(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Direct_configure_named_options_are_validated_when_options_validator_is_registered()
    {
        var section = Assert.Single(Extract("""
            IConfiguration configuration = null!;
            services.Configure<StripeOptions>("tenant", configuration.GetSection("Stripe"));
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """, extraUsings: "using Microsoft.Extensions.Configuration;"));

        Assert.True(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Expression_bodied_binding_is_validated_when_options_validator_is_chained()
    {
        var sections = ExtractCompilation(
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static OptionsBuilder<StripeOptions> Add(IServiceCollection services) =>
                    services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>()
                        .AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe");
            }
            """,
            includeDefaultValidators: true);

        var section = Assert.Single(sections);
        Assert.True(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Field_initializer_binding_is_validated_when_options_validator_is_chained()
    {
        var sections = ExtractCompilation(
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                private static readonly IServiceCollection Services = new ServiceCollection();

                public static readonly OptionsBuilder<StripeOptions> Builder =
                    Services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>()
                        .AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe");
            }
            """,
            includeDefaultValidators: true);

        var section = Assert.Single(sections);
        Assert.True(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Nested_validator_class_validates_the_bound_section()
    {
        var sections = ExtractCompilation(
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe");
                    services.AddSingleton<IValidateOptions<StripeOptions>, Validators.ValidateStripeOptions>();
                }
            }

            public static class Validators
            {
                [OptionsValidator]
                public sealed class ValidateStripeOptions : IValidateOptions<StripeOptions>
                {
                    public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
                }
            }
            """);

        Assert.True(Assert.Single(sections).ValidatesDataAnnotations);
    }

    [Fact]
    public void Inherited_ivalidate_options_with_attribute_on_derived_type_validates()
    {
        var sections = ExtractCompilation(
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe");
                    services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
                }
            }

            public abstract class ValidateStripeOptionsBase : IValidateOptions<StripeOptions>
            {
                public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
            }

            [OptionsValidator]
            public sealed class ValidateStripeOptions : ValidateStripeOptionsBase
            {
            }
            """);

        Assert.True(Assert.Single(sections).ValidatesDataAnnotations);
    }

    [Fact]
    public void Constructed_generic_options_validator_validates()
    {
        var sections = ExtractCompilation(
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe");
                    services.AddSingleton<IValidateOptions<StripeOptions>, ValidateOptions<StripeOptions>>();
                }
            }

            [OptionsValidator]
            public sealed class ValidateOptions<T> : IValidateOptions<T> where T : class
            {
                public ValidateOptionsResult Validate(string? name, T options) => ValidateOptionsResult.Success;
            }
            """);

        Assert.True(Assert.Single(sections).ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_when_options_validator_is_on_a_different_service_collection()
    {
        var section = Assert.Single(Extract("""
            IServiceCollection other = new ServiceCollection();
            other.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            """));

        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_by_a_logical_and_registration()
    {
        var section = Assert.Single(Extract("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            _ = true && services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>() is not null;
            """));

        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_when_options_validator_is_unregistered()
    {
        var section = Assert.Single(Extract("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            """));

        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_for_a_different_options_type()
    {
        var section = Assert.Single(Extract("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            services.AddSingleton<IValidateOptions<BillingOptions>, ValidateBillingOptions>();
            """));

        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_for_factory_add_singleton()
    {
        var section = Assert.Single(Extract("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            services.AddSingleton<IValidateOptions<StripeOptions>>(_ => new ValidateStripeOptions());
            """));

        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_for_add_transient()
    {
        var section = Assert.Single(Extract("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            services.AddTransient<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """));

        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_for_try_add_enumerable_transient()
    {
        var section = Assert.Single(Extract("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            services.TryAddEnumerable(ServiceDescriptor.Transient<IValidateOptions<StripeOptions>, ValidateStripeOptions>());
            """));

        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_for_try_add_enumerable_local_descriptor()
    {
        var section = Assert.Single(Extract("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            var descriptor = ServiceDescriptor.Singleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            services.TryAddEnumerable(descriptor);
            """));

        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_by_a_nested_local_function_registration()
    {
        var section = Assert.Single(Extract("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            void Register()
            {
                services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            }
            Register();
            """));

        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_by_a_conditional_if_registration()
    {
        var section = Assert.Single(Extract("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            if (true)
            {
                services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            }
            """));

        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_by_a_switch_expression_registration()
    {
        var section = Assert.Single(Extract("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            _ = true switch
            {
                true => services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>(),
                _ => services
            };
            """));

        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_by_a_ternary_registration()
    {
        var section = Assert.Single(Extract("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            _ = true
                ? services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>()
                : services;
            """));

        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_by_a_coalesced_registration()
    {
        var section = Assert.Single(Extract("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            _ = null ?? services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """));

        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_when_attribute_is_only_on_the_base_type()
    {
        var sections = ExtractCompilation(
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe");
                    services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
                }
            }

            [OptionsValidator]
            public abstract class ValidateStripeOptionsBase : IValidateOptions<StripeOptions>
            {
                public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
            }

            public sealed class ValidateStripeOptions : ValidateStripeOptionsBase
            {
            }
            """);

        Assert.False(Assert.Single(sections).ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_for_lookalike_options_validator_attribute()
    {
        var sections = ExtractCompilation(
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe");
                    services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
                }
            }

            [Contoso.OptionsValidator]
            public sealed class ValidateStripeOptions : IValidateOptions<StripeOptions>
            {
                public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
            }

            namespace Contoso
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class OptionsValidatorAttribute : System.Attribute
                {
                }
            }
            """);

        Assert.False(Assert.Single(sections).ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_validated_for_static_unreduced_add_singleton()
    {
        var section = Assert.Single(Extract("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            ServiceCollectionServiceExtensions.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>(services);
            """));

        Assert.True(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_by_a_static_add_singleton_on_a_different_service_collection()
    {
        var section = Assert.Single(Extract("""
            IServiceCollection other = new ServiceCollection();
            ServiceCollectionServiceExtensions.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>(other);
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            """));

        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_validated_for_parenthesized_and_null_forgiving_receivers()
    {
        var section = Assert.Single(Extract("""
            (services).AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            services!.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """));

        Assert.True(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_by_a_switch_statement_registration()
    {
        var section = Assert.Single(Extract("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe");
            switch (true)
            {
                case true:
                    services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
                    break;
            }
            """));

        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_by_a_user_defined_descriptor_conversion()
    {
        var sections = ExtractCompilation(
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe");
                    services.TryAddEnumerable(new DescriptorBox(ServiceDescriptor.Singleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>()));
                }
            }

            public readonly struct DescriptorBox
            {
                private readonly ServiceDescriptor _descriptor;

                public DescriptorBox(ServiceDescriptor descriptor)
                {
                    _descriptor = descriptor;
                }

                public static implicit operator ServiceDescriptor(DescriptorBox box) => box._descriptor;
            }
            """,
            includeDefaultValidators: true);

        Assert.False(Assert.Single(sections).ValidatesDataAnnotations);
    }

    private static IReadOnlyList<SchemaSection> Extract(string configureBody, string extraUsings = "")
    {
        return ExtractCompilation(
            $$"""
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;
            using Microsoft.Extensions.Options;
            {{extraUsings}}

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    {{configureBody}}
                }
            }
            """,
            includeDefaultValidators: true);
    }

    private static IReadOnlyList<SchemaSection> ExtractCompilation(
        string startupSource,
        bool includeDefaultValidators = false)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        var types = """
            public sealed class StripeOptions
            {
                public string ApiKey { get; set; } = "";
            }

            public sealed class BillingOptions
            {
                public string ConnectionString { get; set; } = "";
            }
            """;
        if (includeDefaultValidators)
        {
            types += """

                [Microsoft.Extensions.Options.OptionsValidator]
                public sealed class ValidateStripeOptions : Microsoft.Extensions.Options.IValidateOptions<StripeOptions>
                {
                    public Microsoft.Extensions.Options.ValidateOptionsResult Validate(string? name, StripeOptions options) =>
                        Microsoft.Extensions.Options.ValidateOptionsResult.Success;
                }

                [Microsoft.Extensions.Options.OptionsValidator]
                public sealed class ValidateBillingOptions : Microsoft.Extensions.Options.IValidateOptions<BillingOptions>
                {
                    public Microsoft.Extensions.Options.ValidateOptionsResult Validate(string? name, BillingOptions options) =>
                        Microsoft.Extensions.Options.ValidateOptionsResult.Success;
                }
                """;
        }

        var compilation = CSharpCompilation.Create(
            "OptionsValidatorSchemaTests",
            [
                CSharpSyntaxTree.ParseText(startupSource),
                CSharpSyntaxTree.ParseText(types),
            ],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.DoesNotContain(
            compilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        return RegistrationExtractor.ExtractAll(compilation);
    }
}
