using Basin.Scene;

namespace Basin.Shell.Nested;

/// <summary>
/// The scene trees that the shell stacks its content in, from the bottom to the top.
/// Each later layer draws above each earlier layer.
/// </summary>
public sealed class ShellLayers
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShellLayers"/> class. It creates every layer as a child of <paramref name="root"/>, in stacking order.
    /// </summary>
    /// <param name="root">The scene tree that holds the layers.</param>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Gets the bottom layer, which holds the background color.
    /// </summary>
    public SceneTree Background { get; }

    /// <summary>
    /// Gets the layer below normal windows, for consumer content such as desktop icons.
    /// </summary>
    public SceneTree Bottom { get; }

    /// <summary>
    /// Gets the layer of windows that are not above, not fullscreen and not minimized.
    /// </summary>
    public SceneTree Normal { get; }

    /// <summary>
    /// Gets the layer of windows that are set to stay above the others.
    /// </summary>
    public SceneTree Above { get; }

    /// <summary>
    /// Gets the layer where the consumer puts its panel strips.
    /// </summary>
    public SceneTree Panel { get; }

    /// <summary>
    /// Gets the layer of fullscreen windows. It draws above the panels.
    /// </summary>
    public SceneTree Fullscreen { get; }

    /// <summary>
    /// Gets the layer of popups, window menus and unmanaged surfaces.
    /// </summary>
    public SceneTree Menu { get; }

    /// <summary>
    /// Gets the top layer, for the window switcher.
    /// </summary>
    public SceneTree Switcher { get; }
}
