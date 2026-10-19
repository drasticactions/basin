using Basin.Scene;

namespace System7Wm;

internal sealed class System7Layers
{
    public System7Layers(SceneTree root)
    {
        Root = root;
        Hidden = new SceneTree(root) { Enabled = false };
        Pattern = new SceneTree(root);
        LayerBackground = new SceneTree(root);
        Icons = new SceneTree(root);
        LayerBottom = new SceneTree(root);
        Windows = new SceneTree(root);
        Outlines = new SceneTree(root);
        Alerts = new SceneTree(root);
        LayerTop = new SceneTree(root);
        MenuBar = new SceneTree(root);
        Popups = new SceneTree(root);
        LayerOverlay = new SceneTree(root);
        Inspector = new SceneTree(root);
        Lock = new SceneTree(root);
        Cursor = new SceneTree(root);
    }

    public SceneTree Root { get; }

    public SceneTree Hidden { get; }

    public SceneTree Pattern { get; }

    public SceneTree LayerBackground { get; }

    public SceneTree Icons { get; }

    public SceneTree LayerBottom { get; }

    public SceneTree Windows { get; }

    public SceneTree Outlines { get; }

    public SceneTree Alerts { get; }

    public SceneTree LayerTop { get; }

    public SceneTree MenuBar { get; }

    public SceneTree Popups { get; }

    public SceneTree LayerOverlay { get; }

    public SceneTree Inspector { get; }

    public SceneTree Lock { get; }

    public SceneTree Cursor { get; }

    public void SetLocked(bool locked)
    {
        Pattern.Enabled = !locked;
        LayerBackground.Enabled = !locked;
        Icons.Enabled = !locked;
        LayerBottom.Enabled = !locked;
        Windows.Enabled = !locked;
        Outlines.Enabled = !locked;
        Alerts.Enabled = !locked;
        LayerTop.Enabled = !locked;
        MenuBar.Enabled = !locked;
        Popups.Enabled = !locked;
        LayerOverlay.Enabled = !locked;
        Inspector.Enabled = !locked;
    }
}
