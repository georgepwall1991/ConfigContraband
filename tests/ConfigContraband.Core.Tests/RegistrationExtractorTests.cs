using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ConfigContraband.Core.Tests;

public sealed class RegistrationExtractorTests
{
    private const string OptionsTypes =
        """
        public sealed class StripeOptions
        {
            public string ApiKey { get; set; } = "";
        }

        public sealed class BillingOptions
        {
            public int RetryCount { get; set; }
        }
        """;

    [Fact]
    public void Discovers_bind_configuration_with_literal_section()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe")
                        .ValidateDataAnnotations()
                        .ValidateOnStart();
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.Equal("Stripe", section.SectionPath);
        Assert.Equal("StripeOptions", section.Type.Name);
        Assert.False(section.Strict);
        Assert.False(section.BindsNonPublicProperties);
        Assert.True(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Discovers_bind_with_nested_get_section_chain()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    services.AddOptions<StripeOptions>()
                        .Bind(configuration.GetSection("Features").GetSection("Stripe"));
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.Equal("Features:Stripe", section.SectionPath);
        Assert.Equal("StripeOptions", section.Type.Name);
        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Discovers_direct_configure_with_get_section()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public static class Startup
            {
                public static void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    services.Configure<BillingOptions>(configuration.GetSection("Billing"));
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.Equal("Billing", section.SectionPath);
        Assert.Equal("BillingOptions", section.Type.Name);
        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Direct_configure_is_validated_when_options_are_separately_validated()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    services.Configure<BillingOptions>(configuration.GetSection("Billing"));
                    services.AddOptions<BillingOptions>().ValidateDataAnnotations();
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.Equal("Billing", section.SectionPath);
        Assert.True(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Detects_strict_error_on_unknown_configuration()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe", options => options.ErrorOnUnknownConfiguration = true);
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.True(section.Strict);
    }

    [Fact]
    public void Detects_bind_non_public_properties()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    services.AddOptions<StripeOptions>()
                        .Bind(configuration.GetSection("Stripe"), binder => binder.BindNonPublicProperties = true);
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.True(section.BindsNonPublicProperties);
        Assert.False(section.Strict);
    }

    [Fact]
    public void Ignores_non_literal_section_names()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services, string sectionName)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration(sectionName);
                }
            }
            """);

        Assert.Empty(sections);
    }

    [Fact]
    public void Discovers_configure_with_get_required_section()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public static class Startup
            {
                public static void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    services.Configure<BillingOptions>(configuration.GetRequiredSection("Billing"));
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.Equal("Billing", section.SectionPath);
    }

    [Fact]
    public void Ignores_configure_with_action_delegate()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.DependencyInjection;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.Configure<BillingOptions>(options => options.RetryCount = 3);
                }
            }
            """);

        Assert.Empty(sections);
    }

    [Fact]
    public void Ignores_empty_section_names()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>().BindConfiguration("");
                }
            }
            """);

        Assert.Empty(sections);
    }

    [Fact]
    public void Ignores_bind_that_is_not_on_an_options_builder()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public static class Startup
            {
                public static void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    configuration.Bind(new StripeOptions());
                }
            }
            """);

        Assert.Empty(sections);
    }

    [Fact]
    public void Ignores_get_section_with_non_literal_name()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public static class Startup
            {
                public static void Configure(IServiceCollection services, IConfiguration configuration, string name)
                {
                    services.Configure<BillingOptions>(configuration.GetSection(name));
                }
            }
            """);

        Assert.Empty(sections);
    }

    [Fact]
    public void Ignores_bind_of_whole_configuration_root()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    services.AddOptions<StripeOptions>().Bind(configuration);
                }
            }
            """);

        Assert.Empty(sections);
    }

    private static IReadOnlyList<SchemaSection> Extract(string startupSource)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            "RegistrationTests",
            [
                CSharpSyntaxTree.ParseText(startupSource),
                CSharpSyntaxTree.ParseText(OptionsTypes),
            ],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return RegistrationExtractor.ExtractAll(compilation);
    }
}
