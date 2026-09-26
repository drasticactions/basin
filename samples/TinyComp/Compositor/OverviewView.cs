using Basin.Effects;
using Basin.Scene;
using Basin.Seat;

namespace TinyComp;

internal sealed class OverviewView
{
    public OverviewView()
    {
        Full = new CanvasWarpTransform { Left = FullLeft, Right = FullRight, Top = FullTop, Bottom = FullBottom };
    }

    public CanvasMotion Progress { get; } = new();

    public double Value { get; set; }

    public bool Open { get; set; }

    public bool Tracking { get; set; }

    public bool Active => Open || Value > 0 || Progress.IsRunning || Tracking;

    public SceneTree? ShelfTree { get; set; }

    public List<TinyComp.IGrabTarget> Shelved { get; } = [];

    public HotCorner Corner { get; } = new();

    public OverviewSetting Settings { get; set; } = OverviewSetting.Defaults;

    public CanvasWarp FullLeft { get; } = new();

    public CanvasWarp FullRight { get; } = new(1);

    public CanvasWarp FullTop { get; } = new();

    public CanvasWarp FullBottom { get; } = new(1);

    public CanvasWarpTransform Full { get; }

    public CanvasSide Sides { get; set; }

    public CanvasSide BorderSides { get; set; }

    public CanvasSide WallSides { get; set; }

    public double CenterX { get; set; }

    public double CenterY { get; set; }

    public int[] ShelfScreen { get; } = new int[4];

    public int[] SlopeScreen { get; } = new int[4];

    public double[] OuterCanvas { get; } = [double.NaN, double.NaN, double.NaN, double.NaN];

    public double[] OuterSeen { get; } = [double.NaN, double.NaN, double.NaN, double.NaN];

    public OverviewSide[] FullSides { get; } = new OverviewSide[4];

    public SceneTransform? BackgroundZoom { get; set; }

    public SceneTransform? BottomZoom { get; set; }

    public string? Reported { get; set; }

    public bool Steps { get; set; }

    public CanvasStepMap Step { get; } = new();

    public CanvasStepMap StepFull { get; } = new();

    public CanvasStepMap StepSeen { get; } = new();

    public bool StepSeenValid { get; set; }

    public CanvasStepSurfaceSource WallSource { get; } = new(CanvasStepSurface.Walls);

    public CanvasStepSurfaceSource FloorSource { get; } = new(CanvasStepSurface.Floor);

    public CanvasStepSource StepSource { get; } = new() { MinLineSpacing = 8 };

    public SceneMesh? FloorMesh { get; set; }

    public SceneMesh? WallMesh { get; set; }

    public SceneMesh? StepMesh { get; set; }
}
