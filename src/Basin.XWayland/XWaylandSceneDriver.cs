using Basin.Scene;

namespace Basin.XWayland;

public sealed class XWaylandSceneDriver : IDisposable
{
    private readonly List<Entry> _entries = [];
    private XWaylandWm? _wm;
    private bool _disposed;

    public Func<XWaylandWindow, SceneTree?>? ManagedParent { get; set; }

    public Func<XWaylandWindow, SceneTree?>? OverrideRedirectParent { get; set; }

    public Func<Box>? CenterArea { get; set; }

    public event Action<XWaylandWindow, SceneSurface, bool>? Adopted;

    public event Action<XWaylandWindow, SceneSurface>? Removed;

    public event Action<XWaylandWindow>? ActivationRequested;

    public int Count => _entries.Count;

    public void Attach(XWaylandWm wm)
    {
        ArgumentNullException.ThrowIfNull(wm);
        _wm = wm;
        wm.WindowMapped += OnManaged;
        wm.OverrideRedirectMapped += OnOverrideRedirect;
        wm.ActivationRequested += OnActivation;
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
        if (_wm is { } wm)
        {
            wm.WindowMapped -= OnManaged;
            wm.OverrideRedirectMapped -= OnOverrideRedirect;
            wm.ActivationRequested -= OnActivation;
            _wm = null;
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

        var scene = new SceneSurface(parent, window.Surface!);
        scene.Tree.SetPosition(window.X, window.Y);
        var entry = new Entry(window, scene);
        entry.Layout = () =>
        {
            if (!scene.Tree.IsDestroyed)
            {
                scene.Tree.SetPosition(window.X, window.Y);
            }
        };
        entry.Drop = () => Remove(entry);
        window.GeometryChanged += entry.Layout;
        window.Unmapped += entry.Drop;
        window.Destroyed += entry.Drop;
        _entries.Add(entry);
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
        entry.Window.Unmapped -= entry.Drop;
        entry.Window.Destroyed -= entry.Drop;
        if (!entry.Scene.IsDestroyed)
        {
            entry.Scene.Destroy();
        }
    }

    private sealed class Entry(XWaylandWindow window, SceneSurface scene)
    {
        public XWaylandWindow Window { get; } = window;

        public SceneSurface Scene { get; } = scene;

        public Action? Layout { get; set; }

        public Action? Drop { get; set; }
    }
}
