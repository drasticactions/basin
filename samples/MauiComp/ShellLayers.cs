using Basin.Scene;

namespace MauiComp;

internal sealed class ShellLayers
{
    public ShellLayers(SceneTree root)
    {
        Root = root;
        Background = new SceneTree(root);
        Windows = new SceneTree(root);
        Panel = new SceneTree(root);
        Fullscreen = new SceneTree(root);
        Overlay = new SceneTree(root);
    }

    public SceneTree Root { get; }

    public SceneTree Background { get; }

    public SceneTree Windows { get; }

    public SceneTree Panel { get; }

    public SceneTree Fullscreen { get; }

    public SceneTree Overlay { get; }
}
