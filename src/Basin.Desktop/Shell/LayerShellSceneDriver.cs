using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Desktop;

public sealed class LayerShellSceneDriver
{
    private readonly OutputLayout _layout;
    private readonly Func<LayerSurface, SceneTree> _treeFor;
    private readonly List<(LayerSurface Layer, SceneSurface? Scene)> _surfaces = [];
    private readonly List<(LayerSurface Layer, SceneSurface? Scene)> _scratch = [];
    private readonly Dictionary<LayerSurface, ArrangeInputs> _arranged = [];
    private readonly PopupPlacer _popups;
    private bool _trackingPopups;

    public LayerShellSceneDriver(LayerShell shell, OutputLayout layout, Func<LayerSurface, SceneTree> treeFor)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(treeFor);
        _layout = layout;
        _treeFor = treeFor;
        _popups = new PopupPlacer(layout);
        shell.NewSurface += OnNewSurface;
    }

    public LayerShellSceneDriver(LayerShell shell, OutputLayout layout, SceneLayers layers)
        : this(shell, layout, layer => TreeOf(layers, layer.Layer))
    {
    }

    public Func<LayerSurface, bool>? Accept { get; set; }

    public Func<LayerSurface, OutputGlobal?>? DefaultOutput { get; set; }

    public Func<LayerSurface, Box>? PopupBounds { get; set; }

    public IReadOnlyList<(LayerSurface Layer, SceneSurface? Scene)> Surfaces => _surfaces;

    public event Action<LayerSurface, SceneSurface>? SceneCreated;

    public event Action<LayerSurface, XdgPopupWindow, SceneSurface>? PopupSceneCreated;

    public event Action<LayerSurface>? Removed;

    public event Action<IOutput, Box>? UsableAreaChanged;

    public event Action? Arranged;

    public static SceneTree TreeOf(SceneLayers layers, LayerKind kind)
    {
        ArgumentNullException.ThrowIfNull(layers);
        return kind switch
        {
            LayerKind.Background => layers.Background,
            LayerKind.Bottom => layers.Bottom,
            LayerKind.Top => layers.Top,
            _ => layers.Overlay,
        };
    }

    public static LayerSurface? RootLayerOf(XdgPopupWindow popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        var root = popup;
        var parent = popup.Parent;
        while (parent is not null)
        {
            if (parent.Role is not XdgPopupWindow parentPopup)
            {
                return null;
            }

            root = parentPopup;
            parent = parentPopup.Parent;
        }

        return root.LayerParent;
    }

    public void TrackPopups(XdgShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        if (_trackingPopups)
        {
            throw new InvalidOperationException("popups are already tracked");
        }

        _trackingPopups = true;
        shell.NewPopup += OnNewPopup;
    }

    public SceneSurface? SceneOf(LayerSurface layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        foreach (var entry in _surfaces)
        {
            if (ReferenceEquals(entry.Layer, layer))
            {
                return entry.Scene;
            }
        }

        return null;
    }

    private readonly record struct ArrangeInputs(
        LayerKind Layer,
        LayerAnchor Anchor,
        int ExclusiveZone,
        LayerAnchor ExclusiveEdge,
        (int Top, int Right, int Bottom, int Left) Margin,
        int DesiredWidth,
        int DesiredHeight,
        OutputGlobal? Output,
        bool Mapped,
        bool Visible);

    private static ArrangeInputs InputsOf(LayerSurface layer, SceneSurface? scene) =>
        new(layer.Layer,
            layer.Anchor,
            layer.ExclusiveZone,
            layer.ExclusiveEdge,
            layer.Margin,
            layer.DesiredWidth,
            layer.DesiredHeight,
            layer.Output,
            layer.IsMapped,
            scene is not { IsDestroyed: false } visible || visible.Tree.Enabled);

    private void RearrangeIfMoved(LayerSurface layer)
    {
        var inputs = InputsOf(layer, SceneOf(layer));
        if (_arranged.TryGetValue(layer, out var last) && last == inputs)
        {
            return;
        }

        Rearrange();
    }

    public void Rearrange()
    {
        _arranged.Clear();
        foreach (var entry in _surfaces)
        {
            _arranged[entry.Layer] = InputsOf(entry.Layer, entry.Scene);
        }

        var first = true;
        foreach (var (output, _) in _layout.Outputs)
        {
            _scratch.Clear();
            foreach (var entry in _surfaces)
            {
                var target = entry.Layer.Output?.Output;
                if (target == output || (target is null && first))
                {
                    _scratch.Add(entry);
                }
            }

            first = false;
            var usable = LayerArrangement.Arrange(_layout.BoxOf(output), _scratch);
            UsableAreaChanged?.Invoke(output, usable);
        }

        Arranged?.Invoke();
    }

    private void OnNewSurface(LayerSurface layer)
    {
        if (Accept?.Invoke(layer) == false)
        {
            layer.Close();
            return;
        }

        layer.Output ??= DefaultOutput?.Invoke(layer);
        _surfaces.Add((layer, null));
        layer.PopupAdopted += AttachPopup;

        layer.InitialCommit += Rearrange;
        layer.Mapped += () =>
        {
            var index = _surfaces.FindIndex(entry => entry.Layer == layer);
            if (index < 0 || _surfaces[index].Scene is not null)
            {
                return;
            }

            var scene = new SceneSurface(_treeFor(layer), layer.Surface);
            _surfaces[index] = (layer, scene);
            Rearrange();
            SceneCreated?.Invoke(layer, scene);
        };
        layer.Committed += () => RearrangeIfMoved(layer);
        void Remove()
        {
            var index = _surfaces.FindIndex(entry => entry.Layer == layer);
            if (index < 0)
            {
                return;
            }

            if (_surfaces[index].Scene is { IsDestroyed: false } scene)
            {
                scene.Destroy();
            }

            _surfaces.RemoveAt(index);
            _arranged.Remove(layer);
            Removed?.Invoke(layer);
            Rearrange();
        }

        layer.Unmapped += Remove;
        layer.Destroyed += Remove;
    }

    private void OnNewPopup(XdgPopupWindow popup)
    {
        if (popup.Parent?.Role is XdgPopupWindow)
        {
            AttachPopup(popup);
        }
    }

    private void AttachPopup(XdgPopupWindow popup)
    {
        if (RootLayerOf(popup) is not { } layer || SceneOf(layer) is not { } layerScene)
        {
            return;
        }

        var scene = _popups.Attach(popup, layerScene.Tree, constrainBox: () => PopupBoundsOf(layer));
        PopupSceneCreated?.Invoke(layer, popup, scene);
    }

    private Box PopupBoundsOf(LayerSurface layer)
    {
        if (PopupBounds?.Invoke(layer) is { } box)
        {
            return box;
        }

        return layer.Output?.Output is { } output ? _layout.BoxOf(output) : _layout.Bounds;
    }
}
