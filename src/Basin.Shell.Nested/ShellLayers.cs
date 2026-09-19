using Basin.Scene;

namespace Basin.Shell.Nested;

public sealed class ShellLayers
{
    public ShellLayers(SceneTree root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Background = new SceneTree(root);
        Bottom = new SceneTree(root);
        Normal = new SceneTree(root);
        Above = new SceneTree(root);
        Panel = new SceneTree(root);
        Fullscreen = new SceneTree(root);
        Menu = new SceneTree(root);
        Switcher = new SceneTree(root);
    }

    public SceneTree Background { get; }

    public SceneTree Bottom { get; }

    public SceneTree Normal { get; }

    public SceneTree Above { get; }

    public SceneTree Panel { get; }

    public SceneTree Fullscreen { get; }

    public SceneTree Menu { get; }

    public SceneTree Switcher { get; }
}
