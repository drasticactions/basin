namespace Basin.Eis;

/// <summary>
/// Holds the geometry rules for input capture barriers.
/// </summary>
public static class InputCaptureBarriers
{
    private enum Fit
    {
        Invalid,
        Partial,
        Valid,
    }

    /// <summary>
    /// Makes a barrier from the unsigned wire values. Each coordinate is reinterpreted as a signed integer, so a negative layout position survives.
    /// </summary>
    /// <param name="id">The client's barrier id.</param>
    /// <param name="x1">The x of the first end, as sent.</param>
    /// <param name="y1">The y of the first end, as sent.</param>
    /// <param name="x2">The x of the second end, as sent.</param>
    /// <param name="y2">The y of the second end, as sent.</param>
    /// <returns>The barrier with signed coordinates.</returns>
    public static InputCaptureBarrier FromWire(uint id, uint x1, uint y1, uint x2, uint y2) =>
        new(id, unchecked((int)x1), unchecked((int)y1), unchecked((int)x2), unchecked((int)y2));

    /// <summary>
    /// Tells whether a barrier is a full edge of exactly one output. The barrier must be horizontal or vertical and cover the whole edge, and it must not touch part of another output's edge.
    /// </summary>
    /// <param name="barrier">The barrier to check.</param>
    /// <param name="layout">The output layout whose outputs give the edges.</param>
    /// <returns><see langword="true"/> when the barrier matches one full output edge and no partial edge.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is <see langword="null"/>.</exception>
    public static bool IsValid(in InputCaptureBarrier barrier, OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var (x1, y1, x2, y2) = (barrier.X1, barrier.Y1, barrier.X2, barrier.Y2);
        if (x1 != x2 && y1 != y2)
        {
            return false;
        }

        if (x1 == x2 && y1 == y2)
        {
            return false;
        }

        if (x1 > x2)
        {
            (x1, x2) = (x2, x1);
        }

        if (y1 > y2)
        {
            (y1, y2) = (y2, y1);
        }

        var valid = 0;
        var partial = 0;
        foreach (var (output, _) in layout.Outputs)
        {
            switch (FitAgainst(x1, y1, x2, y2, layout.BoxOf(output)))
            {
                case Fit.Valid:
                    valid++;
                    break;

                case Fit.Partial:
                    partial++;
                    break;
            }
        }

        return valid == 1 && partial == 0;
    }

    /// <summary>
    /// Tells whether a pointer motion segment intersects the barrier segment, ends included. Parallel segments never cross.
    /// </summary>
    /// <param name="barrier">The barrier to test.</param>
    /// <param name="fromX">The layout x before the motion.</param>
    /// <param name="fromY">The layout y before the motion.</param>
    /// <param name="toX">The layout x after the motion.</param>
    /// <param name="toY">The layout y after the motion.</param>
    /// <returns><see langword="true"/> when the two segments intersect.</returns>
    public static bool Crosses(in InputCaptureBarrier barrier, double fromX, double fromY, double toX, double toY)
    {
        double s1X = barrier.X2 - barrier.X1;
        double s1Y = barrier.Y2 - barrier.Y1;
        var s2X = toX - fromX;
        var s2Y = toY - fromY;
        var denominator = (-s2X * s1Y) + (s1X * s2Y);
        if (denominator == 0)
        {
            return false;
        }

        var s = ((-s1Y * (barrier.X1 - fromX)) + (s1X * (barrier.Y1 - fromY))) / denominator;
        var t = ((s2X * (barrier.Y1 - fromY)) - (s2Y * (barrier.X1 - fromX))) / denominator;
        return s >= 0 && s <= 1 && t >= 0 && t <= 1;
    }

    private static Fit FitAgainst(int x1, int y1, int x2, int y2, in Box output)
    {
        var mx1 = output.X;
        var my1 = output.Y;
        var mx2 = output.X + output.Width - 1;
        var my2 = output.Y + output.Height - 1;
        if (x1 == x2)
        {
            if (x1 != mx1 && x1 != mx2 + 1)
            {
                return Fit.Invalid;
            }

            if (y1 != my1 || y2 != my2)
            {
                return (my1 <= y1 && y1 <= my2) || (my1 <= y2 && y2 <= my2) ? Fit.Partial : Fit.Invalid;
            }

            return Fit.Valid;
        }

        if (y1 != my1 && y1 != my2 + 1)
        {
            return Fit.Invalid;
        }

        if (x1 != mx1 || x2 != mx2)
        {
            return (mx1 <= x1 && x1 <= mx2) || (mx1 <= x2 && x2 <= mx2) ? Fit.Partial : Fit.Invalid;
        }

        return Fit.Valid;
    }
}
