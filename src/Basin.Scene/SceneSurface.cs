namespace Basin.Scene;

/// <summary>
/// Shows a Wayland surface and its subsurfaces in the scene, and follows each commit. It destroys itself when the surface is destroyed.
/// </summary>
public sealed class SceneSurface
{
    private readonly List<SceneSurface> _childScenes = [];
    private readonly SceneTree _below;
    private readonly SceneBuffer _content;
    private readonly SceneTree _above;

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneSurface"/> class and shows the current state of the surface.
    /// </summary>
    /// <param name="parent">The tree to add the surface tree to.</param>
    /// <param name="surface">The surface to show.</param>
    public SceneSurface(SceneTree parent, Surface surface)
    {
        Surface = surface;
        Tree = new SceneTree(parent);
        _below = new SceneTree(Tree);
        _content = new SceneBuffer(Tree) { InputSurface = surface };
        _above = new SceneTree(Tree);

        surface.Committed += Reconcile;
        surface.Destroyed += Destroy;
        _owner = Tree.RootOwner();
        _owner?.Register(this);
        Reconcile();
    }

    private readonly Scene? _owner;

    private readonly Pixman.PixmanRegion32 _damageScratch = new();

    /// <summary>
    /// Gets the surface that this node shows.
    /// </summary>
    public Surface Surface { get; }

    /// <summary>
    /// Gets the tree that holds the subsurfaces below, the content and the subsurfaces above. Position this tree to move the surface.
    /// </summary>
    public SceneTree Tree { get; }

    /// <summary>
    /// Gets the buffer node that shows the surface's own buffer and takes its input.
    /// </summary>
    public SceneBuffer Content => _content;

    /// <summary>
    /// Gets or sets a value indicating whether the surface and its subsurfaces take input. Setting it applies to every subsurface.
    /// </summary>
    public bool InputEnabled
    {
        get => _content.InputEnabled;
        set
        {
            _content.InputEnabled = value;
            foreach (var child in _childScenes)
            {
                child.InputEnabled = value;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="Destroy"/> has run.
    /// </summary>
    public bool IsDestroyed { get; private set; }

    /// <summary>
    /// Gets the buffer that shows in place of the client's buffer, or <see langword="null"/> when the client's buffer shows.
    /// </summary>
    public IBuffer? Substitute { get; private set; }

    /// <summary>
    /// Shows a different buffer in place of the client's buffer, for example a converted copy. The substitute shows only while the client has a buffer attached.
    /// </summary>
    /// <param name="buffer">The buffer to show, or <see langword="null"/> to show the client's buffer again.</param>
    public void SetSubstitute(IBuffer? buffer)
    {
        if (IsDestroyed || ReferenceEquals(buffer, Substitute))
        {
            return;
        }

        Substitute = buffer;
        Reconcile();
        _content.NotifyContentChanged();
    }

    /// <summary>
    /// Occurs once, at the end of <see cref="Destroy"/>.
    /// </summary>
    public event Action? Destroyed;

    /// <summary>
    /// Sends the frame-done events of the surface and of each subsurface.
    /// </summary>
    /// <param name="timestampMs">The frame time, in milliseconds.</param>
    public void SendFrameDone(uint timestampMs)
    {
        Surface.SendFrameDone(timestampMs);
        foreach (var child in _childScenes)
        {
            child.SendFrameDone(timestampMs);
        }
    }

    /// <summary>
    /// Stops following the surface, destroys the subsurface nodes and <see cref="Tree"/>, then raises <see cref="Destroyed"/>. A second call does nothing.
    /// </summary>
    public void Destroy()
    {
        if (IsDestroyed)
        {
            return;
        }

        IsDestroyed = true;
        Surface.Committed -= Reconcile;
        Surface.Destroyed -= Destroy;
        foreach (var child in _childScenes.ToArray())
        {
            child.Destroy();
        }

        _childScenes.Clear();
        _damageScratch.Dispose();
        (_owner ?? Tree.RootOwner())?.SurfaceBackdrop?.Forget(_content);
        _owner?.Unregister(this);
        Tree.Destroy();
        Destroyed?.Invoke();
    }

    internal void ApplyAppearance()
    {
        if (IsDestroyed)
        {
            return;
        }

        var appearance = (_owner ?? Tree.RootOwner())?.Appearance;
        if (appearance is null)
        {
            Tree.Alpha = 1f;
            _content.VisibleBox = null;
            return;
        }

        Tree.Alpha = (float)appearance.OpacityOf(Surface);
        _content.VisibleBox = appearance.TryVisibleRegion(Surface, out var region) ? VisibleBoxFor(region) : null;
    }

    internal void ApplyBackdrop()
    {
        if (IsDestroyed)
        {
            return;
        }

        _content.ApplySurfaceBackdrop((_owner ?? Tree.RootOwner())?.SurfaceBackdrop, Surface);
    }

    private Box? VisibleBoxFor(Pixman.PixmanRegion32 region)
    {
        var state = Surface.Current;
        if (state.Buffer is not { } buffer || state.Width <= 0 || state.Height <= 0 ||
            state.Transform != OutputTransform.Normal || state.ViewportSourceWidth >= 0)
        {
            return null;
        }

        var extents = region.Extents;
        var scaleX = state.Width / (double)buffer.Width;
        var scaleY = state.Height / (double)buffer.Height;
        var x1 = Math.Max(0, (int)Math.Floor(extents.X1 * scaleX));
        var y1 = Math.Max(0, (int)Math.Floor(extents.Y1 * scaleY));
        var x2 = Math.Min(state.Width, (int)Math.Ceiling(extents.X2 * scaleX));
        var y2 = Math.Min(state.Height, (int)Math.Ceiling(extents.Y2 * scaleY));
        return new Box(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
    }

    private void Reconcile()
    {
        if (IsDestroyed)
        {
            return;
        }

        var state = Surface.Current;
        var shown = state.Buffer is null ? null : Substitute ?? state.Buffer;
        _content.SetBuffer(shown);
        _content.FollowFenceOf(Surface);
        _content.SourceBox = state.ViewportSourceWidth >= 0 && state.ViewportSourceHeight >= 0
            ? new FBox(
                state.ViewportSourceX * state.Scale,
                state.ViewportSourceY * state.Scale,
                state.ViewportSourceWidth * state.Scale,
                state.ViewportSourceHeight * state.Scale)
            : default;
        _content.DestinationWidth = state.Width;
        _content.DestinationHeight = state.Height;

        _content.IsOpaque = shown is not null &&
            (shown.Format.IsOpaque() ||
             (Substitute is null && state.HasOpaque &&
              state.Opaque.Contains(new Pixman.PixmanBox32(0, 0, state.Width, state.Height)) == Pixman.PixmanRegionOverlap.In));
        _content.SetOpaqueRegion(!_content.IsOpaque && Substitute is null && state.Buffer is not null && state.HasOpaque ? state.Opaque : null);

        _damageScratch.Copy(state.SurfaceDamage);
        _damageScratch.UnionWith(state.BufferDamage);
        var damageRects = state.SurfaceDamageRects;
        damageRects.Add(in state.BufferDamageRects);
        _content.NotifyContentChanged(_damageScratch, in damageRects);
        ApplyAppearance();
        ApplyBackdrop();
        ReconcileChildren(Surface.SubsurfacesBelow, _below);
        ReconcileChildren(Surface.SubsurfacesAbove, _above);
        if (state.FrameCallbacks.Count > 0 || state.FrameResources.Count > 0)
        {
            Tree.OwnerIfVisibleOrHeld()?.NotifyFrameRequested();
        }
    }

    private void ReconcileChildren(List<Subsurface> subsurfaces, SceneTree container)
    {
        for (var i = _childScenes.Count - 1; i >= 0; i--)
        {
            var child = _childScenes[i];
            if (child.Tree.Parent == container &&
                (child.Surface.SubsurfaceRole is not { } role || !subsurfaces.Contains(role)))
            {
                _childScenes.RemoveAt(i);
                child.Destroy();
            }
        }

        for (var i = 0; i < subsurfaces.Count; i++)
        {
            var subsurface = subsurfaces[i];
            var scene = FindChild(subsurface.Surface);
            if (scene is null)
            {
                scene = new SceneSurface(container, subsurface.Surface) { InputEnabled = InputEnabled };
                _childScenes.Add(scene);
            }
            else if (scene.Tree.Parent != container)
            {
                scene.Tree.Reparent(container);
            }

            scene.Tree.SetPosition(subsurface.X, subsurface.Y);
            scene.Tree.RaiseToTop();
        }
    }

    private SceneSurface? FindChild(Surface surface)
    {
        for (var i = 0; i < _childScenes.Count; i++)
        {
            if (ReferenceEquals(_childScenes[i].Surface, surface))
            {
                return _childScenes[i];
            }
        }

        return null;
    }
}
