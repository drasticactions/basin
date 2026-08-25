using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.UI.Skia;
using SkiaSharp;
using static EightWm.EightWmLog;

namespace EightWm;

internal sealed class ChromeSurface : IDisposable
{
    private readonly OutputUISurface _anchored;
    private bool _drawing;
    private bool _faulted;

    public ChromeSurface(IUIHost host, SceneTree parent)
    {
        _anchored = new OutputUISurface(parent, host) { InputEnabled = false, AutoEnable = false, Enabled = true };
    }

    public SceneBuffer Node => _anchored.Node.Node;

    public int Width => _anchored.Node.Width;

    public int Height => _anchored.Node.Height;

    public bool Enabled
    {
        get => _anchored.Enabled;
        set => _anchored.Enabled = value;
    }

    public void SetPosition(int x, int y) => _anchored.Node.SetPosition(x, y);

    public bool Configure(int width, int height, double scale) =>
        _anchored.Node.Configure(width, height, scale);

    public bool Place(in Box box, double scale) => _anchored.PlaceAt(box, scale);

    public SKCanvas? BeginDraw()
    {
        if (_faulted || _anchored.IsFaulted || _anchored.Surface is not ISkiaUISurface surface || _drawing)
        {
            return null;
        }

        try
        {
            var canvas = surface.BeginDraw();
            _drawing = true;
            canvas.Clear(SKColors.Transparent);
            return canvas;
        }
        catch (InvalidOperationException error)
        {
            _faulted = true;
            Log.Warn($"eight-wm chrome surface faulted: {error.Message}");
            return null;
        }
    }

    public void EndDraw()
    {
        if (!_drawing || _anchored.Surface is not ISkiaUISurface surface)
        {
            return;
        }

        _drawing = false;
        surface.EndDraw();
        _anchored.Publish();
    }

    public double Scale => _anchored.Scale;

    public void Dispose()
    {
        if (_drawing && _anchored.Surface is ISkiaUISurface surface)
        {
            surface.EndDraw();
            _drawing = false;
        }

        _anchored.Dispose();
    }
}
