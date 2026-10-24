namespace Basin.Scene;

/// <summary>
/// Creates the standard stacking layers under a root, from bottom to top: background, bottom, windows, top, overlay, lock, feedback and handoff.
/// </summary>
public sealed class SceneLayers
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SceneLayers"/> class and adds one tree per layer to the root, in stacking order.
    /// </summary>
    /// <param name="root">The tree to add the layers to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
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
        Handoff = new SceneTree(root) { ExcludeFromScanout = true };
    }

    /// <summary>
    /// Gets the lowest layer, for wallpapers.
    /// </summary>
    public SceneTree Background { get; }

    /// <summary>
    /// Gets the layer below windows, for layer-shell bottom surfaces.
    /// </summary>
    public SceneTree Bottom { get; }

    /// <summary>
    /// Gets the layer for toplevel windows.
    /// </summary>
    public SceneTree Windows { get; }

    /// <summary>
    /// Gets the layer above windows, for panels and layer-shell top surfaces.
    /// </summary>
    public SceneTree Top { get; }

    /// <summary>
    /// Gets the layer for layer-shell overlay surfaces.
    /// </summary>
    public SceneTree Overlay { get; }

    /// <summary>
    /// Gets the layer for the session lock surfaces. It stays enabled when <see cref="SetLocked"/> hides the other layers.
    /// </summary>
    public SceneTree Lock { get; }

    /// <summary>
    /// Gets the layer for drag icons and similar feedback. It is excluded from scanout.
    /// </summary>
    public SceneTree Feedback { get; }

    /// <summary>
    /// Gets the top layer, for the frame that a display handoff inherits. It is excluded from scanout.
    /// </summary>
    public SceneTree Handoff { get; }

    /// <summary>
    /// Hides or shows the background, bottom, windows, top and overlay layers. The lock, feedback and handoff layers do not change.
    /// </summary>
    /// <param name="locked"><see langword="true"/> to hide the layers that the lock must cover.</param>
    public void SetLocked(bool locked)
    {
        Background.Enabled = !locked;
        Bottom.Enabled = !locked;
        Windows.Enabled = !locked;
        Top.Enabled = !locked;
        Overlay.Enabled = !locked;
    }
}
