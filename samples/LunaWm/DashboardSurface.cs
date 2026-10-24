using Basin;
using Basin.Capabilities;
using Basin.Scene;

namespace LunaWm;

internal sealed class DashboardSurface : IDisposable
{
    private readonly ChromeSurface _chrome;

    public DashboardSurface(SceneTree parent, IUIHost host, UISurfaceIndex index, DashboardModel model)
    {
        Frame = new SceneTransform(parent);
        Model = model;
        View = new DashboardView { DataContext = model };
        _chrome = new ChromeSurface(Frame, host, index, View, "Dashboard") { Enabled = false };
    }

    public SceneTransform Frame { get; }

    public DashboardModel Model { get; }

    public DashboardView View { get; }

    public ChromeSurface Chrome => _chrome;

    public bool Open { get; set; }

    public Motion Snap;

    public void Dispose()
    {
        _chrome.Dispose();
        if (!Frame.IsDestroyed)
        {
            Frame.Destroy();
        }
    }
}
