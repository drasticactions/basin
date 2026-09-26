namespace Basin.Effects;

[Flags]
public enum CanvasStepSides
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
