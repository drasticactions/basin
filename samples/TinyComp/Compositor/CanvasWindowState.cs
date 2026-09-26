using Basin;
using Basin.Effects;
using Basin.Scene;
using Basin.Shell.Xdg;

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

    public RenderTransform Placement { get; set; } = RenderTransform.Identity;

    public double Scale { get; set; } = 1.0;

    public bool Dragging { get; set; }

    public double DragGrabX { get; set; }

    public double DragGrabY { get; set; }

    public double DragCursorX { get; set; }

    public double DragCursorY { get; set; }

    public double DragStartX { get; set; }

    public double DragStartY { get; set; }

    public double DragScaleCorrection { get; set; }

    public (double X, double Y)? Anchor { get; set; }

    public bool Resizing { get; set; }

    public Box ResizeStart { get; set; }

    public double ResizeScale { get; set; } = 1.0;

    public bool ResizeFixedLeft { get; set; }

    public bool ResizeFixedTop { get; set; }

    public ResizeEdges ResizeEdges { get; set; }

    public double ResizeScreenX { get; set; }

    public double ResizeScreenY { get; set; }

    public CanvasMotion Blend { get; } = new();

    public double OfferedScale { get; set; } = 1.0;

    public (int Width, int Height) OfferSize { get; set; }

    public bool OfferRefused { get; set; }

    public RenderTransform BlendFrom { get; set; } = RenderTransform.Identity;
}
