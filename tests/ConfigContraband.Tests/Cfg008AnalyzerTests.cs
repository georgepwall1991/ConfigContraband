using ConfigContraband.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{

    [Theory]
    [InlineData("int", "\"eighty\"", 22)]
    [InlineData("long", "\"x\"", 17)]
    [InlineData("double", "\"abc\"", 19)]
    [InlineData("decimal", "\"abc\"", 19)]
    [InlineData("System.Guid", "\"not-a-guid\"", 26)]
    [InlineData("System.TimeSpan", "\"banana\"", 22)]
    [InlineData("System.DateTime", "\"not-a-date\"", 26)]
    public async Task Cfg008_reports_value_that_cannot_convert_to_target_type(string type, string jsonValue, int endColumn)
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf(type));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, endColumn)
            .WithArguments("Server:Value", TypeDisplay(type));

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", $$"""
            {
              "Server": {
                "Value": {{jsonValue}}
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_json_bool_value_bound_to_integer()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 18)
            .WithArguments("Server:Value", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": true
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_unknown_enum_member()
    {
        var source = OptionsSource(
            BindServer,
            optionsTypes: """
            public enum Level { Low, High }

            public sealed class ServerOptions
            {
                public Level Value { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 20)
            .WithArguments("Server:Value", "Level");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "Loud"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_non_bool_string_bound_to_bool()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("bool"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 19)
            .WithArguments("Server:Value", "bool");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "yes"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_decimal_string_with_thousands_separator()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("decimal"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 21)
            .WithArguments("Server:Value", "decimal");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "1,000"
              }
            }
            """),
            expected);
    }

    [Theory]
    [InlineData("float")]
    [InlineData("double")]
    public async Task Cfg008_reports_floating_point_string_with_thousands_separator(string type)
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf(type));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 21)
            .WithArguments("Server:Value", type);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "1,000"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_numeric_enum_value_outside_underlying_range()
    {
        var source = OptionsSource(BindServer, optionsTypes: """
            public enum ByteColor : byte
            {
                Red,
            }

            public sealed class ServerOptions
            {
                public ByteColor Value { get; set; }
            }
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 19)
            .WithArguments("Server:Value", "ByteColor");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "256"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_empty_string_bound_to_non_nullable_integer()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 16)
            .WithArguments("Server:Value", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": ""
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_multi_char_string_bound_to_char()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("char"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 18)
            .WithArguments("Server:Value", "char");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "ab"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_bad_value_for_nullable_target()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int?"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 22)
            .WithArguments("Server:Value", "int?");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_value_bound_through_bind_get_section()
    {
        var source = OptionsSource(
            """
            IConfiguration configuration = null!;
            services.AddOptions<ServerOptions>()
                .Bind(configuration.GetSection("Server"));
            """,
            extraUsings: "using Microsoft.Extensions.Configuration;\n",
            optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 22)
            .WithArguments("Server:Value", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_value_bound_through_configure_get_section()
    {
        var source = OptionsSource(
            """
            IConfiguration configuration = null!;
            services.Configure<ServerOptions>(configuration.GetSection("Server"));
            """,
            extraUsings: "using Microsoft.Extensions.Configuration;\n",
            optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 14, 3, 22)
            .WithArguments("Server:Value", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "eighty"
              }
            }
            """),
            expected);
    }

    [Theory]
    [InlineData("int", "8080")]
    [InlineData("int", "\"8080\"")]
    [InlineData("int", "\"0x1F\"")]
    [InlineData("int", "\"#FF\"")]
    [InlineData("int", "\"&h1F\"")]
    [InlineData("sbyte", "\"&H7F\"")]
    [InlineData("byte", "\"&hFF\"")]
    [InlineData("short", "\"&h7FFF\"")]
    [InlineData("ushort", "\"&hFFFF\"")]
    [InlineData("uint", "\"&hFFFFFFFF\"")]
    [InlineData("long", "\"&h7FFFFFFFFFFFFFFF\"")]
    [InlineData("ulong", "\"&hFFFFFFFFFFFFFFFF\"")]
    [InlineData("bool", "true")]
    [InlineData("bool", "\"TRUE\"")]
    [InlineData("char", "\"a\"")]
    [InlineData("char", "\"\"")]
    [InlineData("System.DateTime", "\"2020-01-02\"")]
    [InlineData("System.DateTime", "\"\"")]
    [InlineData("System.Guid", "\"d3b07384-d9a0-4c9b-8b5e-000000000000\"")]
    [InlineData("string", "\"anything\"")]
    [InlineData("object", "\"anything\"")]
    [InlineData("int", "null")]
    public async Task Cfg008_does_not_report_convertible_or_skipped_value(string type, string jsonValue)
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf(type));

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", $$"""
            {
              "Server": {
                "Value": {{jsonValue}}
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_does_not_report_numeric_or_comma_list_enum_values()
    {
        var source = OptionsSource(
            BindServer,
            optionsTypes: """
            [System.Flags]
            public enum Access { None = 0, Read = 1, Write = 2 }

            public sealed class ServerOptions
            {
                public Access Value { get; set; }
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "Read, Write"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_does_not_report_scalar_given_to_nested_object_property()
    {
        var source = OptionsSource(
            BindServer,
            optionsTypes: """
            public sealed class Nested { public string Name { get; set; } = ""; }

            public sealed class ServerOptions
            {
                public Nested Value { get; set; } = new();
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_reports_get_value_conversion_failure()
    {
        var source = DirectReadSource("""
            _ = configuration.GetValue<int>("Server:Port");
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 13, 3, 21)
            .WithArguments("Server:Port", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_non_generic_get_value_conversion_failure()
    {
        var source = DirectReadSource("""
            _ = configuration.GetValue(typeof(int), "Server:Port");
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 13, 3, 21)
            .WithArguments("Server:Port", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_static_named_non_generic_get_value_default_overload_conversion_failure()
    {
        var source = DirectReadSource("""
            _ = ConfigurationBinder.GetValue(
                configuration: configuration,
                type: typeof(int),
                key: "Server:Port",
                defaultValue: 8080);
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 13, 3, 21)
            .WithArguments("Server:Port", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_ignores_non_generic_get_value_when_type_is_dynamic()
    {
        var source = DirectReadSource("""
            var type = typeof(int);
            _ = configuration.GetValue(type, "Server:Port");
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_ignores_non_generic_get_value_through_user_defined_type_conversion()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetValue(
                (System.Type)(TypeWrapper)typeof(int),
                "Server:Port");
            """,
            extraTypes: """
            public readonly struct TypeWrapper
            {
                private readonly System.Type _type;

                private TypeWrapper(System.Type type)
                {
                    _type = type;
                }

                public static implicit operator TypeWrapper(System.Type type)
                {
                    RuntimeState.Configuration["Server:Port"] = "8080";
                    return new TypeWrapper(type);
                }

                public static implicit operator System.Type(TypeWrapper wrapper) => wrapper._type;
            }

            public static class RuntimeState
            {
                public static IConfiguration Configuration { get; set; } = null!;
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_ignores_non_generic_get_value_when_default_argument_can_mutate_configuration()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetValue(
                typeof(int),
                "Server:Port",
                SetValidValue(configuration));
            """,
            extraMembers: """
            private static object SetValidValue(IConfiguration configuration)
            {
                configuration["Server:Port"] = "8080";
                return 0;
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_reports_non_generic_get_value_conversion_failure_on_known_section_chain()
    {
        var source = DirectReadSource("""
            _ = configuration.GetSection("Server").GetValue(typeof(int), "Port");
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 13, 3, 21)
            .WithArguments("Server:Port", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_ignores_same_fully_qualified_name_non_generic_get_value_shadow()
    {
        var source = DirectReadSource(
            """
            _ = Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue(
                configuration,
                typeof(int),
                "Server:Port");
            """,
            extraTypes: """
            #pragma warning disable CS0436
            namespace Microsoft.Extensions.Configuration
            {
                public static class ConfigurationBinder
                {
                    public static object? GetValue(
                        this IConfiguration configuration,
                        System.Type type,
                        string key) => null;
                }
            }
            #pragma warning restore CS0436
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_reports_static_named_get_value_default_overload_conversion_failure()
    {
        var source = DirectReadSource("""
            _ = ConfigurationBinder.GetValue<int>(
                configuration: configuration,
                key: "Server:Port",
                defaultValue: 8080);
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 13, 3, 21)
            .WithArguments("Server:Port", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_ignores_get_value_when_default_argument_can_mutate_configuration()
    {
        var source = DirectReadSource(
            """
            _ = configuration.GetValue<int>(
                "Server:Port",
                SetValidValue(configuration));
            """,
            extraMembers: """
            private static int SetValidValue(IConfiguration configuration)
            {
                configuration["Server:Port"] = "8080";
                return 0;
            }
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_ignores_get_value_on_locally_initialized_interface_members()
    {
        var source = """
            using Microsoft.Extensions.Configuration;

            public sealed class Reader
            {
                private readonly IConfiguration _configuration = new ConfigurationBuilder().Build();
                private IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();

                public void Read()
                {
                    _ = _configuration.GetValue<int>("Server:Port");
                    _ = Configuration.GetValue<int>("Server:Port");
                }
            }
            """;

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_ignores_get_value_on_constructor_initialized_interface_member()
    {
        var source = """
            using Microsoft.Extensions.Configuration;

            public sealed class Reader
            {
                private readonly IConfiguration _configuration;

                public Reader()
                {
                    _configuration = new ConfigurationBuilder().Build();
                }

                public void Read()
                {
                    _ = _configuration.GetValue<int>("Server:Port");
                }
            }
            """;

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_ignores_get_value_on_custom_accessor_member()
    {
        var source = """
            using Microsoft.Extensions.Configuration;

            public sealed class Reader
            {
                private IConfiguration Configuration
                {
                    get
                    {
                        return new ConfigurationBuilder().Build();
                    }
                }

                public void Read()
                {
                    _ = Configuration.GetValue<int>("Server:Port");
                }
            }
            """;

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_reports_get_value_conversion_failure_on_null_forgiving_injected_field()
    {
        var source = """
            using Microsoft.Extensions.Configuration;

            public sealed class Reader
            {
                private readonly IConfiguration _configuration = null!;

                public void Read()
                {
                    _ = _configuration.GetValue<int>("Server:Port");
                }
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 13, 3, 21)
            .WithArguments("Server:Port", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_ignores_null_forgiving_field_overwritten_with_local_configuration_in_constructor()
    {
        var source = """
            using Microsoft.Extensions.Configuration;

            public sealed class Reader
            {
                private readonly IConfiguration _configuration = null!;

                public Reader()
                {
                    _configuration = new ConfigurationBuilder().Build();
                }

                public void Read()
                {
                    _ = _configuration.GetValue<int>("Server:Port");
                }
            }
            """;

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_reports_for_null_forgiving_field_when_constructor_only_assigns_shadowing_parameter()
    {
        var source = """
            using Microsoft.Extensions.Configuration;

            public sealed class Reader
            {
                private readonly IConfiguration _configuration = null!;

                public Reader(IConfiguration _configuration)
                {
                    _configuration = new ConfigurationBuilder().Build();
                }

                public void Read()
                {
                    _ = _configuration.GetValue<int>("Server:Port");
                }
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 13, 3, 21)
            .WithArguments("Server:Port", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public void Cfg008_rejects_get_value_from_unsigned_replacement_binder_assembly()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("""
            namespace Microsoft.Extensions.Configuration;

            public interface IConfiguration { }

            public static class ConfigurationBinder
            {
                public static T? GetValue<T>(IConfiguration configuration, string key) => default;
            }
            """);
        var compilation = CSharpCompilation.Create(
            "Microsoft.Extensions.Configuration.Binder",
            [syntaxTree],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var method = compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.Configuration.ConfigurationBinder")!
            .GetMembers("GetValue")
            .OfType<IMethodSymbol>()
            .Single();
        var identityGate = typeof(ConfigContrabandAnalyzer).GetMethod(
            "IsFrameworkGenericGetValueMethod",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var accepted = (bool)identityGate.Invoke(null, [method, method])!;

        Assert.False(accepted);
    }

    [Fact]
    public async Task Cfg008_ignores_cross_file_locally_initialized_interface_member_without_ad0001()
    {
        var sources = new[]
        {
            ("Reader.cs", """
                using Microsoft.Extensions.Configuration;

                public sealed partial class Reader
                {
                    public void Read()
                    {
                        _ = Configuration.GetValue<int>("Server:Port");
                    }
                }
                """),
            ("Reader.Configuration.cs", """
                using Microsoft.Extensions.Configuration;

                public sealed partial class Reader
                {
                    private IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();
                }
                """)
        };

        await Verifier.VerifyAnalyzerAsync(
            sources,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_reports_get_value_conversion_failure_on_known_section_chain()
    {
        var source = DirectReadSource("""
            _ = configuration.GetSection("Server").GetValue<int>("Port");
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 13, 3, 21)
            .WithArguments("Server:Port", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_get_value_conversion_failure_once_for_repeated_read()
    {
        var source = DirectReadSource("""
            _ = configuration.GetValue<int>("Server:Port");
            _ = configuration.GetValue<int>("Server:Port");
            """);

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 13, 3, 21)
            .WithArguments("Server:Port", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_reports_once_across_options_binding_and_direct_get_value_read()
    {
        var source = """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            public sealed class Startup
            {
                public void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    services.AddOptions<ServerOptions>().BindConfiguration("Server");
                    _ = configuration.GetValue<int>("Server:Port");
                }
            }

            public sealed class ServerOptions
            {
                public int Port { get; set; }
            }
            """;

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationValueTypeMismatch)
            .WithSpan("appsettings.json", 3, 13, 3, 21)
            .WithArguments("Server:Port", "int");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg008_ignores_same_fully_qualified_name_get_value_shadow()
    {
        var source = DirectReadSource(
            """
            _ = Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue<int>(
                configuration,
                "Server:Port");
            """,
            extraTypes: """
            #pragma warning disable CS0436
            namespace Microsoft.Extensions.Configuration
            {
                public static class ConfigurationBinder
                {
                    public static int GetValue<T>(this IConfiguration configuration, string key) => 0;
                }
            }
            #pragma warning restore CS0436
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_ignores_get_value_on_stored_configuration_section()
    {
        var source = DirectReadSource("""
            IConfigurationSection section = configuration.GetSection("Server");
            _ = section.GetValue<int>("Port");
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Port": "eighty"
            }
            """));
    }

    [Fact]
    public async Task Cfg008_ignores_safe_or_unprovable_get_value_reads()
    {
        var source = DirectReadSource("""
            _ = configuration.GetValue<int>("Server:Port");
            _ = configuration.GetValue<int>("Missing:Port");
            _ = configuration.GetValue<int>("Server:NullPort");
            _ = configuration.GetValue<ServerOptions>("Server");
            var key = "Server:BadPort";
            _ = configuration.GetValue<int>(key);
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": 8080,
                "NullPort": null,
                "BadPort": "eighty"
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg008_ignores_get_value_on_local_or_mutated_configuration()
    {
        var source = DirectReadSource("""
            var local = new ConfigurationBuilder().Build();
            _ = local.GetValue<int>("Server:Port");

            configuration["Server:Port"] = "8080";
            _ = configuration.GetValue<int>("Server:Port");
            """);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Port": "eighty"
              }
            }
            """));
    }
}
