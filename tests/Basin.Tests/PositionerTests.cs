using Basin.Shell.Xdg;
using Basin.Shell.Xdg.Protocol;
using Xunit;

namespace Basin.Tests;

public sealed class PositionerTests
{
    private static readonly Box Constraint = new(0, 0, 200, 150);

    private static XdgPositionerRules Rules(
        XdgPositioner.Anchor anchor = XdgPositioner.Anchor.None,
        XdgPositioner.Gravity gravity = XdgPositioner.Gravity.None,
        XdgPositioner.ConstraintAdjustment adjustment = XdgPositioner.ConstraintAdjustment.None,
        int offsetX = 0,
        int offsetY = 0,
        Box? anchorRect = null,
        int width = 50,
        int height = 40) => new()
    {
        Width = width,
        Height = height,
        AnchorRect = anchorRect ?? new Box(10, 10, 20, 20),
        Anchor = anchor,
        Gravity = gravity,
        ConstraintAdjustment = adjustment,
        OffsetX = offsetX,
        OffsetY = offsetY,
    };

    [Fact]
    public void Anchor_none_centers_on_the_anchor_rect()
    {
        Assert.Equal(new Box(-5, 0, 50, 40), Rules().Place());
    }

    [Fact]
    public void Anchor_bottom_right_with_gravity_bottom_right_extends_away()
    {
        var box = Rules(XdgPositioner.Anchor.BottomRight, XdgPositioner.Gravity.BottomRight).Place();
        Assert.Equal(new Box(30, 30, 50, 40), box);
    }

    [Fact]
    public void Anchor_top_left_with_gravity_top_left_extends_back()
    {
        var box = Rules(XdgPositioner.Anchor.TopLeft, XdgPositioner.Gravity.TopLeft).Place();
        Assert.Equal(new Box(10 - 50, 10 - 40, 50, 40), box);
    }

    [Fact]
    public void Offset_shifts_the_placement()
    {
        var box = Rules(XdgPositioner.Anchor.BottomRight, XdgPositioner.Gravity.BottomRight, offsetX: 7, offsetY: -3).Place();
        Assert.Equal(new Box(37, 27, 50, 40), box);
    }

    [Fact]
    public void Unconstrained_placement_is_untouched()
    {
        var rules = Rules(XdgPositioner.Anchor.BottomRight, XdgPositioner.Gravity.BottomRight,
            XdgPositioner.ConstraintAdjustment.FlipX | XdgPositioner.ConstraintAdjustment.SlideY);
        Assert.Equal(rules.Place(), rules.Constrain(Constraint));
    }

    [Fact]
    public void Flip_x_mirrors_when_the_right_edge_overflows()
    {
        var rules = Rules(XdgPositioner.Anchor.BottomRight, XdgPositioner.Gravity.BottomRight,
            XdgPositioner.ConstraintAdjustment.FlipX, anchorRect: new Box(170, 10, 20, 20));
        var box = rules.Constrain(Constraint);

        Assert.Equal(120, box.X);
        Assert.Equal(30, box.Y);
    }

    [Fact]
    public void Flip_x_keeps_the_offset_unmirrored()
    {
        var rules = Rules(XdgPositioner.Anchor.BottomRight, XdgPositioner.Gravity.BottomRight,
            XdgPositioner.ConstraintAdjustment.FlipX, offsetX: 9, anchorRect: new Box(170, 10, 20, 20));
        var box = rules.Constrain(Constraint);

        Assert.Equal(170 - 50 + 9, box.X);
    }

    [Fact]
    public void Flip_x_reverts_when_flipping_does_not_help()
    {
        var rules = Rules(XdgPositioner.Anchor.BottomRight, XdgPositioner.Gravity.BottomRight,
            XdgPositioner.ConstraintAdjustment.FlipX, anchorRect: new Box(5, 10, 20, 20), width: 300);
        var box = rules.Constrain(Constraint);
        Assert.Equal(rules.Place().X, box.X);
    }

    [Fact]
    public void Slide_x_pulls_the_box_back_inside()
    {
        var rules = Rules(XdgPositioner.Anchor.BottomRight, XdgPositioner.Gravity.BottomRight,
            XdgPositioner.ConstraintAdjustment.SlideX, anchorRect: new Box(170, 10, 20, 20));
        var box = rules.Constrain(Constraint);
        Assert.Equal(150, box.X);
        Assert.Equal(50, box.Width);
    }

    [Fact]
    public void Slide_x_prefers_the_left_edge_when_larger_than_the_constraint()
    {
        var rules = Rules(XdgPositioner.Anchor.BottomRight, XdgPositioner.Gravity.BottomRight,
            XdgPositioner.ConstraintAdjustment.SlideX, anchorRect: new Box(170, 10, 20, 20), width: 300);
        var box = rules.Constrain(Constraint);
        Assert.Equal(0, box.X);
    }

    [Fact]
    public void Resize_x_clamps_to_the_constraint()
    {
        var rules = Rules(XdgPositioner.Anchor.BottomRight, XdgPositioner.Gravity.BottomRight,
            XdgPositioner.ConstraintAdjustment.ResizeX, anchorRect: new Box(170, 10, 20, 20));
        var box = rules.Constrain(Constraint);
        Assert.Equal(190, box.X);
        Assert.Equal(10, box.Width);
    }

    [Fact]
    public void Flip_y_mirrors_when_the_bottom_overflows()
    {
        var rules = Rules(XdgPositioner.Anchor.Bottom, XdgPositioner.Gravity.Bottom,
            XdgPositioner.ConstraintAdjustment.FlipY, anchorRect: new Box(10, 120, 20, 20));
        var box = rules.Constrain(Constraint);

        Assert.Equal(120 - 40, box.Y);
    }

    [Fact]
    public void Slide_and_resize_compose_across_axes()
    {
        var rules = Rules(XdgPositioner.Anchor.BottomRight, XdgPositioner.Gravity.BottomRight,
            XdgPositioner.ConstraintAdjustment.SlideX | XdgPositioner.ConstraintAdjustment.ResizeY,
            anchorRect: new Box(180, 130, 10, 10));
        var box = rules.Constrain(Constraint);
        Assert.Equal(150, box.X);
        Assert.Equal(140, box.Y);
        Assert.Equal(10, box.Height);
    }
}
