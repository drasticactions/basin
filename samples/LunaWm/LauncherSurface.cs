using Basin;
using Basin.Capabilities;
using Basin.Scene;

namespace LunaWm;

internal sealed class LauncherSurface : IDisposable
{
    private readonly ChromeSurface _chrome;

    public LauncherSurface(SceneTree parent, IUIHost host, UISurfaceIndex index, LauncherModel model)
    {
        Frame = new SceneTransform(parent);
        Model = model;
        View = new LauncherView { DataContext = model };
        _chrome = new ChromeSurface(Frame, host, index, View, "Launcher") { Enabled = false };
    }

    public SceneTransform Frame { get; }

    public LauncherModel Model { get; }

    public LauncherView View { get; }

    public ChromeSurface Chrome => _chrome;

    public bool Open { get; set; }

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
