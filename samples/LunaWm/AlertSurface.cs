using Basin;
using Basin.Capabilities;
using Basin.Scene;

namespace LunaWm;

internal sealed class AlertSurface : IDisposable
{
    public const int Height = 240;

    private readonly ChromeSurface _chrome;

    public AlertSurface(SceneTree parent, IUIHost host, UISurfaceIndex index, AlertModel model)
    {
        Frame = new SceneTransform(parent);
        Model = model;
        View = new AlertView { DataContext = model };
        _chrome = new ChromeSurface(Frame, host, index, View, "Alert") { Enabled = false };
    }

    public SceneTransform Frame { get; }

    public AlertModel Model { get; }

    public AlertView View { get; }

    public ChromeSurface Chrome => _chrome;

    public bool Shown { get; set; }

    public Motion Fade;

    public void Dispose()
    {
        _chrome.Dispose();
        if (!Frame.IsDestroyed)
        {
            Frame.Destroy();
        }
    }
}
