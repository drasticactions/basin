using Basin.Scene;

namespace TabWm;

internal sealed class TabLayers
{
    private bool _locked;

    public TabLayers(SceneTree root)
    {
        Root = root;
        Hidden = new SceneTree(root) { Enabled = false };
        Background = new SceneTree(root);
        LayerBackground = new SceneTree(root);
        LayerBottom = new SceneTree(root);
        Tabs = new SceneTree(root);
        NewTabPages = new SceneTree(root);
        Strip = new SceneTree(root);
        LayerTop = new SceneTree(root);
        Launcher = new SceneTree(root);
        Fullscreen = new SceneTree(root);
        InputPanel = new SceneTree(root);
        LayerOverlay = new SceneTree(root);
        Popups = new SceneTree(root);
        Cursor = new SceneTree(root);
        Lock = new SceneTree(root) { Enabled = false };
    }

    public SceneTree Root { get; }

    public SceneTree Hidden { get; }

    public SceneTree Background { get; }

    public SceneTree LayerBackground { get; }

    public SceneTree LayerBottom { get; }

    public SceneTree Tabs { get; }

    public SceneTree NewTabPages { get; }

    public SceneTree Strip { get; }

    public SceneTree LayerTop { get; }

    public SceneTree Launcher { get; }

    public SceneTree Fullscreen { get; }

    public SceneTree InputPanel { get; }

    public SceneTree LayerOverlay { get; }

    public SceneTree Popups { get; }

    public SceneTree Cursor { get; }

    public SceneTree Lock { get; }

    public bool IsLocked => _locked;

    public void SetLocked(bool locked)
    {
        if (_locked == locked)
        {
            return;
        }

        _locked = locked;
        Tabs.Enabled = !locked;
        NewTabPages.Enabled = !locked;
        Strip.Enabled = !locked;
        Launcher.Enabled = !locked;
        Fullscreen.Enabled = !locked;
        LayerTop.Enabled = !locked;
        LayerBottom.Enabled = !locked;
        Popups.Enabled = !locked;
        Lock.Enabled = locked;
        Lock.RaiseToTop();
        Cursor.RaiseToTop();
    }
}
