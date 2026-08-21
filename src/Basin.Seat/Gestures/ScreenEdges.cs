namespace Basin.Seat;

[Flags]
public enum ScreenEdges
{
    None = 0,

    Left = 1,

    Right = 2,

    Top = 4,

    Bottom = 8,

    All = Left | Right | Top | Bottom,
}
