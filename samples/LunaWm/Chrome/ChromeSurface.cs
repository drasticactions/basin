using Avalonia.Controls;
using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.UI.Avalonia;

namespace LunaWm;

internal sealed class ChromeSurface : IDisposable
{
    private readonly OutputUISurface _surface;

    public ChromeSurface(SceneTree parent, IUIHost host, UISurfaceIndex index, Control content, string name)
    {
        Content = content;
        _surface = new OutputUISurface(parent, host, index) { PreciseDamage = true, AutoEnable = false, AccessibleName = name };
        _surface.Realized += surface => ((AvaloniaUISurface)surface).Content = content;
    }

    public Control Content { get; }

    public SceneBuffer Node => _surface.Node.Node;

    public IUISurface? Surface => _surface.Surface;

    public Box Bounds => _surface.Bounds;

    public int Width => _surface.Node.Width;

    public int Height => _surface.Node.Height;

    public double Scale => _surface.Scale;

    public bool Enabled
    {
        get => _surface.Enabled;
        set => _surface.Enabled = value;
    }

    public bool InputEnabled
    {
        get => _surface.InputEnabled;
        set => _surface.InputEnabled = value;
    }

    public bool Place(in Box box, double scale) => _surface.PlaceAt(box, scale);

    public void Dispose() => _surface.Dispose();
}
