namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{

    private const string ServerValueOptions = """
        public sealed class ServerOptions
        {
            public TYPE Value { get; set; }
        }
        """;

    private static string ServerOptionsOf(string type) => ServerValueOptions.Replace("TYPE", type);

    private static string BindServer =>
        """
        services.AddOptions<ServerOptions>()
            .BindConfiguration("Server");
        """;

    private static (string filename, string content) StripeAppSettings =>
        ("appsettings.json", """
        {
          "Stripe": {
            "ApiKey": "secret"
          }
        }
        """);

    private static (string filename, string content) DatabaseConnectionAppSettings =>
        ("appsettings.json", """
        {
          "ConnectionStrings": {
            "Database": "Server=localhost"
          }
        }
        """);

    private static string DirectReadSource(string body, string extraMembers = "", string extraTypes = "")
    {
        return $$"""
            using Microsoft.Extensions.Configuration;

            public sealed class ServerOptions
            {
                public string Host { get; set; } = "";
                public int Port { get; set; }
            }

            public sealed class Reader
            {
                public void Read(IConfiguration configuration)
                {
                    {{body}}
                }

                {{extraMembers}}
            }

            {{extraTypes}}
            """;
    }

    private struct RuntimeDefaultStructOptions
    {
        public RuntimeDefaultStructOptions() { }

        [System.ComponentModel.DataAnnotations.Required]
        public string ConnectionString { get; set; } = "ok";
    }

    private static string TypeDisplay(string type) => type;

    private static string OptionsSource(
        string registration,
        string extraUsings = "",
        string extraMembers = "",
        string? optionsTypes = null)
    {
        optionsTypes ??= """
            public sealed class StripeOptions
            {
                [Required]
                public string ApiKey { get; set; } = "";

                public string WebhookSecret { get; set; } = "";
            }
            """;

        return $$"""
            using System.ComponentModel.DataAnnotations;
            using Microsoft.Extensions.DependencyInjection;
            {{extraUsings}}

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    {{registration}}
                }

                {{extraMembers}}
            }

            {{optionsTypes}}
            """;
    }
}
