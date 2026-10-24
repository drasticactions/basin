using Basin;
using Basin.Capabilities;
using Basin.Scene;

namespace LunaWm;

internal sealed class StatusBar : IDisposable
{
    private readonly ChromeSurface _chrome;

    public StatusBar(SceneTree parent, IUIHost host, UISurfaceIndex index)
    {
        Model = new StatusBarModel();
        View = new StatusBarView { DataContext = Model };
        _chrome = new ChromeSurface(parent, host, index, View, "Status bar") { Enabled = true };
    }

    public StatusBarModel Model { get; }

    public StatusBarView View { get; }

    public ChromeSurface Chrome => _chrome;

    public Motion Fade;

    public bool Place(int width, double scale) => _chrome.Place(new Box(0, 0, Math.Max(1, width), LunaView.StatusBarHeight), scale);

    public void Dispose() => _chrome.Dispose();
}
