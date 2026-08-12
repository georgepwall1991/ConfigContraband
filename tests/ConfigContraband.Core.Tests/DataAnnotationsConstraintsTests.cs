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
    public void AllowedValues_reports_unlisted_char()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class CodeOptions
            {
                [AllowedValues('A', 'B')]
                public char Letter { get; set; }
            }
            """,
            "Letter",
            ScalarKind.String,
            "C",
            "AllowedValues");
    }

    [Fact]
    public void AllowedValues_stays_quiet_for_listed_char()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class CodeOptions
            {
                [AllowedValues('A', 'B')]
                public char Letter { get; set; }
            }
            """,
            "Letter",
            ScalarKind.String,
            "A");
    }

    [Fact]
    public void AllowedValues_stays_quiet_for_padded_listed_char()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class CodeOptions
            {
                [AllowedValues('A')]
                public char Letter { get; set; }
            }
            """,
            "Letter",
            ScalarKind.String,
            " A");
    }

    [Fact]
    public void AllowedValues_stays_quiet_for_single_space_char()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class CodeOptions
            {
                [AllowedValues(' ')]
                public char Letter { get; set; }
            }
            """,
            "Letter",
            ScalarKind.String,
            " ");
    }

    [Fact]
    public void AllowedValues_reports_single_space_when_null_char_is_allowed()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class CodeOptions
            {
                [AllowedValues('\0')]
                public char Letter { get; set; }
            }
            """,
            "Letter",
            ScalarKind.String,
            " ",
            "AllowedValues");
    }

    [Fact]
    public void Whitespace_only_char_maps_to_null_char()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class CodeOptions
            {
                [AllowedValues('\0')]
                public char Letter { get; set; }
            }
            """,
            "Letter",
            ScalarKind.String,
            "  ");
    }

    [Fact]
    public void DeniedValues_reports_listed_char()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class CodeOptions
            {
                [DeniedValues('X')]
                public char Letter { get; set; }
            }
            """,
            "Letter",
            ScalarKind.String,
            "X",
            "DeniedValues");
    }

    [Fact]
    public void AllowedValues_with_no_constructor_arguments_stays_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class EnvOptions
            {
                [AllowedValues]
                public string Environment { get; set; } = "";
            }
            """,
            "Environment",
            ScalarKind.String,
            "prod");
    }

    [Fact]
    public void Empty_char_stays_quiet_for_allowed_values()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class CodeOptions
            {
                [AllowedValues('\0')]
                public char Letter { get; set; }
            }
            """,
            "Letter",
            ScalarKind.String,
            "");
    }

    [Fact]
    public void Multi_character_char_value_stays_quiet_for_cfg008()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class CodeOptions
            {
                [AllowedValues('A')]
                public char Letter { get; set; }
            }
            """,
            "Letter",
            ScalarKind.String,
            "AB");
    }

    [Fact]
    public void Range_subclass_without_is_valid_override_stays_quiet()
    {
        AssertQuiet(
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
            "0");
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
    public void TryReadRangeOperands_is_false_for_parameterless_range_subclass()
    {
        var (property, _) = GetProperty(
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
            "Port");

        Assert.False(ValidationAttributeLimits.TryReadRangeOperands(property.GetAttributes()[0], out _));
    }

    [Fact]
    public void TryReadRangeOperands_is_false_when_three_arguments_are_not_a_type_overload()
    {
        var (property, _) = GetProperty(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class TripleRangeAttribute : RangeAttribute
            {
                public TripleRangeAttribute(int minimum, int maximum, int unused) : base(minimum, maximum)
                {
                }
            }

            public sealed class ServerOptions
            {
                [TripleRange(1, 65535, 0)]
                public int Port { get; set; }
            }
            """,
            "Port");

        Assert.False(ValidationAttributeLimits.TryReadRangeOperands(property.GetAttributes()[0], out _));
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

    [Fact]
    public void Range_reports_when_converted_value_overflows_int_operand()
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
            "3000000000",
            "Range");
    }

    [Fact]
    public void Length_with_inverted_bounds_stays_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class NameOptions
            {
                [Length(5, 2)]
                public string Code { get; set; } = "";
            }
            """,
            "Code",
            ScalarKind.String,
            "abc");
    }

    [Fact]
    public void StringLength_without_minimum_stays_quiet_inside_maximum()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class NameOptions
            {
                [StringLength(8)]
                public string Code { get; set; } = "";
            }
            """,
            "Code",
            ScalarKind.String,
            "abcd");
    }

    [Fact]
    public void Unsupported_property_type_stays_quiet()
    {
        AssertQuiet(
            """
            using System;
            using System.ComponentModel.DataAnnotations;

            public sealed class IdOptions
            {
                [Range(typeof(int), "1", "10", ParseLimitsInInvariantCulture = true)]
                public Guid Id { get; set; }
            }
            """,
            "Id",
            ScalarKind.String,
            "00000000-0000-0000-0000-000000000001");
    }

    [Fact]
    public void MinLength_on_non_string_stays_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [MinLength(1)]
                public int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.Number,
            "0");
    }

    [Fact]
    public void Length_on_non_string_stays_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [Length(1, 4)]
                public int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.Number,
            "0");
    }

    [Fact]
    public void StringLength_on_non_string_stays_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [StringLength(3)]
                public int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.Number,
            "0");
    }

    [Fact]
    public void MaxLength_on_non_string_stays_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [MaxLength(3)]
                public int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.Number,
            "0");
    }

    [Fact]
    public void Unconvertible_bool_stays_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class FlagOptions
            {
                [AllowedValues(true)]
                public bool Enabled { get; set; }
            }
            """,
            "Enabled",
            ScalarKind.String,
            "yes");
    }

    [Theory]
    [InlineData("float")]
    [InlineData("double")]
    [InlineData("decimal")]
    public void Unconvertible_floating_value_stays_quiet(string clrType)
    {
        AssertQuiet(
            $$"""
            using System.ComponentModel.DataAnnotations;

            public sealed class NumericOptions
            {
                [Range(typeof({{clrType}}), "1", "10", ParseLimitsInInvariantCulture = true)]
                public {{clrType}} Amount { get; set; }
            }
            """,
            "Amount",
            ScalarKind.String,
            "nope");
    }

    [Theory]
    [InlineData("sbyte")]
    [InlineData("byte")]
    [InlineData("short")]
    [InlineData("ushort")]
    [InlineData("uint")]
    [InlineData("long")]
    [InlineData("ulong")]
    public void Hex_integer_inside_bounds_for_each_integral_type(string clrType)
    {
        AssertQuiet(
            $$"""
            using System.ComponentModel.DataAnnotations;

            public sealed class NumericOptions
            {
                [Range(typeof({{clrType}}), "1", "32", ParseLimitsInInvariantCulture = true)]
                public {{clrType}} Amount { get; set; }
            }
            """,
            "Amount",
            ScalarKind.String,
            "0x10");
    }

    [Fact]
    public void AllowedValues_reports_numeric_enum_value_not_in_list()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public enum Color { Red = 1, Blue = 2 }

            public sealed class TintOptions
            {
                [AllowedValues(Color.Red)]
                public Color Tint { get; set; }
            }
            """,
            "Tint",
            ScalarKind.Number,
            "2",
            "AllowedValues");
    }

    [Fact]
    public void AllowedValues_stays_quiet_for_enum_comma_list()
    {
        AssertQuiet(
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
            "Red, Blue");
    }

    [Fact]
    public void AllowedValues_stays_quiet_for_flags_enum_comma_list()
    {
        AssertQuiet(
            """
            using System;
            using System.ComponentModel.DataAnnotations;

            [Flags]
            public enum Perms { Read = 1, Write = 2, ReadWrite = 3 }

            public sealed class AccessOptions
            {
                [AllowedValues(Perms.ReadWrite)]
                public Perms Access { get; set; }
            }
            """,
            "Access",
            ScalarKind.String,
            "Read, Write");
    }

    [Fact]
    public void Unknown_enum_member_stays_quiet()
    {
        AssertQuiet(
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
            "Green");
    }

    [Fact]
    public void Unparseable_invariant_range_bounds_stay_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [Range(typeof(int), "abc", "10", ParseLimitsInInvariantCulture = true)]
                public int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.Number,
            "5");
    }

    [Fact]
    public void Empty_enum_value_stays_quiet()
    {
        AssertQuiet(
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
            "   ");
    }

    [Fact]
    public void Empty_enum_token_stays_quiet()
    {
        AssertQuiet(
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
            "Red,");
    }

    [Theory]
    [InlineData("byte")]
    [InlineData("sbyte")]
    [InlineData("short")]
    [InlineData("ushort")]
    [InlineData("uint")]
    [InlineData("long")]
    [InlineData("ulong")]
    public void AllowedValues_reports_numeric_enum_for_each_backing_type(string backing)
    {
        AssertFailure(
            $$"""
            using System.ComponentModel.DataAnnotations;

            public enum Color : {{backing}} { Red = 1, Blue = 2 }

            public sealed class TintOptions
            {
                [AllowedValues(Color.Red)]
                public Color Tint { get; set; }
            }
            """,
            "Tint",
            ScalarKind.Number,
            "2",
            "AllowedValues");
    }

    [Theory]
    [InlineData("sbyte")]
    [InlineData("byte")]
    [InlineData("short")]
    [InlineData("ushort")]
    [InlineData("uint")]
    [InlineData("long")]
    [InlineData("ulong")]
    public void Invalid_hex_stays_quiet_for_each_integral_type(string clrType)
    {
        AssertQuiet(
            $$"""
            using System.ComponentModel.DataAnnotations;

            public sealed class NumericOptions
            {
                [Range(typeof({{clrType}}), "1", "32", ParseLimitsInInvariantCulture = true)]
                public {{clrType}} Amount { get; set; }
            }
            """,
            "Amount",
            ScalarKind.String,
            "0xGG");
    }

    [Theory]
    [InlineData("byte")]
    [InlineData("sbyte")]
    [InlineData("short")]
    [InlineData("ushort")]
    [InlineData("uint")]
    [InlineData("long")]
    [InlineData("ulong")]
    public void Unknown_numeric_enum_token_stays_quiet_for_each_backing_type(string backing)
    {
        AssertQuiet(
            $$"""
            using System.ComponentModel.DataAnnotations;

            public enum Color : {{backing}} { Red = 1, Blue = 2 }

            public sealed class TintOptions
            {
                [AllowedValues(Color.Red)]
                public Color Tint { get; set; }
            }
            """,
            "Tint",
            ScalarKind.String,
            "nope");
    }

    [Fact]
    public void Typeof_range_with_null_minimum_stays_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [Range(typeof(int), null, "10", ParseLimitsInInvariantCulture = true)]
                public int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.Number,
            "0");
    }

    [Fact]
    public void Typeof_range_with_null_maximum_stays_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class ServerOptions
            {
                [Range(typeof(int), "1", null, ParseLimitsInInvariantCulture = true)]
                public int Port { get; set; }
            }
            """,
            "Port",
            ScalarKind.Number,
            "100");
    }

    [Fact]
    public void AllowedValues_subclass_with_extra_constructor_argument_stays_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class TaggedAllowedAttribute : AllowedValuesAttribute
            {
                public TaggedAllowedAttribute(int tag, params object[] values) : base(values)
                {
                }
            }

            public sealed class EnvOptions
            {
                [TaggedAllowed(1, "dev", "prod")]
                public string Environment { get; set; } = "";
            }
            """,
            "Environment",
            ScalarKind.String,
            "staging");
    }

    [Fact]
    public void AllowedValues_reports_when_list_types_do_not_match_property()
    {
        AssertFailure(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class EnvOptions
            {
                [AllowedValues(1, 2)]
                public string Environment { get; set; } = "";
            }
            """,
            "Environment",
            ScalarKind.String,
            "1",
            "AllowedValues");
    }

    [Fact]
    public void AllowedValues_null_entry_stays_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class EnvOptions
            {
                [AllowedValues(null)]
                public string Environment { get; set; } = "";
            }
            """,
            "Environment",
            ScalarKind.String,
            "prod");
    }

    [Fact]
    public void Array_property_type_stays_quiet()
    {
        AssertQuiet(
            """
            using System.ComponentModel.DataAnnotations;

            public sealed class NameOptions
            {
                [MaxLength(2)]
                public string[] Tags { get; set; } = [];
            }
            """,
            "Tags",
            ScalarKind.String,
            "abc");
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
