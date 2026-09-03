namespace Basin.Scene;

public sealed class SceneSurface
{
    private readonly List<SceneSurface> _childScenes = [];
    private readonly SceneTree _below;
    private readonly SceneBuffer _content;
    private readonly SceneTree _above;

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

    public Surface Surface { get; }

    public SceneTree Tree { get; }

    public SceneBuffer Content => _content;

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

    public bool IsDestroyed { get; private set; }

    public event Action? Destroyed;

    public void SendFrameDone(uint timestampMs)
    {
        Surface.SendFrameDone(timestampMs);
        foreach (var child in _childScenes)
        {
            child.SendFrameDone(timestampMs);
        }
    }

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
        _content.SetBuffer(state.Buffer);
        _content.AcquireFenceFd = Surface.AcquireFenceFd;
        _content.SourceBox = state.ViewportSourceWidth >= 0 && state.ViewportSourceHeight >= 0
            ? new FBox(
                state.ViewportSourceX,
                state.ViewportSourceY,
                state.ViewportSourceWidth,
                state.ViewportSourceHeight)
            : default;
        _content.DestinationWidth = state.Width;
        _content.DestinationHeight = state.Height;

        _content.IsOpaque = state.Buffer is not null &&
            (state.Buffer.Format.IsOpaque() ||
             (state.HasOpaque &&
              state.Opaque.Contains(new Pixman.PixmanBox32(0, 0, state.Width, state.Height)) == Pixman.PixmanRegionOverlap.In));
        _content.SetOpaqueRegion(!_content.IsOpaque && state.Buffer is not null && state.HasOpaque ? state.Opaque : null);

        _damageScratch.Copy(state.SurfaceDamage);
        _damageScratch.UnionWith(state.BufferDamage);
        var damageRects = state.SurfaceDamageRects;
        damageRects.Add(in state.BufferDamageRects);
        _content.NotifyContentChanged(_damageScratch, in damageRects);
        ApplyAppearance();
        ReconcileChildren(Surface.SubsurfacesBelow, _below);
        ReconcileChildren(Surface.SubsurfacesAbove, _above);
        if (state.FrameCallbacks.Count > 0 || state.FrameResources.Count > 0)
        {
            Tree.RootOwner()?.NotifyFrameRequested();
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
