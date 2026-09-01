using Basin.WindowManager;

namespace DeskbarWm;

internal sealed class SatArea(SatGroup group, SatTab left, SatTab top, SatTab right, SatTab bottom)
{
    public SatGroup Group { get; } = group;

    public SatTab Left { get; set; } = left;

    public SatTab Top { get; set; } = top;

    public SatTab Right { get; set; } = right;

    public SatTab Bottom { get; set; } = bottom;

    public List<ManagedWindow> Windows { get; } = [];

    public ManagedWindow? Front { get; set; }

    public Rect Cell => new(
        Left.Position,
        Top.Position,
        Math.Max(Right.Position - Left.Position, 1),
        Math.Max(Bottom.Position - Top.Position, 1));

    public bool Touches(SatTab tab) => ReferenceEquals(Left, tab) || ReferenceEquals(Top, tab)
        || ReferenceEquals(Right, tab) || ReferenceEquals(Bottom, tab);
}
