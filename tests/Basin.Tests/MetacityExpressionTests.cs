using Basin.Frames.Metacity;
using Xunit;

namespace Basin.Tests;

public sealed class MetacityExpressionTests
{
    private static readonly MetacityTheme Theme = MetacityParserTests.ParseText(
        MetacityParserTests.Wrap("<constant name=\"Ten\" value=\"10\"/><constant name=\"Half\" value=\"0.5\"/>" + MetacityParserTests.Geometry + "<draw_ops name=\"empty\"/>" + MetacityParserTests.MinimalStyle),
        1);

    private static MetacityExpressionEnvironment Env => new()
    {
        X = 100,
        Y = 200,
        Width = 40,
        Height = 30,
        ObjectWidth = 8,
        ObjectHeight = 6,
        LeftWidth = 1,
        RightWidth = 2,
        TopHeight = 3,
        BottomHeight = 4,
        MiniIconWidth = 16,
        MiniIconHeight = 16,
        IconWidth = 48,
        IconHeight = 48,
        TitleWidth = 70,
        TitleHeight = 12,
        FrameXCenter = 50,
        FrameYCenter = 60,
    };

    private static int Eval(string source, bool hasObject = false)
    {
        var expression = MetacityExpression.Compile(source, Theme, out var error);
        Assert.Null(error);
        var env = Env;
        return expression.Evaluate(in env, hasObject);
    }

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("10 - 2 - 3", 5)]
    [InlineData("2 * 3 `max` 7", 7)]
    [InlineData("2 + 3 `max` 4", 5)]
    [InlineData("1 `min` 2 + 5", 1)]
    [InlineData("7 / 2", 3)]
    [InlineData("7 % 4", 3)]
    [InlineData("7 / 2.0", 3)]
    [InlineData("0.5 * 3", 1)]
    [InlineData("Ten * 2", 20)]
    [InlineData("Half * Ten", 5)]
    [InlineData("((width))", 40)]
    public void Precedence_promotion_and_truncation_follow_marco(string source, int expected) =>
        Assert.Equal(expected, Eval(source));

    [Fact]
    public void Constant_expressions_fold_at_load()
    {
        var expression = MetacityExpression.Compile("Ten * 2 + 1", Theme, out var error);
        Assert.Null(error);
        Assert.True(expression.Constant);
        Assert.Equal(21, expression.Value);
        var dynamic = MetacityExpression.Compile("width - Ten", Theme, out error);
        Assert.Null(error);
        Assert.False(dynamic.Constant);
    }

    [Theory]
    [InlineData("width", 40)]
    [InlineData("height", 30)]
    [InlineData("left_width", 1)]
    [InlineData("right_width", 2)]
    [InlineData("top_height", 3)]
    [InlineData("bottom_height", 4)]
    [InlineData("mini_icon_width", 16)]
    [InlineData("mini_icon_height", 16)]
    [InlineData("icon_width", 48)]
    [InlineData("icon_height", 48)]
    [InlineData("title_width", 70)]
    [InlineData("title_height", 12)]
    [InlineData("frame_x_center", 50)]
    [InlineData("frame_y_center", 60)]
    public void Every_variable_reads_its_source(string source, int expected) =>
        Assert.Equal(expected, Eval(source));

    [Fact]
    public void Object_size_is_defined_only_inside_an_image()
    {
        Assert.Equal(8, Eval("object_width", hasObject: true));
        Assert.Equal(6, Eval("object_height", hasObject: true));
        Assert.Equal(0, Eval("object_width + 5", hasObject: false));
    }

    [Fact]
    public void Positions_add_the_origin_and_sizes_clamp_at_one()
    {
        var expression = MetacityExpression.Compile("width - 45", Theme, out var error);
        Assert.Null(error);
        var env = Env;
        Assert.Equal(95, expression.Position(in env, horizontal: true));
        Assert.Equal(195, expression.Position(in env, horizontal: false));
        Assert.Equal(1, expression.Size(in env));
    }

    [Theory]
    [InlineData("width / 0")]
    [InlineData("width % 0")]
    [InlineData("width % 2.5")]
    [InlineData("width + nonsense")]
    public void Runtime_errors_yield_zero(string source)
    {
        var expression = MetacityExpression.Compile(source, Theme, out var error);
        Assert.Null(error);
        var env = Env;
        Assert.Equal(0, expression.Evaluate(in env, false));
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("width $ 2", "character '$'")]
    [InlineData("(width", "parenthes")]
    [InlineData("width)", "parenthes")]
    [InlineData("width +", "operator")]
    [InlineData("* width", "operator")]
    [InlineData("1 / 0", "division by zero")]
    [InlineData("width `mid` 2", "unknown operator")]
    public void Structural_errors_fail_at_load(string source, string expected)
    {
        MetacityExpression.Compile(source, Theme, out var error);
        Assert.NotNull(error);
        Assert.Contains(expected, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Thirty_two_slots_overflow()
    {
        var sixteen = string.Join(" + ", Enumerable.Repeat("width", 16));
        Assert.Equal(640, Eval(sixteen));
        var seventeen = string.Join(" + ", Enumerable.Repeat("width", 17));
        MetacityExpression.Compile(seventeen, Theme, out var error);
        Assert.NotNull(error);
        Assert.Contains("overflowed", error);
    }
}
