using Basin;
using Basin.Capabilities;
using Basin.Scene;

namespace LunaWm;

internal sealed class MenuSurface : IDisposable
{
    private readonly ChromeSurface _chrome;

    public MenuSurface(SceneTree parent, IUIHost host, UISurfaceIndex index)
    {
        Frame = new SceneTransform(parent);
        Model = new SystemMenuModel();
        View = new SystemMenuView { DataContext = Model };
        _chrome = new ChromeSurface(Frame, host, index, View, "Menu") { Enabled = false };
    }

    public SceneTransform Frame { get; }

    public SystemMenuModel Model { get; }

    public SystemMenuView View { get; }

    public ChromeSurface Chrome => _chrome;

    public string Kind { get; set; } = "";

    public bool Open { get; set; }

    public Motion Fade;

    public bool Place(in Box box, double scale) => _chrome.Place(box, scale);

    public void Dispose()
    {
        _chrome.Dispose();
        if (!Frame.IsDestroyed)
        {
            Frame.Destroy();
        }
    }
}
