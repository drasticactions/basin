using Basin;
using Basin.Capabilities;
using Basin.Scene;

namespace LunaWm;

internal sealed class LockSurface : IDisposable
{
    private readonly ChromeSurface _chrome;

    public LockSurface(SceneTree parent, IUIHost host, UISurfaceIndex index, LockScreenModel model)
    {
        Frame = new SceneTransform(parent);
        Model = model;
        View = new LockScreenView { DataContext = model };
        _chrome = new ChromeSurface(Frame, host, index, View, "Lock screen") { Enabled = false };
    }

    public SceneTransform Frame { get; }

    public LockScreenModel Model { get; }

    public LockScreenView View { get; }

    public ChromeSurface Chrome => _chrome;

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
