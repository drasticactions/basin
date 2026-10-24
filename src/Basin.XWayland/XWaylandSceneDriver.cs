using Basin.Capabilities;
using Basin.Scene;

namespace Basin.XWayland;

/// <summary>
/// Puts each mapped X11 window into the scene as a <see cref="SceneSurface"/>, follows its geometry and its X Shape, and removes it on unmap. The consumer supplies the parent trees.
/// </summary>
public sealed class XWaylandSceneDriver : IDisposable
{
    private readonly List<Entry> _entries = [];
    private readonly List<XWaylandWm> _wms = [];
    private bool _disposed;

    /// <summary>
    /// Gets or sets the function that chooses the parent tree of a managed window. A <see langword="null"/> function or result leaves the window out of the scene.
    /// </summary>
    public Func<XWaylandWindow, SceneTree?>? ManagedParent { get; set; }

    /// <summary>
    /// Gets or sets the function that chooses the parent tree of an override-redirect window. A <see langword="null"/> function or result leaves the window out of the scene.
    /// </summary>
    public Func<XWaylandWindow, SceneTree?>? OverrideRedirectParent { get; set; }

    /// <summary>
    /// Gets or sets the function that gives the area, in layout coordinates, to center a managed window in when it maps at 0,0. <see langword="null"/> keeps the position.
    /// </summary>
    public Func<Box>? CenterArea { get; set; }

    /// <summary>
    /// Gets or sets the function that gives the frame of a managed window, or <see langword="null"/> for no frame. On adoption the driver writes the result to <c>_NET_FRAME_EXTENTS</c>, and <see cref="CenterArea"/> centers the outer box so the frame stays inside the area.
    /// </summary>
    public Func<XWaylandWindow, FrameInsets>? FrameInsets { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether each new window gets a <see cref="SceneFit"/>. The fit scales or letterboxes a buffer of the wrong size.
    /// </summary>
    public bool Letterbox { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the driver moves the tree of a managed window to its X11 position. Override-redirect windows are always positioned.
    /// </summary>
    public bool PositionsManagedWindows { get; set; } = true;

    /// <summary>
    /// Gets or sets the renderer that draws the bounding shape mask. When it is <see langword="null"/>, a shaped window shows its full buffer but still takes input only inside the shape.
    /// </summary>
    public IRenderer? ShapeRenderer { get; set; }

    /// <summary>
    /// Gets or sets the allocator for the mask buffers. When it is <see langword="null"/>, the masks use memory buffers.
    /// </summary>
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

    /// <summary>
    /// Finds the fit node of an adopted window.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The fit, or <see langword="null"/> when the window is not adopted or was adopted without <see cref="Letterbox"/>.</returns>
    public SceneFit? FitOf(XWaylandWindow window) => Find(window)?.Fit;

    /// <summary>
    /// Finds the parent tree that an adopted window was put in.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The parent, or <see langword="null"/> when the window is not adopted.</returns>
    public SceneTree? ParentOf(XWaylandWindow window) => Find(window)?.Parent;

    /// <summary>
    /// Finds the outermost tree of an adopted window: the fit root, or else the surface tree. Move this tree to move the window.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns>The tree, or <see langword="null"/> when the window is not adopted.</returns>
    public SceneTree? OuterOf(XWaylandWindow window) => Find(window) is { } entry ? entry.Fit?.Root ?? entry.Scene.Tree : null;

    /// <summary>
    /// Updates the fit of an adopted window again, for example after its letterbox policy changes. An unknown window is ignored.
    /// </summary>
    /// <param name="window">The window.</param>
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

    /// <summary>
    /// Occurs after a window enters the scene. The arguments are the window, its scene surface, and <see langword="true"/> for a managed window.
    /// </summary>
    public event Action<XWaylandWindow, SceneSurface, bool>? Adopted;

    /// <summary>
    /// Occurs after an adopted window unmaps or is destroyed and its scene nodes are destroyed. <see cref="Dispose"/> does not raise it.
    /// </summary>
    public event Action<XWaylandWindow, SceneSurface>? Removed;

    /// <summary>
    /// Occurs when a window of an attached window manager asks for activation, for example through <c>_NET_ACTIVE_WINDOW</c>.
    /// </summary>
    public event Action<XWaylandWindow>? ActivationRequested;

    /// <summary>
    /// Gets the number of windows in the scene.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Starts to adopt the windows that <paramref name="wm"/> maps from now on. The driver detaches by itself when the window manager is disposed. A second call does nothing.
    /// </summary>
    /// <param name="wm">The window manager.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wm"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Stops adopting new windows from <paramref name="wm"/>. Windows already in the scene stay until they unmap.
    /// </summary>
    /// <param name="wm">The window manager.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wm"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Finds the scene surface of an adopted window by its Wayland surface.
    /// </summary>
    /// <param name="surface">The surface, or <see langword="null"/>.</param>
    /// <returns>The scene surface, or <see langword="null"/> when no adopted window has <paramref name="surface"/>.</returns>
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

    /// <summary>
    /// Detaches from every window manager and destroys the scene nodes of every adopted window. A second call does nothing.
    /// </summary>
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

        var insets = FrameInsets?.Invoke(window) ?? default;
        if (FrameInsets is not null)
        {
            window.SetFrameExtents(insets);
        }

        if (CenterArea is { } area && window is { X: 0, Y: 0 })
        {
            var box = area();
            var outerWidth = window.Width + insets.Left + insets.Right;
            var outerHeight = window.Height + insets.Top + insets.Bottom;
            window.Configure(
                box.X + insets.Left + Math.Max(0, (box.Width - outerWidth) / 2),
                box.Y + insets.Top + Math.Max(0, (box.Height - outerHeight) / 2),
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
