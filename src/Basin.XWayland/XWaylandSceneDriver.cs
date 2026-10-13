using Basin.Scene;

namespace Basin.XWayland;

public sealed class XWaylandSceneDriver : IDisposable
{
    private readonly List<Entry> _entries = [];
    private readonly List<XWaylandWm> _wms = [];
    private bool _disposed;

    public Func<XWaylandWindow, SceneTree?>? ManagedParent { get; set; }

    public Func<XWaylandWindow, SceneTree?>? OverrideRedirectParent { get; set; }

    public Func<Box>? CenterArea { get; set; }

    public bool Letterbox { get; set; }

    public bool PositionsManagedWindows { get; set; } = true;

    public IRenderer? ShapeRenderer { get; set; }

    public IAllocator? ShapeAllocator { get; set; }

    private static void Reshape(Entry entry, IRenderer? renderer, IAllocator? allocator)
    {
        var window = entry.Window;
        if (entry.Scene.IsDestroyed)
        {
            return;
        }

        entry.Scene.Content.InputFilter = window.InputShape is not null || window.BoundingShape is not null
            ? entry.InputFilter ??= new XWaylandShapeInputFilter(window)
            : null;
        if (window.BoundingShape is not { } shape || renderer is null
            || window.Surface is not { IsDestroyed: false } surface || surface.Current.Buffer is not { } buffer)
        {
            entry.Scene.SetSubstitute(null);
            if (window.BoundingShape is null)
            {
                entry.Mask?.Dispose();
                entry.Mask = null;
            }

            return;
        }

        entry.Mask ??= new XWaylandShapeMask(renderer, allocator);
        entry.Scene.SetSubstitute(entry.Mask.Render(buffer, shape));
    }

    private static bool AwaitsResizedBuffer(XWaylandWindow window) =>
        window.Surface is { IsDestroyed: false } surface && surface.Current.Buffer is { } buffer
        && (buffer.Width != window.X11Bounds.Width || buffer.Height != window.X11Bounds.Height);

    public SceneFit? FitOf(XWaylandWindow window) => Find(window)?.Fit;

    public SceneTree? ParentOf(XWaylandWindow window) => Find(window)?.Parent;

    public SceneTree? OuterOf(XWaylandWindow window) => Find(window) is { } entry ? entry.Fit?.Root ?? entry.Scene.Tree : null;

    public void Refit(XWaylandWindow window)
    {
        if (Find(window) is { } entry)
        {
            Fit(entry);
        }
    }

    private Entry? Find(XWaylandWindow window)
    {
        foreach (var entry in _entries)
        {
            if (ReferenceEquals(entry.Window, window))
            {
                return entry;
            }
        }

        return null;
    }

    private static void Fit(Entry entry)
    {
        if (entry.Fit is not { } fit || entry.Window.Surface is not { IsDestroyed: false } surface)
        {
            return;
        }

        var window = entry.Window;
        var committed = (surface.Current.Width, surface.Current.Height);
        if ((window.EmulatedMode ?? OverrideRedirectMode(window)) is { } emulated)
        {
            var target = window.OverrideRedirect
                ? new Box(0, 0, committed.Width, committed.Height)
                : new Box(0, 0, window.Width, window.Height);
            fit.Update(
                target, committed, (emulated.Width, emulated.Height),
                window.Letterbox switch
                {
                    SceneFitPolicy.Off => null,
                    SceneFitPolicy.Integer => SceneFitMode.Integer,
                    SceneFitPolicy.Fill => SceneFitMode.Fill,
                    SceneFitPolicy.Stretch => SceneFitMode.Stretch,
                    _ => SceneFitMode.Fit,
                });
            return;
        }

        var state = window.IsFullscreen ? SceneFitState.Fullscreen
            : window.IsMaximized ? SceneFitState.Maximized
            : window.Tiled ? SceneFitState.Tiled
            : SceneFitState.Floating;
        var configured = (window.Width, window.Height);
        fit.Update(new Box(0, 0, window.Width, window.Height), committed, SceneFit.Needed(configured, committed, state, window.Letterbox));
    }

    private static Box? OverrideRedirectMode(XWaylandWindow window)
    {
        if (!window.OverrideRedirect || !window.HonorEmulatedModes)
        {
            return null;
        }

        foreach (var mode in window.EmulatedModes)
        {
            if (mode is { X: 0, Y: 0 } && window.CoversScreen)
            {
                return mode;
            }
        }

        return null;
    }

    public event Action<XWaylandWindow, SceneSurface, bool>? Adopted;

    public event Action<XWaylandWindow, SceneSurface>? Removed;

    public event Action<XWaylandWindow>? ActivationRequested;

    public int Count => _entries.Count;

    public void Attach(XWaylandWm wm)
    {
        ArgumentNullException.ThrowIfNull(wm);
        if (_wms.Contains(wm))
        {
            return;
        }

        _wms.Add(wm);
        wm.WindowMapped += OnManaged;
        wm.OverrideRedirectMapped += OnOverrideRedirect;
        wm.ActivationRequested += OnActivation;
        wm.Disposed += Detach;
    }

    public void Detach(XWaylandWm wm)
    {
        ArgumentNullException.ThrowIfNull(wm);
        if (!_wms.Remove(wm))
        {
            return;
        }

        wm.WindowMapped -= OnManaged;
        wm.OverrideRedirectMapped -= OnOverrideRedirect;
        wm.ActivationRequested -= OnActivation;
        wm.Disposed -= Detach;
    }

    public SceneSurface? SceneOf(Surface? surface)
    {
        if (surface is null)
        {
            return null;
        }

        foreach (var entry in _entries)
        {
            if (ReferenceEquals(entry.Window.Surface, surface))
            {
                return entry.Scene;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var wm in _wms.ToArray())
        {
            Detach(wm);
        }

        foreach (var entry in _entries.ToArray())
        {
            Release(entry);
        }

        _entries.Clear();
    }

    private void OnManaged(XWaylandWindow window)
    {
        if (_disposed || window.Surface is null)
        {
            return;
        }

        if (CenterArea is { } area && window is { X: 0, Y: 0 })
        {
            var box = area();
            window.Configure(
                box.X + Math.Max(0, (box.Width - window.Width) / 2),
                box.Y + Math.Max(0, (box.Height - window.Height) / 2),
                window.Width,
                window.Height);
        }

        Adopt(window, ManagedParent?.Invoke(window), managed: true);
    }

    private void OnOverrideRedirect(XWaylandWindow window)
    {
        if (_disposed || window.Surface is null)
        {
            return;
        }

        Adopt(window, OverrideRedirectParent?.Invoke(window), managed: false);
    }

    private void OnActivation(XWaylandWindow window) => ActivationRequested?.Invoke(window);

    private void Adopt(XWaylandWindow window, SceneTree? parent, bool managed)
    {
        if (parent is null || parent.IsDestroyed)
        {
            return;
        }

        var fit = Letterbox ? new SceneFit(parent) : null;
        var scene = new SceneSurface(fit?.Content ?? parent, window.Surface!);
        var outer = fit?.Root ?? scene.Tree;
        var positions = !managed || PositionsManagedWindows;
        if (positions)
        {
            outer.SetPosition(window.X, window.Y);
        }

        var entry = new Entry(window, scene) { Fit = fit, Parent = parent };
        entry.Layout = () =>
        {
            if (!outer.IsDestroyed)
            {
                if (positions)
                {
                    outer.SetPosition(window.X, window.Y);
                }

                Fit(entry);
            }
        };
        entry.Refit = () =>
        {
            Fit(entry);
            if (window.BoundingShape is not null || entry.Mask is not null)
            {
                Reshape(entry, ShapeRenderer, ShapeAllocator);
            }
        };
        entry.Reshape = () =>
        {
            if (!AwaitsResizedBuffer(window))
            {
                Reshape(entry, ShapeRenderer, ShapeAllocator);
            }
        };
        window.ShapeChanged += entry.Reshape;
        entry.Drop = () => Remove(entry);
        window.GeometryChanged += entry.Layout;
        window.Surface!.Committed += entry.Refit;
        entry.Surface = window.Surface;
        window.Unmapped += entry.Drop;
        window.Destroyed += entry.Drop;
        _entries.Add(entry);
        if (window.BoundingShape is not null || window.InputShape is not null)
        {
            entry.Reshape();
        }

        Adopted?.Invoke(window, scene, managed);
    }

    private void Remove(Entry entry)
    {
        if (!_entries.Remove(entry))
        {
            return;
        }

        Release(entry);
        Removed?.Invoke(entry.Window, entry.Scene);
    }

    private static void Release(Entry entry)
    {
        entry.Window.GeometryChanged -= entry.Layout;
        entry.Window.ShapeChanged -= entry.Reshape;
        entry.Window.Unmapped -= entry.Drop;
        entry.Window.Destroyed -= entry.Drop;
        if (entry.Surface is { } surface)
        {
            surface.Committed -= entry.Refit;
        }

        if (!entry.Scene.IsDestroyed)
        {
            entry.Scene.Destroy();
        }

        entry.Mask?.Dispose();
        entry.Mask = null;
        entry.Fit?.Destroy();
    }

    private sealed class Entry(XWaylandWindow window, SceneSurface scene)
    {
        public XWaylandWindow Window { get; } = window;

        public SceneSurface Scene { get; } = scene;

        public Action? Layout { get; set; }

        public Action? Drop { get; set; }

        public Action? Refit { get; set; }

        public Action? Reshape { get; set; }

        public XWaylandShapeMask? Mask { get; set; }

        public XWaylandShapeInputFilter? InputFilter { get; set; }

        public SceneFit? Fit { get; init; }

        public SceneTree? Parent { get; init; }

        public Surface? Surface { get; set; }
    }
}
