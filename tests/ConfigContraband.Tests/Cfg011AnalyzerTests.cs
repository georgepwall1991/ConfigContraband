using ConfigContraband.Tests.Infrastructure;

namespace ConfigContraband.Tests;

public sealed partial class ConfigContrabandAnalyzerTests
{
    private const string InvalidSyntaxMessage = "the JSON syntax is invalid";
    private const string NonObjectRootMessage = "the top-level JSON element must be an object";

    [Fact]
    public async Task Cfg011_reports_unterminated_object()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationFileLoadFailure)
            .WithSpan("appsettings.json", 1, 1, 1, 2)
            .WithArguments(InvalidSyntaxMessage);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": 80
              }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg011_reports_missing_colon()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationFileLoadFailure)
            .WithSpan("appsettings.json", 1, 12, 1, 13)
            .WithArguments(InvalidSyntaxMessage);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Server" "x" }"""),
            expected);
    }

    [Fact]
    public async Task Cfg011_reports_missing_comma_between_members()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationFileLoadFailure)
            .WithSpan("appsettings.json", 1, 15, 1, 16)
            .WithArguments(InvalidSyntaxMessage);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Server": 1 "Other": 2 }"""),
            expected);
    }

    [Fact]
    public async Task Cfg011_reports_unquoted_object_key()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationFileLoadFailure)
            .WithSpan("appsettings.json", 1, 3, 1, 4)
            .WithArguments(InvalidSyntaxMessage);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ unquoted: true }"""),
            expected);
    }

    [Theory]
    [InlineData("tru")]
    [InlineData("TRUE")]
    [InlineData("Null")]
    [InlineData("080")]
    [InlineData("+1")]
    [InlineData(".5")]
    [InlineData("1.")]
    [InlineData("1e")]
    [InlineData("1x")]
    [InlineData("NaN")]
    [InlineData("0x10")]
    public async Task Cfg011_reports_scalar_tokens_the_runtime_reader_rejects(string token)
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationFileLoadFailure)
            .WithSpan("appsettings.json", 1, 13, 1, 14)
            .WithArguments(InvalidSyntaxMessage);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", $$"""{ "Server": {{token}} }"""),
            expected);
    }

    [Fact]
    public async Task Cfg011_reports_invalid_string_escape()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationFileLoadFailure)
            .WithSpan("appsettings.json", 1, 15, 1, 16)
            .WithArguments(InvalidSyntaxMessage);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Server": "a\qb" }"""),
            expected);
    }

    [Fact]
    public async Task Cfg011_reports_unterminated_string()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationFileLoadFailure)
            .WithSpan("appsettings.json", 1, 13, 1, 14)
            .WithArguments(InvalidSyntaxMessage);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Server": "abc"""),
            expected);
    }

    [Fact]
    public async Task Cfg011_reports_unterminated_block_comment_after_root()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationFileLoadFailure)
            .WithSpan("appsettings.json", 1, 29, 1, 30)
            .WithArguments(InvalidSyntaxMessage);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Server": {"Value": 80} } /*"""),
            expected);
    }

    [Fact]
    public async Task Cfg011_reports_line_separator_inside_line_comment()
    {
        // U+2028 counts as a line break in SourceText's line model: the rejected char is the last
        // of "line 1", so the one-character span reports as (1,33)-(2,1).
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationFileLoadFailure)
            .WithSpan("appsettings.json", 1, 33, 2, 1)
            .WithArguments(InvalidSyntaxMessage);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", "{ \"Server\": {\"Value\": 80} } // x\u2028"),
            expected);
    }

    [Fact]
    public async Task Cfg011_reports_unterminated_array()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationFileLoadFailure)
            .WithSpan("appsettings.json", 1, 13, 1, 14)
            .WithArguments(InvalidSyntaxMessage);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Server": [1, 2"""),
            expected);
    }

    [Fact]
    public async Task Cfg011_reports_beyond_maximum_json_depth()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));
        var json = "{ \"Deep\": {" +
            string.Concat(Enumerable.Repeat("\"Nested\":{", 63)) +
            "\"Value\":1" +
            new string('}', 64) + " }";

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationFileLoadFailure)
            .WithSpan("appsettings.json", 1, 641, 1, 642)
            .WithArguments("the maximum JSON depth of 64 is exceeded");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", json),
            expected);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("5")]
    [InlineData("-1.5e2")]
    [InlineData("\"text\"")]
    [InlineData("true")]
    [InlineData("null")]
    public async Task Cfg011_reports_non_object_roots(string root)
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationFileLoadFailure)
            .WithSpan("appsettings.json", 1, 1, 1, 2)
            .WithArguments(NonObjectRootMessage);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", root),
            expected);
    }

    [Theory]
    [InlineData("", 1, 1)]
    [InlineData("   ", 4, 4)]
    [InlineData("// nothing but a comment", 25, 25)]
    [InlineData("garbage", 1, 2)]
    public async Task Cfg011_reports_files_without_a_root_value(string content, int startColumn, int endColumn)
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationFileLoadFailure)
            .WithSpan("appsettings.json", 1, startColumn, 1, endColumn)
            .WithArguments(InvalidSyntaxMessage);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", content),
            expected);
    }

    [Fact]
    public async Task Cfg011_reports_content_after_the_root_object()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationFileLoadFailure)
            .WithSpan("appsettings.json", 1, 17, 1, 18)
            .WithArguments(InvalidSyntaxMessage);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Server": 1 } { "Other": 2 }"""),
            expected);
    }

    [Fact]
    public async Task Cfg011_reports_duplicate_flattened_scalar_path_at_second_key()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationFileLoadFailure)
            .WithSpan("appsettings.json", 5, 3, 5, 17)
            .WithArguments("the configuration key \"server:value\" is duplicated");

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": 80
              },
              "server:value": 443
            }
            """),
            expected);
    }

    [Fact]
    public async Task Cfg011_reports_each_rejected_file_once()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expectedFirst = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationFileLoadFailure)
            .WithSpan("appsettings.json", 1, 13, 1, 14)
            .WithArguments(InvalidSyntaxMessage);
        var expectedSecond = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationFileLoadFailure)
            .WithSpan("appsettings.Production.json", 1, 13, 1, 14)
            .WithArguments(InvalidSyntaxMessage);

        await Verifier.VerifyAnalyzerAsync(
            source,
            [
                ("appsettings.json", """{ "Server": tru }"""),
                ("appsettings.Production.json", """{ "Server": 080 }""")
            ],
            expectedFirst,
            expectedSecond);
    }

    [Fact]
    public async Task Cfg011_rejected_file_is_excluded_from_section_analysis()
    {
        // A rejected file contributes no sections at all, matching the runtime: the provider throws
        // before any of its contents reach configuration. Section-dependent rules stay quiet the
        // same way they do when no appsettings file is visible; CFG011 carries the signal alone.
        var source = OptionsSource("""
            services.AddOptions<StripeOptions>()
                .BindConfiguration("Stripe")
                .ValidateDataAnnotations()
                .ValidateOnStart();
            """);

        var expectedFile = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationFileLoadFailure)
            .WithSpan("appsettings.json", 1, 13, 1, 14)
            .WithArguments(InvalidSyntaxMessage);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """{ "Stripe": { "ApiKey": "secret" """),
            expectedFile);
    }

    [Fact]
    public async Task Cfg011_stays_quiet_for_comments_and_trailing_commas()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              // tolerated comment
              "Server": {
                "Value": 80, /* another tolerated comment */
              },
            }
            """));
    }

    [Fact]
    public async Task Cfg011_stays_quiet_for_comment_between_scalar_and_delimiter()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": 80 /* the runtime reader skips comments here */
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg011_stays_quiet_for_merging_duplicate_object_members()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": 80
              },
              "Merged": {
                "A": 1
              },
              "Merged": {
                "B": 2
              }
            }
            """));
    }

    [Fact]
    public async Task Cfg011_stays_quiet_for_scalar_overwritten_by_empty_container()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": 80
              },
              "server:value": {}
            }
            """));
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("1e3")]
    [InlineData("1E+3")]
    [InlineData("0")]
    [InlineData("-0.5E-2")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("null")]
    [InlineData("\"\"")]
    public async Task Cfg011_stays_quiet_for_valid_scalars(string token)
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("string"));

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", $$"""{ "Server": {{token}} }"""));
    }

    [Fact]
    public async Task Cfg011_stays_quiet_for_maximum_depth_boundary()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));
        var json = "{ \"Server\": {\"Value\":80}, \"Deep\": {" +
            string.Concat(Enumerable.Repeat("\"Nested\":{", 61)) +
            "\"Value\":1" +
            new string('}', 62) + " }";

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", json));
    }

    [Fact]
    public async Task Cfg011_reports_byte_order_mark_inside_file_text()
    {
        // A physical single-BOM file is stripped by Roslyn's decode before the analyzer sees it
        // (covered by the runtime-parity matrix). A literal U+FEFF reaching the parser is the
        // second BOM of a double-BOM file, which the provider rejects.
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        var expected = Verifier.Diagnostic(DiagnosticDescriptors.ConfigurationFileLoadFailure)
            .WithSpan("appsettings.json", 1, 1, 1, 2)
            .WithArguments(InvalidSyntaxMessage);

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", "\uFEFF{ \"Server\": 80 }"),
            expected);
    }

    [Fact]
    public async Task Cfg011_stays_quiet_for_environment_appsettings_file_when_valid()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        await Verifier.VerifyAnalyzerAsync(
            source,
            [
                ("appsettings.json", """
                    {
                      "Server": {
                        "Value": 80
                      }
                    }
                    """),
                ("appsettings.Production.json", """
                    {
                      "Server": {
                        "Value": 443
                      }
                    }
                    """)
            ]);
    }

    [Fact]
    public async Task Cfg011_stays_quiet_for_non_appsettings_files()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        await Verifier.VerifyAnalyzerAsync(
            source,
            [
                ("other.json", """{ "Server": tru }"""),
                ("appsettings.json", """
                {
                  "Server": {
                    "Value": 80
                  }
                }
                """)
            ]);
    }

    [Fact]
    public async Task Cfg011_honors_analyzer_config_suppression()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        await Verifier.VerifyAnalyzerWithAnalyzerConfigAsync(
            source,
            ("appsettings.json", """{ "Server": tru }"""),
            """
            is_global = true
            dotnet_diagnostic.CFG011.severity = none
            """);
    }

    [Fact]
    public async Task Cfg011_stays_quiet_for_valid_appsettings_files()
    {
        var source = OptionsSource(BindServer, optionsTypes: ServerOptionsOf("int"));

        await Verifier.VerifyAnalyzerAsync(
            source,
            ("appsettings.json", """
            {
              "Server": {
                "Value": 80
              }
            }
            """));
    }
}
