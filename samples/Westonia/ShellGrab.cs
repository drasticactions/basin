using Basin;
using Basin.Shell.Xdg;

namespace Westonia;

internal sealed class ShellGrab
{
    public ShellGrabKind Kind { get; set; }

    public ShellWindow? Window { get; set; }

    public ResizeEdges Edges { get; set; }

    public double StartX { get; set; }

    public double StartY { get; set; }

    public int OriginX { get; set; }

    public int OriginY { get; set; }

    public int OriginWidth { get; set; }

    public int OriginHeight { get; set; }

    public bool ClientInitiated { get; set; }

    public bool Active => Kind != ShellGrabKind.None && Window is not null;

    public void Clear()
    {
        Kind = ShellGrabKind.None;
        Window = null;
        Edges = ResizeEdges.None;
        ClientInitiated = false;
    }
}
