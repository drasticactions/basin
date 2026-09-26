namespace TinyComp;

[Flags]
internal enum CanvasSide
{
    None = 0,
    Left = 1,
    Right = 2,
    Top = 4,
    Bottom = 8,
    Horizontal = Left | Right,
    Vertical = Top | Bottom,
    All = Horizontal | Vertical,
}
