using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ConfigContraband.Core.Tests;

public sealed class DataAnnotationsConstraintsTests
{
    [Fact]
    public void Range_reports_integer_below_minimum()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.Number,
            "0",
            "Range");
    }

    [Fact]
    public void Range_stays_quiet_for_integer_inside_bounds()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.Number,
            "8080");
    }

    [Fact]
    public void Range_reports_integer_above_maximum()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.Number,
            "70000",
            "Range");
    }

    [Fact]
    public void Exclusive_range_reports_boundary_value()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class RateOptions
            {
                [Range(0.0, 1.0, MinimumIsExclusive = true, MaximumIsExclusive = true)]
                public double Ratio { get; set; }
            }
            """,
            "Ratio",
            ScalarKind.Number,
            "0",
            "Range");
    }

    [Fact]
    public void Exclusive_range_stays_quiet_inside_open_interval()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class RateOptions
            {
                [Range(0.0, 1.0, MinimumIsExclusive = true, MaximumIsExclusive = true)]
                public double Ratio { get; set; }
            }
            """,
            "Ratio",
            ScalarKind.Number,
            "0.5");
    }

    [Fact]
    public void Invariant_type_string_range_reports_out_of_range_decimal()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class PriceOptions
            {
                [Range(typeof(decimal), "0.0", "100.0", ParseLimitsInInvariantCulture = true)]
                public decimal Price { get; set; }
            }
            """,
            "Price",
            ScalarKind.Number,
            "-1",
            "Range");
    }

    [Fact]
    public void Culture_dependent_type_string_range_stays_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class PriceOptions
            {
                [Range(typeof(decimal), "0.0", "100.0")]
                public decimal Price { get; set; }
            }
            """,
            "Price",
            ScalarKind.Number,
            "-1");
    }

    [Fact]
    public void MaxLength_reports_overlong_string()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class NameOptions
            {
                [MaxLength(3)]
                public string Code { get; set; } = "";
            }
            """,
            "Code",
            ScalarKind.String,
            "abcd",
            "MaxLength");
    }

    [Fact]
    public void MinLength_reports_short_string()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class NameOptions
            {
                [MinLength(2)]
                public string Code { get; set; } = "";
            }
            """,
            "Code",
            ScalarKind.String,
            "a",
            "MinLength");
    }

    [Fact]
    public void MinLength_accepts_utf16_emoji_that_is_one_code_point()
    {
        // "😀" is one Unicode code point and two UTF-16 code units. DataAnnotations MinLength
        // counts UTF-16 units, so length 2 satisfies [MinLength(2)].
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class NameOptions
            {
                [MinLength(2)]
                public string Code { get; set; } = "";
            }
            """,
            "Code",
            ScalarKind.String,
            "😀");
    }

    [Fact]
    public void StringLength_reports_below_minimum()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class NameOptions
            {
                [StringLength(8, MinimumLength = 3)]
                public string Code { get; set; } = "";
            }
            """,
            "Code",
            ScalarKind.String,
            "ab",
            "StringLength");
    }

    [Fact]
    public void Length_reports_outside_inclusive_bounds()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class NameOptions
            {
                [Length(2, 4)]
                public string Code { get; set; } = "";
            }
            """,
            "Code",
            ScalarKind.String,
            "a",
            "Length");
    }

    [Fact]
    public void AllowedValues_reports_value_not_in_list()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class EnvOptions
            {
                [AllowedValues("dev", "prod")]
                public string Environment { get; set; } = "";
            }
            """,
            "Environment",
            ScalarKind.String,
            "staging",
            "AllowedValues");
    }

    [Fact]
    public void AllowedValues_stays_quiet_for_listed_value()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class EnvOptions
            {
                [AllowedValues("dev", "prod")]
                public string Environment { get; set; } = "";
            }
            """,
            "Environment",
            ScalarKind.String,
            "prod");
    }

    [Fact]
    public void DeniedValues_reports_listed_value()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class EnvOptions
            {
                [DeniedValues("debug")]
                public string Environment { get; set; } = "";
            }
            """,
            "Environment",
            ScalarKind.String,
            "debug",
            "DeniedValues");
    }

    [Fact]
    public void Range_subclass_without_is_valid_override_still_reports()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class PortRangeAttribute : RangeAttribute
            {
                public PortRangeAttribute(int minimum, int maximum) : base(minimum, maximum)
                {
                }
            }

            public sealed class ServerOptions
            {
                [PortRange(1, 65535)]
                public int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.Number,
            "0",
            "Range");
    }

    [Fact]
    public void Parameterless_range_subclass_stays_quiet_when_bounds_are_not_on_the_attribute()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class PortRangeAttribute : RangeAttribute
            {
                public PortRangeAttribute() : base(1, 65535)
                {
                }
            }

            public sealed class ServerOptions
            {
                [PortRange]
                public int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.Number,
            "0");
    }

    [Fact]
    public void Unconvertible_value_stays_quiet_for_cfg008()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.String,
            "eighty");
    }

    [Fact]
    public void Custom_range_subclass_overriding_is_valid_stays_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class WideRangeAttribute : RangeAttribute
            {
                public WideRangeAttribute(int minimum, int maximum) : base(minimum, maximum)
                {
                }

                public override bool IsValid(object? value) => true;
            }

            public sealed class ServerOptions
            {
                [WideRange(1, 10)]
                public int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.Number,
            "0");
    }

    [Fact]
    public void Json_null_stays_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.Null,
            "null");
    }

    [Fact]
    public void Missing_raw_value_stays_quiet()
    {
        var (property, type) = GetProperty(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """,
            "Port");
        Assert.False(DataAnnotationsConstraints.TryGetProvableFailure(property, type, ScalarKind.Number, null, out _));
        Assert.False(DataAnnotationsConstraints.TryGetProvableFailure(property, type, ScalarKind.None, "0", out _));
        Assert.False(DataAnnotationsConstraints.TryGetProvableFailure(null!, type, ScalarKind.Number, "0", out _));
        Assert.False(DataAnnotationsConstraints.TryGetProvableFailure(property, null!, ScalarKind.Number, "0", out _));
    }

    [Fact]
    public void Hex_integer_below_minimum_reports()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.String,
            "0x0",
            "Range");
    }

    [Theory]
    [InlineData("0x10")]
    [InlineData("&h10")]
    [InlineData("#10")]
    public void Hex_integer_inside_bounds_stays_quiet(string rawValue)
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.String,
            rawValue);
    }

    [Fact]
    public void Invalid_hex_stays_quiet_for_cfg008()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [Range(1, 65535)]
                public int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.String,
            "0xGG");
    }

    [Fact]
    public void Range_reports_when_value_overflows_operand_type()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [Range(1, 65535)]
                public long Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.Number,
            "70000",
            "Range");
    }

    [Fact]
    public void Exclusive_range_reports_maximum_boundary()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class RateOptions
            {
                [Range(0.0, 1.0, MinimumIsExclusive = true, MaximumIsExclusive = true)]
                public double Ratio { get; set; }
            }
            """,
            "Ratio",
            ScalarKind.Number,
            "1",
            "Range");
    }

    [Fact]
    public void Nullable_range_reports_out_of_range_value()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [Range(1, 10)]
                public int? Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.Number,
            "0",
            "Range");
    }

    [Fact]
    public void Empty_nullable_stays_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [Range(1, 10)]
                public int? Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.String,
            "");
    }

    [Theory]
    [InlineData("sbyte", "0")]
    [InlineData("byte", "0")]
    [InlineData("short", "0")]
    [InlineData("ushort", "0")]
    [InlineData("uint", "0")]
    [InlineData("ulong", "0")]
    [InlineData("float", "-1")]
    public void Invariant_type_string_range_reports_for_numeric_operand_types(string clrType, string rawValue)
    {
        AssertFailure(
            $$"""
            using System.ComponentModel.DataAnnotations;

            public sealed class NumericOptions
            {
                [Range(typeof({{clrType}}), "1", "10", ParseLimitsInInvariantCulture = true)]
                public {{clrType}} Amount { get; set; }
            }
            """,
            "Amount",
            ScalarKind.Number,
            rawValue,
            "Range");
    }

    [Fact]
    public void Invariant_string_operand_range_reports()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class NameOptions
            {
                [Range(typeof(string), "a", "c", ParseLimitsInInvariantCulture = true)]
                public string Code { get; set; } = "";
            }
            """,
            "Code",
            ScalarKind.String,
            "d",
            "Range");
    }

    [Fact]
    public void Invariant_bool_operand_range_reports()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class FlagOptions
            {
                [Range(typeof(bool), "true", "true", ParseLimitsInInvariantCulture = true)]
                public bool Enabled { get; set; }
            }
            """,
            "Enabled",
            ScalarKind.Bool,
            "false",
            "Range");
    }

    [Fact]
    public void Invariant_char_operand_range_reports()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class NameOptions
            {
                [Range(typeof(char), "a", "z", ParseLimitsInInvariantCulture = true)]
                public string Code { get; set; } = "";
            }
            """,
            "Code",
            ScalarKind.String,
            "0",
            "Range");
    }

    [Fact]
    public void Unsupported_range_operand_type_stays_quiet()
    {
        AssertQuiet(
            """
            using System;
            using System.ComponentModel.DataAnnotations;

            public sealed class IdOptions
            {
                [Range(typeof(Guid), "00000000-0000-0000-0000-000000000000", "ffffffff-ffff-ffff-ffff-ffffffffffff", ParseLimitsInInvariantCulture = true)]
                public string Id { get; set; } = "";
            }
            """,
            "Id",
            ScalarKind.String,
            "not-a-guid-range");
    }

    [Fact]
    public void Length_stays_quiet_inside_inclusive_bounds()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class NameOptions
            {
                [Length(2, 4)]
                public string Code { get; set; } = "";
            }
            """,
            "Code",
            ScalarKind.String,
            "abc");
    }

    [Fact]
    public void Length_reports_above_maximum()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class NameOptions
            {
                [Length(2, 4)]
                public string Code { get; set; } = "";
            }
            """,
            "Code",
            ScalarKind.String,
            "abcde",
            "Length");
    }

    [Fact]
    public void StringLength_reports_above_maximum()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class NameOptions
            {
                [StringLength(3)]
                public string Code { get; set; } = "";
            }
            """,
            "Code",
            ScalarKind.String,
            "abcd",
            "StringLength");
    }

    [Fact]
    public void MaxLength_sentinel_stays_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class NameOptions
            {
                [MaxLength(-1)]
                public string Code { get; set; } = "";
            }
            """,
            "Code",
            ScalarKind.String,
            "abcdefgh");
    }

    [Fact]
    public void DeniedValues_stays_quiet_for_unlisted_value()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class EnvOptions
            {
                [DeniedValues("debug")]
                public string Environment { get; set; } = "";
            }
            """,
            "Environment",
            ScalarKind.String,
            "prod");
    }

    [Fact]
    public void AllowedValues_reports_unlisted_integer()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ModeOptions
            {
                [AllowedValues(1, 2)]
                public int Mode { get; set; }
            }
            """,
            "Mode",
            ScalarKind.Number,
            "3",
            "AllowedValues");
    }

    [Fact]
    public void AllowedValues_reports_unlisted_enum_member()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public enum Color { Red, Blue }

            public sealed class TintOptions
            {
                [AllowedValues(Color.Red)]
                public Color Tint { get; set; }
            }
            """,
            "Tint",
            ScalarKind.String,
            "Blue",
            "AllowedValues");
    }

    [Fact]
    public void AllowedValues_reports_unlisted_bool()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class FlagOptions
            {
                [AllowedValues(true)]
                public bool Enabled { get; set; }
            }
            """,
            "Enabled",
            ScalarKind.Bool,
            "false",
            "AllowedValues");
    }

    [Fact]
    public void RegularExpression_stays_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class NameOptions
            {
                [RegularExpression("^[a-z]+$")]
                public string Code { get; set; } = "";
            }
            """,
            "Code",
            ScalarKind.String,
            "123");
    }

    [Fact]
    public void TypeId_override_stays_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class WeirdRangeAttribute : RangeAttribute
            {
                public WeirdRangeAttribute(int minimum, int maximum) : base(minimum, maximum)
                {
                }

                public override object TypeId => "weird";
            }

            public sealed class ServerOptions
            {
                [WeirdRange(1, 65535)]
                public int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.Number,
            "0");
    }

    [Fact]
    public void Derived_property_last_wins_range_stays_quiet_inside_override_bounds()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public class BaseOptions
            {
                [Range(1, 10)]
                public virtual int Port { get; set; }
            }

            public sealed class ServerOptions : BaseOptions
            {
                [Range(1, 65535)]
                public override int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.Number,
            "100",
            containingTypeName: "ServerOptions");
    }

    private static void AssertFailure(
        string source,
        string propertyName,
        ScalarKind kind,
        string rawValue,
        string expectedAttribute)
    {
        var (property, type) = GetProperty(source, propertyName);
        Assert.True(
            DataAnnotationsConstraints.TryGetProvableFailure(property, type, kind, rawValue, out var name),
            "Expected a provable DataAnnotations failure.");
        Assert.Equal(expectedAttribute, name);
    }

    private static void AssertQuiet(
        string source,
        string propertyName,
        ScalarKind kind,
        string rawValue,
        string? containingTypeName = null)
    {
        var (property, type) = GetProperty(source, propertyName, containingTypeName);
        Assert.False(DataAnnotationsConstraints.TryGetProvableFailure(property, type, kind, rawValue, out _));
    }

    private static (IPropertySymbol Property, ITypeSymbol Type) GetProperty(
        string source,
        string propertyName,
        string? containingTypeName = null)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            "ConstraintTests",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var property = tree.GetRoot()
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax>()
            .Select(node => model.GetDeclaredSymbol(node))
            .OfType<IPropertySymbol>()
            .Single(symbol =>
                string.Equals(symbol.Name, propertyName, StringComparison.Ordinal) &&
                (containingTypeName is null ||
                 string.Equals(symbol.ContainingType.Name, containingTypeName, StringComparison.Ordinal)));

        return (property, property.Type);
    }
}
