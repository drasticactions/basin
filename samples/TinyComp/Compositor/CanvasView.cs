using Basin.Effects;
using Basin.Scene;

namespace TinyComp;

internal sealed class CanvasView
{
    public CanvasWarp Left { get; } = new();

    public CanvasWarp Right { get; } = new(1);

    public CanvasWarp Top { get; } = new();

    public CanvasWarp Bottom { get; } = new(1);

    public CanvasWarpTransform Map { get; }

    public SceneMesh? Grid { get; set; }

    public CanvasGridSource? GridSource { get; set; }

    public CanvasSetting Settings { get; set; } = CanvasSetting.Defaults;

    public int Generation { get; set; }

    public bool Enabled { get; set; }

    public CanvasView()
    {
        Map = new CanvasWarpTransform { Left = Left, Right = Right, Top = Top, Bottom = Bottom };
    }

    public bool IsIdentity => Left.IsIdentity && Right.IsIdentity && Top.IsIdentity && Bottom.IsIdentity;

    public (double X, double Y) ToScreenPoint(double canvasX, double canvasY) => Map.ToScreenPoint(canvasX, canvasY);

    public (double X, double Y) ToCanvasPoint(double screenX, double screenY) => Map.ToCanvasPoint(screenX, screenY);
}
