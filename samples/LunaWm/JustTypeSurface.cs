using Basin;
using Basin.Capabilities;
using Basin.Scene;

namespace LunaWm;

internal sealed class JustTypeSurface : IDisposable
{
    private readonly ChromeSurface _chrome;

    public JustTypeSurface(SceneTree parent, IUIHost host, UISurfaceIndex index, JustTypeModel model)
    {
        Frame = new SceneTransform(parent);
        Model = model;
        View = new JustTypeView { DataContext = model };
        _chrome = new ChromeSurface(Frame, host, index, View, "Just type") { Enabled = false };
    }

    public SceneTransform Frame { get; }

    public JustTypeModel Model { get; }

    public JustTypeView View { get; }

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
