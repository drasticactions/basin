using Basin;
using Basin.Capabilities;
using Basin.Scene;

namespace LunaWm;

internal sealed class DockSurface : IDisposable
{
    private readonly ChromeSurface _chrome;

    public DockSurface(SceneTree parent, IUIHost host, UISurfaceIndex index, QuickLaunchModel model)
    {
        Frame = new SceneTransform(parent);
        Model = model;
        View = new QuickLaunchView { DataContext = model };
        _chrome = new ChromeSurface(Frame, host, index, View, "Quick launch") { Enabled = false };
    }

    public SceneTransform Frame { get; }

    public QuickLaunchModel Model { get; }

    public QuickLaunchView View { get; }

    public ChromeSurface Chrome => _chrome;

    public bool Shown { get; set; }

    public bool Following { get; set; }

    public Motion Slide;

    public void Dispose()
    {
        _chrome.Dispose();
        if (!Frame.IsDestroyed)
        {
            Frame.Destroy();
        }
    }
}
