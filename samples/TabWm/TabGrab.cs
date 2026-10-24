using Basin.Shell.Xdg;

namespace TabWm;

internal sealed class TabGrab
{
    public TabGrabKind Kind { get; set; }

    public TabWindow? Window { get; set; }

    public Tab? Tab { get; set; }

    public ResizeEdges Edges { get; set; }

    public double StartX { get; set; }

    public double StartY { get; set; }

    public int OriginX { get; set; }

    public int OriginY { get; set; }

    public int OriginWidth { get; set; }

    public int OriginHeight { get; set; }

    public Tab? Hover { get; set; }

    public bool Active => Kind != TabGrabKind.None;

    public void Clear()
    {
        Kind = TabGrabKind.None;
        Window = null;
        Tab = null;
        Hover = null;
        Edges = ResizeEdges.None;
    }
}
