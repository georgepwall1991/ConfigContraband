using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConfigContraband.Core.Tests;

public sealed class OptionsValidatorRegistrationTests
{
    private const string StripeValidatorTypes =
        """
        public sealed class StripeOptions
        {
            public string ApiKey { get; set; } = "";
        }

        public sealed class BillingOptions
        {
            public string ConnectionString { get; set; } = "";
        }

        [Microsoft.Extensions.Options.OptionsValidator]
        public sealed class ValidateStripeOptions : Microsoft.Extensions.Options.IValidateOptions<StripeOptions>
        {
            public Microsoft.Extensions.Options.ValidateOptionsResult Validate(string? name, StripeOptions options) =>
                Microsoft.Extensions.Options.ValidateOptionsResult.Success;
        }
        """;

    [Theory]
    [InlineData("services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();")]
    [InlineData("services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>());")]
    [InlineData("services.TryAddEnumerable(descriptor: ServiceDescriptor.Singleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>());")]
    [InlineData("services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>().AddOptions<StripeOptions>();")]
    [InlineData("(services).AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();")]
    [InlineData("services!.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();")]
    public void Proves_framework_options_validator_registration(string registration)
    {
        var result = TryProve(registration);

        Assert.True(result.Proved);
        Assert.Equal("StripeOptions", result.OptionsTypeName);
        Assert.True(result.SawServiceCollectionRegistration);
    }

    [Fact]
    public void Proves_partial_options_validator_class()
    {
        var result = TryProve(
            "services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();",
            extraTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; set; } = "";
            }

            [OptionsValidator]
            public partial class ValidateStripeOptions : IValidateOptions<StripeOptions>
            {
                public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
            }
            """);

        Assert.True(result.Proved);
        Assert.Equal("StripeOptions", result.OptionsTypeName);
    }

    [Fact]
    public void Proves_nested_options_validator_class()
    {
        var result = TryProve(
            "services.AddSingleton<IValidateOptions<StripeOptions>, Validators.ValidateStripeOptions>();",
            extraTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; set; } = "";
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

        Assert.True(result.Proved);
        Assert.Equal("StripeOptions", result.OptionsTypeName);
    }

    [Fact]
    public void Proves_validator_that_inherits_ivalidate_options_from_base_type()
    {
        var result = TryProve(
            "services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();",
            extraTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; set; } = "";
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

        Assert.True(result.Proved);
        Assert.Equal("StripeOptions", result.OptionsTypeName);
    }

    [Fact]
    public void Proves_constructed_generic_options_validator()
    {
        var result = TryProve(
            "services.AddSingleton<IValidateOptions<StripeOptions>, ValidateOptions<StripeOptions>>();",
            extraTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; set; } = "";
            }

            [OptionsValidator]
            public sealed class ValidateOptions<T> : IValidateOptions<T> where T : class
            {
                public ValidateOptionsResult Validate(string? name, T options) => ValidateOptionsResult.Success;
            }
            """);

        Assert.True(result.Proved);
        Assert.Equal("StripeOptions", result.OptionsTypeName);
    }

    [Fact]
    public void Proves_validator_that_implements_multiple_ivalidate_options_interfaces()
    {
        var result = TryProve(
            "services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();",
            extraTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; set; } = "";
            }

            public sealed class BillingOptions
            {
                public string ConnectionString { get; set; } = "";
            }

            [OptionsValidator]
            public sealed class ValidateStripeOptions : IValidateOptions<StripeOptions>, IValidateOptions<BillingOptions>
            {
                public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;

                public ValidateOptionsResult Validate(string? name, BillingOptions options) => ValidateOptionsResult.Success;
            }
            """);

        Assert.True(result.Proved);
        Assert.Equal("StripeOptions", result.OptionsTypeName);
    }

    [Fact]
    public void Proves_record_options_type()
    {
        var result = TryProve(
            "services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();",
            extraTypes: """
            public sealed record StripeOptions
            {
                public string ApiKey { get; set; } = "";
            }

            [OptionsValidator]
            public sealed class ValidateStripeOptions : IValidateOptions<StripeOptions>
            {
                public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
            }
            """);

        Assert.True(result.Proved);
        Assert.Equal("StripeOptions", result.OptionsTypeName);
    }

    [Fact]
    public void Proves_fully_qualified_options_validator_attribute()
    {
        var result = TryProve(
            "services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();",
            extraTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; set; } = "";
            }

            [Microsoft.Extensions.Options.OptionsValidator]
            public sealed class ValidateStripeOptions : IValidateOptions<StripeOptions>
            {
                public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
            }
            """);

        Assert.True(result.Proved);
        Assert.Equal("StripeOptions", result.OptionsTypeName);
    }

    [Theory]
    [InlineData("services.AddSingleton<IValidateOptions<StripeOptions>>(_ => new ValidateStripeOptions());")]
    [InlineData("services.AddSingleton<IValidateOptions<StripeOptions>>(new ValidateStripeOptions());")]
    [InlineData("services.AddSingleton(typeof(IValidateOptions<StripeOptions>), typeof(ValidateStripeOptions));")]
    [InlineData("services.AddTransient<IValidateOptions<StripeOptions>, ValidateStripeOptions>();")]
    [InlineData("services.AddScoped<IValidateOptions<StripeOptions>, ValidateStripeOptions>();")]
    [InlineData("services.AddSingleton<ValidateStripeOptions>();")]
    [InlineData("services.TryAddEnumerable(ServiceDescriptor.Transient<IValidateOptions<StripeOptions>, ValidateStripeOptions>());")]
    [InlineData("services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidateOptions<StripeOptions>, ValidateStripeOptions>());")]
    [InlineData("services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<StripeOptions>>(_ => new ValidateStripeOptions()));")]
    [InlineData("services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<StripeOptions>>(new ValidateStripeOptions()));")]
    [InlineData("services.TryAddEnumerable(ServiceDescriptor.Describe(typeof(IValidateOptions<StripeOptions>), typeof(ValidateStripeOptions), ServiceLifetime.Singleton));")]
    [InlineData("""
            var descriptor = ServiceDescriptor.Singleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            services.TryAddEnumerable(descriptor);
            """)]
    public void Does_not_prove_unsigned_or_non_singleton_shapes(string registration)
    {
        var result = TryProve(registration);

        Assert.False(result.Proved);
        Assert.Null(result.OptionsTypeName);
    }

    [Fact]
    public void Proves_the_registered_options_type_not_a_sibling_type()
    {
        var result = TryProve(
            "services.AddSingleton<IValidateOptions<BillingOptions>, ValidateBillingOptions>();",
            extraTypes: StripeValidatorTypes + """

            [Microsoft.Extensions.Options.OptionsValidator]
            public sealed class ValidateBillingOptions : Microsoft.Extensions.Options.IValidateOptions<BillingOptions>
            {
                public Microsoft.Extensions.Options.ValidateOptionsResult Validate(string? name, BillingOptions options) =>
                    Microsoft.Extensions.Options.ValidateOptionsResult.Success;
            }
            """);

        Assert.True(result.Proved);
        Assert.Equal("BillingOptions", result.OptionsTypeName);
    }

    [Fact]
    public void Does_not_prove_handwritten_ivalidate_options_without_attribute()
    {
        var result = TryProve(
            "services.AddSingleton<IValidateOptions<StripeOptions>, AppValidator>();",
            extraTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; set; } = "";
            }

            public sealed class AppValidator : IValidateOptions<StripeOptions>
            {
                public ValidateOptionsResult Validate(string? name, StripeOptions options) => ValidateOptionsResult.Success;
            }
            """);

        Assert.False(result.Proved);
    }

    [Fact]
    public void Does_not_prove_lookalike_options_validator_attribute()
    {
        var result = TryProve(
            "services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();",
            extraUsings: "",
            extraTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; set; } = "";
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

        Assert.False(result.Proved);
    }

    [Fact]
    public void Does_not_prove_options_validator_declared_only_on_base_type()
    {
        var result = TryProve(
            "services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();",
            extraTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; set; } = "";
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

        Assert.False(result.Proved);
    }

    [Fact]
    public void Does_not_prove_custom_add_singleton_extension()
    {
        var result = TryProve(
            "services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>(42);",
            extraTypes: StripeValidatorTypes + """

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ContosoServiceCollectionExtensions
                {
                    public static IServiceCollection AddSingleton<TService, TImplementation>(
                        this IServiceCollection services,
                        int marker)
                        where TService : class
                        where TImplementation : class, TService
                    {
                        return services;
                    }
                }
            }
            """);

        Assert.False(result.Proved);
    }

    [Fact]
    public void Does_not_prove_user_ivalidate_options_interface()
    {
        var result = TryProve(
            "services.AddSingleton<Contoso.IValidateOptions<StripeOptions>, ValidateStripeOptions>();",
            extraTypes: """
            public sealed class StripeOptions
            {
                public string ApiKey { get; set; } = "";
            }

            namespace Contoso
            {
                public interface IValidateOptions<T>
                {
                }
            }

            [OptionsValidator]
            public sealed class ValidateStripeOptions : Contoso.IValidateOptions<StripeOptions>
            {
            }
            """);

        Assert.False(result.Proved);
    }

    [Fact]
    public void Does_not_prove_a_dynamic_invocation()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    dynamic d = services;
                    d.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
                }
            }
            """ + StripeValidatorTypes;

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "OptionsValidatorRegistrationTests",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var invocation = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(node => node.ToString().Contains("AddSingleton", StringComparison.Ordinal));

        Assert.False(OptionsValidatorRegistration.TryGetValidatedOptionsType(invocation, model, out _));
    }

    [Fact]
    public void Does_not_prove_an_implementation_that_does_not_implement_ivalidate_options()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IValidateOptions<StripeOptions>, string>();
                }
            }

            public sealed class StripeOptions
            {
                public string ApiKey { get; set; } = "";
            }
            """;

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "OptionsValidatorRegistrationTests",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();

        Assert.False(OptionsValidatorRegistration.TryGetValidatedOptionsType(invocation, model, out _));
    }

    [Fact]
    public void Proves_try_add_enumerable_when_the_descriptor_is_explicitly_cast()
    {
        var result = TryProve(
            "services.TryAddEnumerable((ServiceDescriptor)ServiceDescriptor.Singleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>());");

        Assert.True(result.Proved);
        Assert.Equal("StripeOptions", result.OptionsTypeName);
    }

    [Fact]
    public void Service_collection_registration_includes_factory_add_singleton_but_does_not_prove_it()
    {
        var result = TryProve("services.AddSingleton<IValidateOptions<StripeOptions>>(_ => new ValidateStripeOptions());");

        Assert.False(result.Proved);
        Assert.True(result.SawServiceCollectionRegistration);
    }

    [Fact]
    public void Service_collection_registration_includes_try_add_enumerable_but_not_add_options()
    {
        var addOptions = TryProve("services.AddOptions<StripeOptions>();");
        var tryAdd = TryProve(
            "services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>());");

        Assert.False(addOptions.SawServiceCollectionRegistration);
        Assert.True(tryAdd.SawServiceCollectionRegistration);
        Assert.True(tryAdd.Proved);
    }

    [Fact]
    public void Service_collection_registration_rejects_add_transient()
    {
        var result = TryProve("services.AddTransient<IValidateOptions<StripeOptions>, ValidateStripeOptions>();");

        Assert.False(result.Proved);
        Assert.False(result.SawServiceCollectionRegistration);
    }

    [Fact]
    public void Same_service_collection_or_unproven_matches_the_bind_receiver()
    {
        var (bind, validator, model) = GetBindAndValidator(
            """
            services.AddOptions<StripeOptions>().BindConfiguration("Stripe");
            services.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            """);

        Assert.True(OptionsValidatorRegistration.SameServiceCollectionOrUnproven(bind, validator, model));
    }

    [Fact]
    public void Same_service_collection_or_unproven_rejects_a_different_local()
    {
        var (bind, validator, model) = GetBindAndValidator(
            """
            IServiceCollection other = new ServiceCollection();
            other.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
            services.AddOptions<StripeOptions>().BindConfiguration("Stripe");
            """);

        Assert.False(OptionsValidatorRegistration.SameServiceCollectionOrUnproven(bind, validator, model));
    }

    [Fact]
    public void Same_service_collection_or_unproven_matches_when_a_receiver_is_a_field()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public sealed class Startup
            {
                private readonly IServiceCollection other = new ServiceCollection();

                public void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>().BindConfiguration("Stripe");
                    other.AddSingleton<IValidateOptions<StripeOptions>, ValidateStripeOptions>();
                }
            }
            """ + StripeValidatorTypes;

        var (bind, validator, model) = GetBindAndValidatorFromSource(source);
        Assert.True(OptionsValidatorRegistration.SameServiceCollectionOrUnproven(bind, validator, model));
    }

    [Fact]
    public void Same_service_collection_or_unproven_matches_a_non_member_access_invocation()
    {
        var source = """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>().BindConfiguration("Stripe");
                    Register();
                }

                static void Register() {}
            }
            """ + StripeValidatorTypes;

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "OptionsValidatorRegistrationTests",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(
            compilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var model = compilation.GetSemanticModel(tree);
        var configure = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Configure");
        var invocations = configure.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        var bind = invocations.Single(invocation =>
            invocation.Expression is MemberAccessExpressionSyntax member &&
            member.Name.Identifier.ValueText == "BindConfiguration");
        var register = invocations.Single(invocation =>
            invocation.Expression is IdentifierNameSyntax identifier &&
            identifier.Identifier.ValueText == "Register");

        Assert.True(OptionsValidatorRegistration.SameServiceCollectionOrUnproven(bind, register, model));
    }

    private static (InvocationExpressionSyntax Bind, InvocationExpressionSyntax Validator, SemanticModel Model) GetBindAndValidator(
        string registration)
    {
        return GetBindAndValidatorFromSource(
            $$"""
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    {{registration}}
                }
            }

            {{StripeValidatorTypes}}
            """);
    }

    private static (InvocationExpressionSyntax Bind, InvocationExpressionSyntax Validator, SemanticModel Model) GetBindAndValidatorFromSource(
        string source)
    {

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "OptionsValidatorRegistrationTests",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(
            compilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var model = compilation.GetSemanticModel(tree);
        var configure = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Configure");
        var invocations = configure.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        var bind = invocations.Single(invocation =>
            invocation.Expression is MemberAccessExpressionSyntax member &&
            member.Name.Identifier.ValueText == "BindConfiguration");
        var validator = invocations.Single(invocation =>
            OptionsValidatorRegistration.TryGetValidatedOptionsType(invocation, model, out _));
        return (bind, validator, model);
    }

    private static ProofResult TryProve(
        string registration,
        string extraUsings = "",
        string extraTypes = StripeValidatorTypes)
    {
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;
            using Microsoft.Extensions.Options;
            {{extraUsings}}

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    {{registration}}
                }
            }

            {{extraTypes}}
            """;

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "OptionsValidatorRegistrationTests",
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.DoesNotContain(
            compilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var model = compilation.GetSemanticModel(tree);
        var configure = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Configure");

        var provedName = default(string);
        var sawServiceCollectionRegistration = false;
        foreach (var invocation in configure.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method &&
                OptionsValidatorRegistration.IsServiceCollectionRegistration(method))
            {
                sawServiceCollectionRegistration = true;
            }

            if (OptionsValidatorRegistration.TryGetValidatedOptionsType(invocation, model, out var optionsType))
            {
                provedName = optionsType.Name;
            }
        }

        return new ProofResult(provedName is not null, provedName, sawServiceCollectionRegistration);
    }

    private sealed record ProofResult(bool Proved, string? OptionsTypeName, bool SawServiceCollectionRegistration);
}
