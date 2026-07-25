using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
    public void Discovers_bind_with_get_required_section()
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
                        .Bind(configuration.GetRequiredSection("Stripe"));
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.Equal("Stripe", section.SectionPath);
        Assert.Equal("StripeOptions", section.Type.Name);
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
    public void Does_not_mark_binding_strict_when_assignment_targets_unrelated_binder_options()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe", options =>
                        {
                            var unrelated = new BinderOptions();
                            unrelated.ErrorOnUnknownConfiguration = true;
                        });
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.False(section.Strict);
    }

    [Fact]
    public void Does_not_mark_binding_strict_when_assignment_is_conditional()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services, bool strict)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe", options =>
                        {
                            if (strict)
                            {
                                options.ErrorOnUnknownConfiguration = true;
                            }
                        });
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.False(section.Strict);
    }

    [Fact]
    public void Detects_direct_binder_options_assignments_in_a_block()
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
                        .BindConfiguration("Stripe", options =>
                        {
                            options.ErrorOnUnknownConfiguration = true;
                            options.BindNonPublicProperties = true;
                        });
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.True(section.Strict);
        Assert.True(section.BindsNonPublicProperties);
    }

    [Fact]
    public void Does_not_mark_binding_strict_when_a_conditional_write_follows_a_direct_write()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services, bool strict)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe", options =>
                        {
                            options.ErrorOnUnknownConfiguration = true;
                            if (!strict)
                            {
                                options.ErrorOnUnknownConfiguration = false;
                            }
                        });
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.False(section.Strict);
    }

    [Fact]
    public void Does_not_mark_binding_strict_after_the_callback_parameter_is_reassigned()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe", options =>
                        {
                            options = new BinderOptions();
                            options.ErrorOnUnknownConfiguration = true;
                        });
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.False(section.Strict);
    }

    [Fact]
    public void Does_not_mark_binding_strict_when_the_runtime_options_escape_to_a_helper()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe", options =>
                        {
                            options.ErrorOnUnknownConfiguration = true;
                            Reset(options);
                        });
                }

                private static void Reset(BinderOptions options)
                {
                    options.ErrorOnUnknownConfiguration = false;
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.False(section.Strict);
    }

    [Fact]
    public void Does_not_mark_binding_strict_when_a_converted_runtime_options_reference_escapes()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe", options =>
                        {
                            options.ErrorOnUnknownConfiguration = true;
                            Reset((object)options);
                        });
                }

                private static void Reset(object value)
                {
                    ((BinderOptions)value).ErrorOnUnknownConfiguration = false;
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.False(section.Strict);
    }

    [Fact]
    public void Does_not_mark_binding_strict_when_the_runtime_options_escape_during_local_initialization()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe", options =>
                        {
                            options.ErrorOnUnknownConfiguration = true;
                            var reset = new ResetBinderOptions(options);
                        });
                }
            }

            public sealed class ResetBinderOptions
            {
                public ResetBinderOptions(BinderOptions options)
                {
                    options.ErrorOnUnknownConfiguration = false;
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.False(section.Strict);
    }

    [Fact]
    public void Does_not_mark_binding_strict_when_the_runtime_options_escape_during_local_assignment()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe", options =>
                        {
                            options.ErrorOnUnknownConfiguration = true;
                            BinderOptions sink;
                            sink = Wrap(options);
                        });
                }

                private static BinderOptions Wrap(BinderOptions options)
                {
                    options.ErrorOnUnknownConfiguration = false;
                    return options;
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.False(section.Strict);
    }

    [Fact]
    public void Does_not_mark_binding_strict_when_parameter_reassignment_escapes_the_runtime_options()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe", options =>
                        {
                            options.ErrorOnUnknownConfiguration = true;
                            options = Swap(options);
                        });
                }

                private static BinderOptions Swap(BinderOptions options)
                {
                    options.ErrorOnUnknownConfiguration = false;
                    return new BinderOptions();
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.False(section.Strict);
    }

    [Fact]
    public void Does_not_restore_strict_binding_after_the_runtime_options_escape()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                private static BinderOptions? stored;

                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe", options =>
                        {
                            Register(options);
                            options.ErrorOnUnknownConfiguration = true;
                        });
                }

                private static void Register(BinderOptions options)
                {
                    stored = options;
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.False(section.Strict);
    }

    [Fact]
    public void Does_not_restore_strict_binding_through_an_alias_after_escape()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                private static BinderOptions? stored;

                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe", options =>
                        {
                            var alias = options;
                            Register(alias);
                            alias.ErrorOnUnknownConfiguration = true;
                        });
                }

                private static void Register(BinderOptions options)
                {
                    stored = options;
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.False(section.Strict);
    }

    [Fact]
    public void Does_not_create_a_trusted_alias_after_the_runtime_options_escape()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                private static BinderOptions? stored;

                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe", options =>
                        {
                            Register(options);
                            var alias = options;
                            alias.ErrorOnUnknownConfiguration = true;
                        });
                }

                private static void Register(BinderOptions options)
                {
                    stored = options;
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.False(section.Strict);
    }

    [Fact]
    public void Does_not_trust_an_alias_after_conditional_reassignment()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services, bool replace)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe", options =>
                        {
                            var alias = options;
                            if (replace)
                            {
                                alias = new BinderOptions();
                            }

                            alias.ErrorOnUnknownConfiguration = true;
                        });
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.False(section.Strict);
    }

    [Fact]
    public void Does_not_mark_binding_strict_when_a_deferred_callback_captures_the_runtime_options()
    {
        var sections = Extract(
            """
            using System;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                private static Action? reset;

                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration("Stripe", options =>
                        {
                            options.ErrorOnUnknownConfiguration = true;
                            reset = () => options.ErrorOnUnknownConfiguration = false;
                        });
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.False(section.Strict);
    }

    [Fact]
    public void Does_not_mark_binding_strict_when_a_local_function_captures_the_runtime_options()
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
                        .BindConfiguration("Stripe", options =>
                        {
                            options.ErrorOnUnknownConfiguration = true;

                            void Reset()
                            {
                                options.ErrorOnUnknownConfiguration = false;
                            }

                            Reset();
                        });
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.False(section.Strict);
    }

    [Fact]
    public void Detects_strict_binding_in_an_unconditional_nested_block()
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
                        .BindConfiguration("Stripe", options =>
                        {
                            {
                                options.ErrorOnUnknownConfiguration = true;
                            }
                        });
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.True(section.Strict);
    }

    [Fact]
    public void Detects_strict_binding_through_a_local_alias()
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
                        .BindConfiguration("Stripe", options =>
                        {
                            var alias = options;
                            alias.ErrorOnUnknownConfiguration = true;
                        });
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.True(section.Strict);
    }

    [Fact]
    public void Detects_strict_binding_in_an_anonymous_method_callback()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>()
                        .BindConfiguration(
                            "Stripe",
                            delegate(BinderOptions options)
                            {
                                options.ErrorOnUnknownConfiguration = true;
                            });
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.True(section.Strict);
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

    [Fact]
    public void Named_binding_is_not_validated_by_a_different_named_registration()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddOptions<StripeOptions>("A").ValidateDataAnnotations();
                    services.AddOptions<StripeOptions>("B").BindConfiguration("B");
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.Equal("B", section.SectionPath);
        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_by_a_deferred_registration()
    {
        var sections = Extract(
            """
            using System;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    Action deferred = () =>
                        services.AddOptions<BillingOptions>().ValidateDataAnnotations();

                    services.Configure<BillingOptions>(configuration.GetSection("Billing"));
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_by_a_conditional_registration()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(
                    IServiceCollection services,
                    IConfiguration configuration,
                    bool validate)
                {
                    if (validate)
                    {
                        services.AddOptions<BillingOptions>().ValidateDataAnnotations();
                    }

                    services.Configure<BillingOptions>(configuration.GetSection("Billing"));
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_by_a_conditional_expression_registration()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static void Configure(
                    IServiceCollection services,
                    IConfiguration configuration,
                    bool validate)
                {
                    _ = validate
                        ? services.AddOptions<BillingOptions>().ValidateDataAnnotations()
                        : services.AddOptions<BillingOptions>();

                    services.Configure<BillingOptions>(configuration.GetSection("Billing"));
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Binding_is_not_validated_by_a_custom_same_name_extension()
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
                    services.AddOptions<BillingOptions>().ValidateDataAnnotations(42);
                    services.Configure<BillingOptions>(configuration.GetSection("Billing"));
                }
            }

            public static class CustomValidationExtensions
            {
                public static OptionsBuilder<T> ValidateDataAnnotations<T>(
                    this OptionsBuilder<T> builder,
                    int marker)
                    where T : class
                {
                    return builder;
                }
            }
            """);

        var section = Assert.Single(sections);
        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Top_level_binding_is_validated_by_an_unconditional_registration()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            IServiceCollection services = null!;
            IConfiguration configuration = null!;

            services.AddOptions<BillingOptions>().ValidateDataAnnotations();
            services.Configure<BillingOptions>(configuration.GetSection("Billing"));
            """);

        var section = Assert.Single(sections);
        Assert.True(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Top_level_binding_is_not_validated_by_a_conditional_registration()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            IServiceCollection services = null!;
            IConfiguration configuration = null!;
            bool validate = false;

            if (validate)
            {
                services.AddOptions<BillingOptions>().ValidateDataAnnotations();
            }

            services.Configure<BillingOptions>(configuration.GetSection("Billing"));
            """);

        var section = Assert.Single(sections);
        Assert.False(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Top_level_local_binding_is_validated_by_a_separate_registration()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            IServiceCollection services = null!;

            var builder = services.AddOptions<BillingOptions>().BindConfiguration("Billing");
            services.AddOptions<BillingOptions>().ValidateDataAnnotations();
            """);

        var section = Assert.Single(sections);
        Assert.True(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Expression_bodied_binding_detects_same_chain_data_annotations_validation()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                public static OptionsBuilder<BillingOptions> Add(IServiceCollection services) =>
                    services.AddOptions<BillingOptions>()
                        .BindConfiguration("Billing")
                        .ValidateDataAnnotations();
            }
            """);

        var section = Assert.Single(sections);
        Assert.True(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Field_initializer_binding_detects_same_chain_data_annotations_validation()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;

            public static class Startup
            {
                private static readonly IServiceCollection Services = new ServiceCollection();

                public static readonly OptionsBuilder<BillingOptions> Builder =
                    Services.AddOptions<BillingOptions>()
                        .BindConfiguration("Billing")
                        .ValidateDataAnnotations();
            }
            """);

        var section = Assert.Single(sections);
        Assert.True(section.ValidatesDataAnnotations);
    }

    [Fact]
    public void Ignores_unrelated_configure_helper()
    {
        var sections = Extract(
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public static class Startup
            {
                public static void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    Helper.Configure<BillingOptions>(configuration.GetSection("Billing"));
                }
            }

            public static class Helper
            {
                public static void Configure<T>(IConfiguration config)
                {
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

        var startupTree = CSharpSyntaxTree.ParseText(startupSource);
        var outputKind = startupTree.GetRoot().ChildNodes().OfType<GlobalStatementSyntax>().Any()
            ? OutputKind.ConsoleApplication
            : OutputKind.DynamicallyLinkedLibrary;

        var compilation = CSharpCompilation.Create(
            "RegistrationTests",
            [
                startupTree,
                CSharpSyntaxTree.ParseText(OptionsTypes),
            ],
            references,
            new CSharpCompilationOptions(outputKind));

        Assert.DoesNotContain(
            compilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        return RegistrationExtractor.ExtractAll(compilation);
    }
}
