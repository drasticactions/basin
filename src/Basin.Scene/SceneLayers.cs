namespace Basin.Scene;

public sealed class SceneLayers
{
    public SceneLayers(SceneTree root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Background = new SceneTree(root);
        Bottom = new SceneTree(root);
        Windows = new SceneTree(root);
        Top = new SceneTree(root);
        Overlay = new SceneTree(root);
        Lock = new SceneTree(root);
        Feedback = new SceneTree(root) { ExcludeFromScanout = true };
    }

    public SceneTree Background { get; }

    public SceneTree Bottom { get; }

    public SceneTree Windows { get; }

    public SceneTree Top { get; }

    public SceneTree Overlay { get; }

    public SceneTree Lock { get; }

    public SceneTree Feedback { get; }

    public void SetLocked(bool locked)
    {
        Background.Enabled = !locked;
        Bottom.Enabled = !locked;
        Windows.Enabled = !locked;
        Top.Enabled = !locked;
        Overlay.Enabled = !locked;
    }
}
