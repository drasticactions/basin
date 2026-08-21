using Basin.Shell.Xdg.Protocol;

namespace Basin.Shell.Xdg;

public struct XdgPositionerRules
{
    public int Width;

    public int Height;

    public Box AnchorRect;

    public XdgPositioner.Anchor Anchor;

    public XdgPositioner.Gravity Gravity;

    public XdgPositioner.ConstraintAdjustment ConstraintAdjustment;

    public int OffsetX;

    public int OffsetY;

    public bool Reactive;

    public readonly bool IsComplete => Width > 0 && Height > 0 && AnchorRect.Width >= 0 && AnchorRect.Height >= 0;

    public readonly Box Place()
    {
        var anchor = AnchorPoint();
        var x = anchor.X + OffsetX;
        var y = anchor.Y + OffsetY;

        x -= Gravity switch
        {
            XdgPositioner.Gravity.Left or XdgPositioner.Gravity.TopLeft or XdgPositioner.Gravity.BottomLeft => Width,
            XdgPositioner.Gravity.Right or XdgPositioner.Gravity.TopRight or XdgPositioner.Gravity.BottomRight => 0,
            _ => Width / 2,
        };
        y -= Gravity switch
        {
            XdgPositioner.Gravity.Top or XdgPositioner.Gravity.TopLeft or XdgPositioner.Gravity.TopRight => Height,
            XdgPositioner.Gravity.Bottom or XdgPositioner.Gravity.BottomLeft or XdgPositioner.Gravity.BottomRight => 0,
            _ => Height / 2,
        };

        return new Box(x, y, Width, Height);
    }

    public readonly Box Constrain(Box constraint)
    {
        var box = Place();

        if (IsConstrainedX(box, constraint))
        {
            if ((ConstraintAdjustment & XdgPositioner.ConstraintAdjustment.FlipX) != 0)
            {
                var flipped = FlippedX().Place() with { Y = box.Y };
                if (!IsConstrainedX(flipped, constraint))
                {
                    box = box with { X = flipped.X };
                }
            }

            if (IsConstrainedX(box, constraint) && (ConstraintAdjustment & XdgPositioner.ConstraintAdjustment.SlideX) != 0)
            {
                var x = box.X;
                if (box.Right > constraint.Right)
                {
                    x = constraint.Right - box.Width;
                }

                if (x < constraint.X)
                {
                    x = constraint.X;
                }

                box = box with { X = x };
            }

            if (IsConstrainedX(box, constraint) && (ConstraintAdjustment & XdgPositioner.ConstraintAdjustment.ResizeX) != 0)
            {
                var x1 = Math.Max(box.X, constraint.X);
                var x2 = Math.Min(box.Right, constraint.Right);
                if (x2 > x1)
                {
                    box = box with { X = x1, Width = x2 - x1 };
                }
            }
        }

        if (IsConstrainedY(box, constraint))
        {
            if ((ConstraintAdjustment & XdgPositioner.ConstraintAdjustment.FlipY) != 0)
            {
                var flipped = FlippedY().Place() with { X = box.X };
                if (!IsConstrainedY(flipped, constraint))
                {
                    box = box with { Y = flipped.Y };
                }
            }

            if (IsConstrainedY(box, constraint) && (ConstraintAdjustment & XdgPositioner.ConstraintAdjustment.SlideY) != 0)
            {
                var y = box.Y;
                if (box.Bottom > constraint.Bottom)
                {
                    y = constraint.Bottom - box.Height;
                }

                if (y < constraint.Y)
                {
                    y = constraint.Y;
                }

                box = box with { Y = y };
            }

            if (IsConstrainedY(box, constraint) && (ConstraintAdjustment & XdgPositioner.ConstraintAdjustment.ResizeY) != 0)
            {
                var y1 = Math.Max(box.Y, constraint.Y);
                var y2 = Math.Min(box.Bottom, constraint.Bottom);
                if (y2 > y1)
                {
                    box = box with { Y = y1, Height = y2 - y1 };
                }
            }
        }

        return box;
    }

    private static bool IsConstrainedX(in Box box, in Box constraint) =>
        box.X < constraint.X || box.Right > constraint.Right;

    private static bool IsConstrainedY(in Box box, in Box constraint) =>
        box.Y < constraint.Y || box.Bottom > constraint.Bottom;

    private readonly Point AnchorPoint()
    {
        var rect = AnchorRect;
        var x = Anchor switch
        {
            XdgPositioner.Anchor.Left or XdgPositioner.Anchor.TopLeft or XdgPositioner.Anchor.BottomLeft => rect.X,
            XdgPositioner.Anchor.Right or XdgPositioner.Anchor.TopRight or XdgPositioner.Anchor.BottomRight => rect.Right,
            _ => rect.X + rect.Width / 2,
        };
        var y = Anchor switch
        {
            XdgPositioner.Anchor.Top or XdgPositioner.Anchor.TopLeft or XdgPositioner.Anchor.TopRight => rect.Y,
            XdgPositioner.Anchor.Bottom or XdgPositioner.Anchor.BottomLeft or XdgPositioner.Anchor.BottomRight => rect.Bottom,
            _ => rect.Y + rect.Height / 2,
        };
        return new Point(x, y);
    }

    internal readonly XdgPositionerRules FlippedX() => this with
    {
        Anchor = Anchor switch
        {
            XdgPositioner.Anchor.Left => XdgPositioner.Anchor.Right,
            XdgPositioner.Anchor.Right => XdgPositioner.Anchor.Left,
            XdgPositioner.Anchor.TopLeft => XdgPositioner.Anchor.TopRight,
            XdgPositioner.Anchor.TopRight => XdgPositioner.Anchor.TopLeft,
            XdgPositioner.Anchor.BottomLeft => XdgPositioner.Anchor.BottomRight,
            XdgPositioner.Anchor.BottomRight => XdgPositioner.Anchor.BottomLeft,
            var other => other,
        },
        Gravity = Gravity switch
        {
            XdgPositioner.Gravity.Left => XdgPositioner.Gravity.Right,
            XdgPositioner.Gravity.Right => XdgPositioner.Gravity.Left,
            XdgPositioner.Gravity.TopLeft => XdgPositioner.Gravity.TopRight,
            XdgPositioner.Gravity.TopRight => XdgPositioner.Gravity.TopLeft,
            XdgPositioner.Gravity.BottomLeft => XdgPositioner.Gravity.BottomRight,
            XdgPositioner.Gravity.BottomRight => XdgPositioner.Gravity.BottomLeft,
            var other => other,
        },
    };

    internal readonly XdgPositionerRules FlippedY() => this with
    {
        Anchor = Anchor switch
        {
            XdgPositioner.Anchor.Top => XdgPositioner.Anchor.Bottom,
            XdgPositioner.Anchor.Bottom => XdgPositioner.Anchor.Top,
            XdgPositioner.Anchor.TopLeft => XdgPositioner.Anchor.BottomLeft,
            XdgPositioner.Anchor.BottomLeft => XdgPositioner.Anchor.TopLeft,
            XdgPositioner.Anchor.TopRight => XdgPositioner.Anchor.BottomRight,
            XdgPositioner.Anchor.BottomRight => XdgPositioner.Anchor.TopRight,
            var other => other,
        },
        Gravity = Gravity switch
        {
            XdgPositioner.Gravity.Top => XdgPositioner.Gravity.Bottom,
            XdgPositioner.Gravity.Bottom => XdgPositioner.Gravity.Top,
            XdgPositioner.Gravity.TopLeft => XdgPositioner.Gravity.BottomLeft,
            XdgPositioner.Gravity.BottomLeft => XdgPositioner.Gravity.TopLeft,
            XdgPositioner.Gravity.TopRight => XdgPositioner.Gravity.BottomRight,
            XdgPositioner.Gravity.BottomRight => XdgPositioner.Gravity.TopRight,
            var other => other,
        },
    };
}
