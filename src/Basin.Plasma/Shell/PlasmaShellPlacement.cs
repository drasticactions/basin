using Basin.Capabilities;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Plasma;

public sealed class PlasmaShellPlacement : IDisposable
{
    private readonly Scene.Scene _scene;
    private readonly OutputLayout _layout;
    private readonly IOutputSet? _outputs;
    private readonly SceneLayers _layers;
    private readonly SceneTree _panels;
    private readonly SceneTree _appletPopups;
    private readonly SceneTree _notifications;
    private readonly SceneTree _tooltips;
    private readonly SceneTree _criticalNotifications;
    private readonly SceneTree _osds;
    private readonly Dictionary<PlasmaShellSurface, Placed> _placed = [];
    private readonly Dictionary<IOutput, Box> _usable = [];
    private PlasmaShellManager? _manager;
    private bool _disposed;

    private sealed class Placed
    {
        public required PlasmaShellSurface Shell;
        public SceneSurface? Scene;
        public bool Positioned;
        public Action? OnCommitted;
        public IDisposable? EdgeToken;
    }

    public PlasmaShellPlacement(Scene.Scene scene, OutputLayout layout, IOutputSet? outputs = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(layout);
        _scene = scene;
        _layout = layout;
        _outputs = outputs;
        _layers = new SceneLayers(scene.Root);
        _panels = new SceneTree(_layers.Top);
        _appletPopups = new SceneTree(_layers.Top);
        _notifications = new SceneTree(_layers.Overlay);
        _tooltips = new SceneTree(_layers.Overlay);
        _criticalNotifications = new SceneTree(_layers.Overlay);
        _osds = new SceneTree(_layers.Overlay);
        _layout.Changed += OnLayoutChanged;
    }

    public SceneLayers Layers => _layers;

    public SceneTree Background => _layers.Background;

    public SceneTree Bottom => _layers.Bottom;

    public SceneTree Windows => _layers.Windows;

    public SceneTree Top => _layers.Top;

    public SceneTree Overlay => _layers.Overlay;

    public PlasmaScreenEdges? ScreenEdges { get; set; }

    public Basin.Seat.Seat? Seat { get; set; }

    public event Action? UsableAreaChanged;

    public event Action<PlasmaShellSurface>? ActivationRequested;

    public event Action<PlasmaShellSurface, SceneSurface>? SceneCreated;

    public static LayerKind? LayerOf(PlasmaShellRole role) => role switch
    {
        PlasmaShellRole.Desktop => LayerKind.Background,
        PlasmaShellRole.Panel or PlasmaShellRole.AppletPopup => LayerKind.Top,
        PlasmaShellRole.OnScreenDisplay or PlasmaShellRole.Notification or
        PlasmaShellRole.Tooltip or PlasmaShellRole.CriticalNotification => LayerKind.Overlay,
        _ => null,
    };

    public SceneTree TreeOf(PlasmaShellRole role) => role switch
    {
        PlasmaShellRole.Desktop => _layers.Background,
        PlasmaShellRole.Panel => _panels,
        PlasmaShellRole.AppletPopup => _appletPopups,
        PlasmaShellRole.Notification => _notifications,
        PlasmaShellRole.Tooltip => _tooltips,
        PlasmaShellRole.CriticalNotification => _criticalNotifications,
        PlasmaShellRole.OnScreenDisplay => _osds,
        _ => _layers.Windows,
    };

    public SceneTree TreeFor(LayerKind kind) => kind switch
    {
        LayerKind.Background => _layers.Background,
        LayerKind.Bottom => _layers.Bottom,
        LayerKind.Top => _layers.Top,
        _ => _layers.Overlay,
    };

    public SceneSurface? SceneOf(PlasmaShellSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return _placed.TryGetValue(surface, out var placed) ? placed.Scene : null;
    }

    public Box UsableArea(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _usable.TryGetValue(output, out var box) ? box : _layout.BoxOf(output);
    }

    public void Attach(PlasmaShellManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
        manager.SurfaceAdded += OnSurfaceAdded;
        foreach (var surface in manager.Surfaces)
        {
            OnSurfaceAdded(surface);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _layout.Changed -= OnLayoutChanged;
        if (_manager is { } manager)
        {
            manager.SurfaceAdded -= OnSurfaceAdded;
        }

        foreach (var placed in _placed.Values)
        {
            Release(placed);
        }

        _placed.Clear();
    }

    private void OnSurfaceAdded(PlasmaShellSurface shell)
    {
        var placed = new Placed { Shell = shell };
        _placed[shell] = placed;
        placed.OnCommitted = () => OnCommitted(placed);
        shell.Surface.Committed += placed.OnCommitted;
        shell.RoleChanged += () => OnRoleChanged(placed);
        shell.PositionChanged += () => OnPositionChanged(placed);
        shell.AutoHideRequested += hide => OnAutoHide(placed, hide);
        shell.TakesFocusChanged += () => OnTakesFocusChanged(placed);
        shell.Destroyed += () =>
        {
            if (_placed.Remove(shell, out var gone))
            {
                Release(gone);
                RecomputeUsableAreas();
            }
        };
    }

    private void Release(Placed placed)
    {
        if (placed.OnCommitted is { } handler)
        {
            placed.Shell.Surface.Committed -= handler;
            placed.OnCommitted = null;
        }

        placed.EdgeToken?.Dispose();
        placed.EdgeToken = null;

        if (placed.Scene is { IsDestroyed: false } scene)
        {
            scene.Destroy();
        }

        placed.Scene = null;
    }

    private void OnCommitted(Placed placed)
    {
        var surface = placed.Shell.Surface;
        if (surface.Current.Buffer is null)
        {
            if (placed.Shell.Role == PlasmaShellRole.Panel)
            {
                RecomputeUsableAreas();
            }

            return;
        }

        var created = false;
        if (placed.Scene is null or { IsDestroyed: true })
        {
            placed.Scene = new SceneSurface(TreeOf(placed.Shell.Role), surface);
            placed.Positioned = false;
            created = true;
        }

        if (!placed.Positioned)
        {
            placed.Positioned = true;
            PlaceInitially(placed);
        }

        if (created)
        {
            SceneCreated?.Invoke(placed.Shell, placed.Scene);
        }

        if (placed.Shell.Role == PlasmaShellRole.Panel)
        {
            RecomputeUsableAreas();
        }
    }

    private void OnRoleChanged(Placed placed)
    {
        placed.Scene?.Tree.Reparent(TreeOf(placed.Shell.Role));
        RecomputeUsableAreas();
    }

    private void OnPositionChanged(Placed placed)
    {
        if (placed.Scene is { IsDestroyed: false } scene)
        {
            var position = placed.Shell.Position;
            scene.Tree.SetPosition(position.X, position.Y);
            if (placed.Shell.Role == PlasmaShellRole.Panel)
            {
                RecomputeUsableAreas();
            }
        }
    }

    private void PlaceInitially(Placed placed)
    {
        var shell = placed.Shell;
        var scene = placed.Scene!;
        if (TryPlaceUnderCursor(placed))
        {
            return;
        }

        if (shell.HasPosition)
        {
            scene.Tree.SetPosition(shell.Position.X, shell.Position.Y);
            return;
        }

        var output = OutputFor(shell);
        if (output is null)
        {
            return;
        }

        var box = _layout.BoxOf(output);
        if (shell.Role == PlasmaShellRole.Desktop)
        {
            scene.Tree.SetPosition(box.X, box.Y);
            return;
        }

        var current = shell.Surface.Current;
        scene.Tree.SetPosition(
            box.X + (box.Width - current.Width) / 2,
            box.Y + (box.Height - current.Height) / 2);
    }

    private bool TryPlaceUnderCursor(Placed placed)
    {
        if (!placed.Shell.OpenUnderCursor || Seat is not { } seat)
        {
            return false;
        }

        var x = (int)seat.Pointer.LayoutX;
        var y = (int)seat.Pointer.LayoutY;
        var output = _layout.OutputAt(x, y);
        if (output is null)
        {
            return false;
        }

        var box = _layout.BoxOf(output);
        var current = placed.Shell.Surface.Current;
        x = Math.Clamp(x, box.X, Math.Max(box.X, box.X + box.Width - current.Width));
        y = Math.Clamp(y, box.Y, Math.Max(box.Y, box.Y + box.Height - current.Height));
        placed.Scene!.Tree.SetPosition(x, y);
        return true;
    }

    private void OnTakesFocusChanged(Placed placed)
    {
        var shell = placed.Shell;
        if (!shell.TakesFocus || !shell.Focusable || !shell.Surface.IsMapped)
        {
            return;
        }

        Seat?.Keyboard.NotifyEnter(shell.Surface);
        ActivationRequested?.Invoke(shell);
    }

    private void OnAutoHide(Placed placed, bool hide)
    {
        if (placed.Scene is not { IsDestroyed: false } scene)
        {
            if (hide)
            {
                placed.Shell.NotifyAutoHidden();
            }
            else
            {
                placed.Shell.NotifyAutoShown();
            }

            return;
        }

        placed.EdgeToken?.Dispose();
        placed.EdgeToken = null;
        if (hide)
        {
            scene.Tree.Enabled = false;
            placed.Shell.NotifyAutoHidden();
            ArmRevealEdge(placed);
        }
        else
        {
            scene.Tree.Enabled = true;
            placed.Shell.NotifyAutoShown();
        }

        RecomputeUsableAreas();
    }

    private void ArmRevealEdge(Placed placed)
    {
        if (ScreenEdges is not { } edges || OutputFor(placed.Shell) is not { } output)
        {
            return;
        }

        var box = PanelBox(placed);
        if (box.IsEmpty)
        {
            return;
        }

        var anchor = NearestEdge(box, _layout.BoxOf(output));
        placed.EdgeToken = edges.Arm(anchor, output, () =>
        {
            placed.EdgeToken = null;
            OnAutoHide(placed, hide: false);
        });
    }

    private IOutput? OutputFor(PlasmaShellSurface shell)
    {
        if (shell.Output is { } named)
        {
            return named;
        }

        if (_placed.TryGetValue(shell, out var placed) &&
            placed.Scene is { IsDestroyed: false } scene)
        {
            var current = shell.Surface.Current;
            if (_layout.OutputAt(
                scene.Tree.X + (current.Width / 2.0), scene.Tree.Y + (current.Height / 2.0)) is { } under)
            {
                return under;
            }
        }

        var outputs = _outputs?.Outputs;
        if (outputs is { Count: > 0 })
        {
            return outputs[0];
        }

        foreach (var (output, _) in _layout.Outputs)
        {
            return output;
        }

        return null;
    }

    private Box PanelBox(Placed placed)
    {
        if (placed.Scene is not { IsDestroyed: false } scene)
        {
            return default;
        }

        var current = placed.Shell.Surface.Current;
        return new Box(scene.Tree.X, scene.Tree.Y, current.Width, current.Height);
    }

    private static LayerAnchor NearestEdge(Box panel, Box output)
    {
        var left = panel.X - output.X;
        var right = output.X + output.Width - (panel.X + panel.Width);
        var top = panel.Y - output.Y;
        var bottom = output.Y + output.Height - (panel.Y + panel.Height);
        var nearest = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
        if (nearest == bottom)
        {
            return LayerAnchor.Bottom;
        }

        if (nearest == top)
        {
            return LayerAnchor.Top;
        }

        return nearest == left ? LayerAnchor.Left : LayerAnchor.Right;
    }

    private void OnLayoutChanged() => RecomputeUsableAreas();

    private void RecomputeUsableAreas()
    {
        _usable.Clear();
        foreach (var (output, _) in _layout.Outputs)
        {
            _usable[output] = _layout.BoxOf(output);
        }

        foreach (var placed in _placed.Values)
        {
            var shell = placed.Shell;
            if (shell.Role != PlasmaShellRole.Panel || shell.IsAutoHidden ||
                placed.Scene is not { IsDestroyed: false } scene || !scene.Tree.Enabled ||
                !shell.Surface.IsMapped)
            {
                continue;
            }

            if (OutputFor(shell) is not { } output || !_usable.TryGetValue(output, out var usable))
            {
                continue;
            }

            var panel = PanelBox(placed);
            if (panel.IsEmpty)
            {
                continue;
            }

            _usable[output] = Subtract(usable, panel, NearestEdge(panel, _layout.BoxOf(output)));
        }

        UsableAreaChanged?.Invoke();
    }

    private static Box Subtract(Box usable, Box panel, LayerAnchor edge) => edge switch
    {
        LayerAnchor.Bottom => ClampHeight(usable, panel.Y - usable.Y),
        LayerAnchor.Top => new Box(
            usable.X,
            panel.Y + panel.Height,
            usable.Width,
            Math.Max(0, usable.Y + usable.Height - (panel.Y + panel.Height))),
        LayerAnchor.Left => new Box(
            panel.X + panel.Width,
            usable.Y,
            Math.Max(0, usable.X + usable.Width - (panel.X + panel.Width)),
            usable.Height),
        _ => new Box(usable.X, usable.Y, Math.Max(0, panel.X - usable.X), usable.Height),
    };

    private static Box ClampHeight(Box usable, int height) =>
        new(usable.X, usable.Y, usable.Width, Math.Max(0, Math.Min(usable.Height, height)));
}
