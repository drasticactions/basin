using Basin;
using Basin.Scene;
using Basin.XWayland;
using Microsoft.Extensions.Logging;

namespace Westonia;

internal sealed class ShellXWayland : IDisposable
{
    private readonly WestonShell _shell;
    private readonly ShellLayers _layers;
    private readonly ILogger _log;
    private readonly List<(XWaylandWindow Window, SceneSurface Scene)> _windows = [];
    private readonly Func<SceneTree> _workspace;
    private readonly Func<Box> _area;
    private bool _disposed;

    public ShellXWayland(
        WestonShell shell,
        ShellLayers layers,
        Func<SceneTree> workspace,
        Func<Box> area,
        ILogger log)
    {
        _shell = shell;
        _layers = layers;
        _workspace = workspace;
        _area = area;
        _log = log;
    }

    public int Count => _windows.Count;

    public SceneNode? TreeOf(Surface surface)
    {
        foreach (var entry in _windows)
        {
            if (ReferenceEquals(entry.Window.Surface, surface))
            {
                return entry.Scene.Tree;
            }
        }

        return null;
    }

    public Action? Changed { get; set; }

    public void Attach(XWaylandWm wm)
    {
        wm.WindowMapped += OnMapped;
        wm.OverrideRedirectMapped += OnOverrideRedirect;
        wm.ActivationRequested += Activate;
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var entry in _windows)
        {
            if (!entry.Scene.Tree.IsDestroyed)
            {
                entry.Scene.Tree.Destroy();
            }
        }

        _windows.Clear();
    }

    private void OnMapped(XWaylandWindow window)
    {
        if (_disposed || window.Surface is null)
        {
            return;
        }

        if (window.X == 0 && window.Y == 0)
        {
            var area = _area();
            window.Configure(
                area.X + Math.Max(0, (area.Width - window.Width) / 2),
                area.Y + Math.Max(0, (area.Height - window.Height) / 2),
                window.Width,
                window.Height);
        }

        Adopt(window, _workspace());
        Activate(window);
    }

    private void OnOverrideRedirect(XWaylandWindow window)
    {
        if (_disposed || window.Surface is null)
        {
            return;
        }

        Adopt(window, _layers.InputPanel);
    }

    private void Adopt(XWaylandWindow window, SceneTree parent)
    {
        var scene = new SceneSurface(parent, window.Surface!);
        scene.Tree.SetPosition(window.X, window.Y);
        var entry = (window, scene);
        _windows.Add(entry);

        void Layout() => scene.Tree.SetPosition(window.X, window.Y);

        window.GeometryChanged += Layout;
        window.Unmapped += () => Remove(entry);
        window.Destroyed += () => Remove(entry);
        Changed?.Invoke();
        _log.LogInformation("mapped X window {Id} \"{Title}\"", window.WindowId, window.Title);
    }

    private void Remove((XWaylandWindow Window, SceneSurface Scene) entry)
    {
        if (!_windows.Remove(entry))
        {
            return;
        }

        if (!entry.Scene.Tree.IsDestroyed)
        {
            entry.Scene.Tree.Destroy();
        }

        Changed?.Invoke();
    }

    private void Activate(XWaylandWindow window)
    {
        if (window.Surface is { } surface && window.WantsFocus)
        {
            _shell.KeyboardTarget = null;
            _shell.Seat?.Keyboard.NotifyEnter(surface);
        }
    }
}
