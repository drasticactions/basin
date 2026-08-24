using Basin.Plasma.Protocol;
using Basin.Scene;
using Basin.Shell.Xdg;
using Wayland.Server;

namespace Basin.Plasma;

public sealed class ScreenEdgeManager : IDisposable
{
    public const int Version = 1;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Scene.Scene? _scene;
    private readonly OutputLayout? _layout;
    private readonly PlasmaScreenEdges? _edges;
    private readonly Dictionary<Surface, AutoHideScreenEdge> _bySurface = [];

    public ScreenEdgeManager(
        WlServerDisplay display,
        CompositorGlobal compositor,
        Scene.Scene? scene = null,
        OutputLayout? layout = null,
        PlasmaScreenEdges? edges = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(compositor);
        _compositor = compositor;
        _scene = scene;
        _layout = layout;
        _edges = edges;
        _global = display.CreateGlobal(KdeScreenEdgeManagerV1.Interface, Version, OnBind);
    }

    public event Action? Changed;

    public AutoHideScreenEdge? For(Surface? surface) =>
        surface is null ? null : _bySurface.GetValueOrDefault(surface);

    public void Dispose()
    {
        var edges = new List<AutoHideScreenEdge>(_bySurface.Values);
        foreach (var edge in edges)
        {
            edge.Release();
        }

        _bySurface.Clear();
        _global.Dispose();
    }

    internal void NotifyChanged() => Changed?.Invoke();

    internal SceneTree? NodeFor(Surface surface) =>
        _scene is null ? null : FindTree(_scene.Root, surface);

    internal IOutput? OutputFor(LayerSurface layer)
    {
        if (layer.Output?.Output is { } named)
        {
            return named;
        }

        if (_layout is null)
        {
            return null;
        }

        if (NodeFor(layer.Surface) is { } node)
        {
            var current = layer.Surface.Current;
            var (sceneX, sceneY) = node.ScenePosition;
            if (_layout.OutputAt(
                sceneX + (current.Width / 2.0), sceneY + (current.Height / 2.0)) is { } under)
            {
                return under;
            }
        }

        foreach (var (output, _) in _layout.Outputs)
        {
            return output;
        }

        return null;
    }

    internal IDisposable? ArmEdge(LayerAnchor border, IOutput output, Action triggered) =>
        _edges?.Arm(border, output, triggered);

    private static SceneTree? FindTree(SceneTree tree, Surface surface)
    {
        foreach (var child in tree.Children)
        {
            switch (child)
            {
                case SceneBuffer buffer when ReferenceEquals(buffer.InputSurface, surface):
                    return buffer.Parent;
                case SceneTree sub when FindTree(sub, surface) is { } found:
                    return found;
            }
        }

        return null;
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var resource = new KdeScreenEdgeManagerV1Resource(client, version, id);
        resource.GetAutoHideScreenEdge += (_, e) =>
        {
            var border = (uint)e.Border switch
            {
                (uint)KdeScreenEdgeManagerV1.Border.Top => LayerAnchor.Top,
                (uint)KdeScreenEdgeManagerV1.Border.Bottom => LayerAnchor.Bottom,
                (uint)KdeScreenEdgeManagerV1.Border.Left => LayerAnchor.Left,
                (uint)KdeScreenEdgeManagerV1.Border.Right => LayerAnchor.Right,
                _ => LayerAnchor.None,
            };
            if (border == LayerAnchor.None)
            {
                resource.PostError(
                    (uint)KdeScreenEdgeManagerV1.Error.InvalidBorder,
                    "border must be top, bottom, left or right");
                return;
            }

            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface?.RoleObject is not LayerSurface layer)
            {
                resource.PostError(
                    (uint)KdeScreenEdgeManagerV1.Error.InvalidRole,
                    "the surface is not a layer surface");
                return;
            }

            if (_bySurface.ContainsKey(surface))
            {
                resource.PostError(
                    (uint)KdeScreenEdgeManagerV1.Error.AlreadyConstructed,
                    "the surface already has a screen edge");
                return;
            }

            var edgeResource = new KdeAutoHideScreenEdgeV1Resource(client, resource.Version, e.Id);
            var edge = new AutoHideScreenEdge(this, edgeResource, layer, border);
            _bySurface[surface] = edge;
            edge.Removed += () => _bySurface.Remove(surface);
        };
    }
}
