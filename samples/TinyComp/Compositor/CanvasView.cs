using Basin.Effects;
using Basin.Scene;

namespace TinyComp;

internal sealed class CanvasView
{
    public CanvasWarp Left { get; } = new();

    public CanvasWarp Right { get; } = new(1);

    public SceneMesh? Grid { get; set; }

    public CanvasGridSource? GridSource { get; set; }

    public CanvasSetting Settings { get; set; } = CanvasSetting.Defaults;

    public int Generation { get; set; }

    public bool Enabled { get; set; }

    public bool IsIdentity => Left.IsIdentity && Right.IsIdentity;

    public double ToScreen(double canvasX)
    {
        if (Left.ContainsCanvas(canvasX))
        {
            return Left.ToScreen(canvasX);
        }

        if (Right.ContainsCanvas(canvasX))
        {
            return Right.ToScreen(canvasX);
        }

        return canvasX;
    }

    public double ToCanvas(double screenX)
    {
        if (Left.ContainsScreen(screenX))
        {
            return Left.ToCanvas(screenX);
        }

        if (Right.ContainsScreen(screenX))
        {
            return Right.ToCanvas(screenX);
        }

        return screenX;
    }

    public double ToScreenY(double canvasX, double canvasY)
    {
        if (Left.ContainsCanvas(canvasX))
        {
            return Left.ToScreenY(canvasX, canvasY);
        }

        if (Right.ContainsCanvas(canvasX))
        {
            return Right.ToScreenY(canvasX, canvasY);
        }

        return canvasY;
    }

    public double ToCanvasY(double screenX, double screenY)
    {
        if (Left.ContainsScreen(screenX))
        {
            return Left.ToCanvasY(screenX, screenY);
        }

        if (Right.ContainsScreen(screenX))
        {
            return Right.ToCanvasY(screenX, screenY);
        }

        return screenY;
    }

    public bool ContainsCanvas(double canvasX) => Left.ContainsCanvas(canvasX) || Right.ContainsCanvas(canvasX);
}
