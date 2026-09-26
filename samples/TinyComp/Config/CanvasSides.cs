namespace TinyComp;

internal static class CanvasSides
{
    private static readonly CanvasSide[] EachSide = [CanvasSide.Left, CanvasSide.Right, CanvasSide.Top, CanvasSide.Bottom];

    private static readonly CanvasSide[] HorizontalSides = [CanvasSide.Left, CanvasSide.Right];

    private static readonly CanvasSide[] VerticalSides = [CanvasSide.Top, CanvasSide.Bottom];

    public static ReadOnlySpan<CanvasSide> Each => EachSide;

    public static ReadOnlySpan<CanvasSide> Horizontal => HorizontalSides;

    public static ReadOnlySpan<CanvasSide> Vertical => VerticalSides;
}
