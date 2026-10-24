using Basin;
using Basin.Capabilities;
using Basin.Scene;

namespace LunaWm;

internal sealed class KeyboardSurface : IDisposable
{
    private readonly ChromeSurface _chrome;

    public KeyboardSurface(SceneTree parent, IUIHost host, UISurfaceIndex index, KeyboardModel model)
    {
        Frame = new SceneTransform(parent);
        Model = model;
        View = new KeyboardView { Model = model };
        _chrome = new ChromeSurface(Frame, host, index, View, "Keyboard") { Enabled = false, InputEnabled = false };
    }

    public SceneTransform Frame { get; }

    public KeyboardModel Model { get; }

    public KeyboardView View { get; }

    public ChromeSurface Chrome => _chrome;

    public int Height { get; set; }

    public bool Shown { get; set; }

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
