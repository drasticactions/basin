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

    public CanvasScale Scale { get; } = new();

    public CanvasWindowMode Mode { get; set; }

    public bool Scales => Mode == CanvasWindowMode.Scale;

    public bool Terraces => Mode == CanvasWindowMode.Terrace;

    public CanvasWindowMode? ModeOverride { get; set; }

    public double[] ShelfOverride { get; } = [double.NaN, double.NaN, double.NaN, double.NaN];

    public double[] ShelfTarget { get; } = [double.NaN, double.NaN, double.NaN, double.NaN];

    public CanvasMotion[] ShelfMotion { get; } = [new(), new(), new(), new()];

    public bool ShelfAnimating =>
        ShelfMotion[0].IsRunning || ShelfMotion[1].IsRunning || ShelfMotion[2].IsRunning || ShelfMotion[3].IsRunning;

    public SceneMesh? Grid { get; set; }

    public CanvasGridSource? GridSource { get; set; }

    public CanvasSetting Settings { get; set; } = CanvasSetting.Defaults;

    public int Generation { get; set; }

    public bool Enabled { get; set; }

    public bool Overview { get; set; }

    public CanvasStepMap? Step { get; set; }

    public CanvasView()
    {
        Map = new CanvasWarpTransform { Left = Left, Right = Right, Top = Top, Bottom = Bottom };
    }

    public bool IsIdentity =>
        Left.IsIdentity && Right.IsIdentity && Top.IsIdentity && Bottom.IsIdentity && Map.ViewScale == 1.0 &&
        (Step is null || Step.IsIdentity);

    public (double X, double Y) ToScreenPoint(double canvasX, double canvasY) =>
        Step is { } step ? step.ToScreen(canvasX, canvasY) : Map.ToScreenPoint(canvasX, canvasY);

    public (double X, double Y) ToCanvasPoint(double screenX, double screenY) =>
        Step is { } step ? step.ToCanvasNearest(screenX, screenY, out _) : Map.ToCanvasPoint(screenX, screenY);
}
