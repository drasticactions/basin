using Basin.Effects;
using Basin.Scene;

namespace TinyComp;

internal sealed class CanvasWindowState
{
    public CanvasWarpTransform Transform { get; } = new();

    public SceneTransform? Node { get; set; }

    public (int X, int Y)? Home { get; set; }

    public CanvasMotion MotionX { get; } = new();

    public CanvasMotion MotionY { get; } = new();

    public int AppliedGeneration { get; set; } = -1;

    public int AppliedSceneX { get; set; }

    public int AppliedSceneY { get; set; }

    public bool Deformed { get; set; }
}
