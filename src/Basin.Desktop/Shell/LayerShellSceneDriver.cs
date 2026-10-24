using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Desktop;

/// <summary>
/// Drives a layer shell with the scene. It puts mapped layer surfaces into scene trees, arranges them per output and reports the usable area that is left.
/// </summary>
public sealed class LayerShellSceneDriver : ILayerShellDriver
{
    private readonly OutputLayout _layout;
    private readonly Func<LayerSurface, SceneTree> _treeFor;
    private readonly List<(LayerSurface Layer, SceneSurface? Scene)> _surfaces = [];
    private readonly List<(LayerSurface Layer, SceneSurface? Scene)> _scratch = [];
    private readonly Dictionary<LayerSurface, ArrangeInputs> _arranged = [];
    private readonly List<(LayerSurface Layer, SceneSurface? Scene)> _confined = [];
    private readonly List<(LayerSurface Layer, SceneSurface? Scene)> _single = [];
    private readonly PopupPlacer _popups;
    private bool _trackingPopups;
    private bool _arranging;
    private bool _again;
    private LayerShell? _shell;

    /// <summary>
    /// Initializes a new instance of the <see cref="LayerShellSceneDriver"/> class that drives <paramref name="shell"/> at once.
    /// </summary>
    /// <param name="shell">The layer shell to drive.</param>
    /// <param name="layout">The layout whose outputs the surfaces cover.</param>
    /// <param name="treeFor">The function that chooses the scene tree for a surface.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public LayerShellSceneDriver(LayerShell shell, OutputLayout layout, Func<LayerSurface, SceneTree> treeFor)
        : this(layout, treeFor)
    {
        ArgumentNullException.ThrowIfNull(shell);
        AttachTo(shell);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LayerShellSceneDriver"/> class that drives <paramref name="shell"/> at once and uses the scene layer for each surface's layer.
    /// </summary>
    /// <param name="shell">The layer shell to drive.</param>
    /// <param name="layout">The layout whose outputs the surfaces cover.</param>
    /// <param name="layers">The scene layers that hold the surfaces.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public LayerShellSceneDriver(LayerShell shell, OutputLayout layout, SceneLayers layers)
        : this(shell, layout, layer => TreeOf(layers, layer.Layer))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LayerShellSceneDriver"/> class and registers it as the <see cref="ILayerShellDriver"/>.
    /// </summary>
    /// <param name="services">The registry to register in.</param>
    /// <param name="layout">The layout whose outputs the surfaces cover.</param>
    /// <param name="treeFor">The function that chooses the scene tree for a surface.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public LayerShellSceneDriver(BasinServices services, OutputLayout layout, Func<LayerSurface, SceneTree> treeFor)
        : this(layout, treeFor)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Use<ILayerShellDriver>(this);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LayerShellSceneDriver"/> class, registers it as the <see cref="ILayerShellDriver"/> and uses the scene layer for each surface's layer.
    /// </summary>
    /// <param name="services">The registry to register in.</param>
    /// <param name="layout">The layout whose outputs the surfaces cover.</param>
    /// <param name="layers">The scene layers that hold the surfaces.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public LayerShellSceneDriver(BasinServices services, OutputLayout layout, SceneLayers layers)
        : this(services, layout, layer => TreeOf(layers, layer.Layer))
    {
    }

    private LayerShellSceneDriver(OutputLayout layout, Func<LayerSurface, SceneTree> treeFor)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(treeFor);
        _layout = layout;
        _treeFor = treeFor;
        _popups = new PopupPlacer(layout);
    }

    /// <summary>
    /// Gets or sets a function that decides whether to accept a new surface. A surface that gets <see langword="false"/> is closed.
    /// </summary>
    public Func<LayerSurface, bool>? Accept { get; set; }

    /// <summary>
    /// Gets or sets a function that chooses an output for a new surface that did not name one. Without an output, a surface goes on the first output.
    /// </summary>
    public Func<LayerSurface, OutputGlobal?>? DefaultOutput { get; set; }

    /// <summary>
    /// Gets or sets a function that gives the box in layout coordinates for the popups of a surface. Without it, popups stay on the surface's output.
    /// </summary>
    public Func<LayerSurface, Box>? PopupBounds { get; set; }

    /// <summary>
    /// Gets or sets a function that marks surfaces to arrange alone in a smaller area. A confined surface does not reduce the usable area.
    /// </summary>
    public Func<LayerSurface, IOutput, bool>? Confine { get; set; }

    /// <summary>
    /// Gets or sets a function that gives the area for a confined surface from the output and its usable area. Without it, the area is the whole output box.
    /// </summary>
    public Func<LayerSurface, IOutput, Box, Box>? ConfinedBox { get; set; }

    /// <summary>
    /// Gets the accepted surfaces in creation order. The scene surface is <see langword="null"/> until the surface maps.
    /// </summary>
    public IReadOnlyList<(LayerSurface Layer, SceneSurface? Scene)> Surfaces => _surfaces;

    /// <summary>
    /// Occurs when a surface maps and gets its scene surface, after the arrangement.
    /// </summary>
    public event Action<LayerSurface, SceneSurface>? SceneCreated;

    /// <summary>
    /// Occurs when a popup of a mapped surface gets its scene surface. The arguments are the root surface, the popup and its scene surface.
    /// </summary>
    public event Action<LayerSurface, XdgPopupWindow, SceneSurface>? PopupSceneCreated;

    /// <summary>
    /// Occurs when a surface unmaps or is destroyed, after its scene surface is destroyed and before the new arrangement.
    /// </summary>
    public event Action<LayerSurface>? Removed;

    /// <summary>
    /// Occurs for each output on each arrangement, with the area in layout coordinates that exclusive zones leave. It occurs also when the area did not change.
    /// </summary>
    public event Action<IOutput, Box>? UsableAreaChanged;

    /// <summary>
    /// Occurs after an arrangement of all outputs is complete.
    /// </summary>
    public event Action? Arranged;

    void ILayerShellDriver.Attach(LayerShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        AttachTo(shell);
    }

    /// <summary>
    /// Chooses the scene layer for a layer-shell layer.
    /// </summary>
    /// <param name="layers">The scene layers.</param>
    /// <param name="kind">The layer-shell layer.</param>
    /// <returns>The matching tree. An unknown layer gets the overlay tree.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="layers"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Follows the popup parents up to the layer surface at the root.
    /// </summary>
    /// <param name="popup">The popup to start at.</param>
    /// <returns>The layer surface, or <see langword="null"/> when the chain ends at another kind of parent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="popup"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Places nested popups of layer surfaces too. Popups whose parent is a layer surface are placed without this call.
    /// </summary>
    /// <param name="shell">The xdg shell whose new popups to watch.</param>
    /// <exception cref="ArgumentNullException"><paramref name="shell"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The driver already tracks popups.</exception>
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

    /// <summary>
    /// Finds the scene surface of an accepted surface.
    /// </summary>
    /// <param name="layer">The surface to look up.</param>
    /// <returns>The scene surface, or <see langword="null"/> when the surface is unknown or not mapped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="layer"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Arranges every surface on every output and raises <see cref="UsableAreaChanged"/> and <see cref="Arranged"/>. A call from inside an arrangement runs one more pass.
    /// </summary>
    public void Rearrange()
    {
        if (_arranging)
        {
            _again = true;
            return;
        }

        _arranging = true;
        try
        {
            do
            {
                _again = false;
                RearrangeOnce();
            }
            while (_again);
        }
        finally
        {
            _arranging = false;
        }

        Arranged?.Invoke();
    }

    private void RearrangeOnce()
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
            _confined.Clear();
            foreach (var entry in _surfaces)
            {
                var target = entry.Layer.Output?.Output;
                if (target == output || (target is null && first))
                {
                    if (Confine is { } confine && confine(entry.Layer, output))
                    {
                        _confined.Add(entry);
                    }
                    else
                    {
                        _scratch.Add(entry);
                    }
                }
            }

            first = false;
            var box = _layout.BoxOf(output);
            var usable = LayerArrangement.Arrange(box, _scratch);
            UsableAreaChanged?.Invoke(output, usable);
            foreach (var entry in _confined)
            {
                _single.Clear();
                _single.Add(entry);
                var area = ConfinedBox is { } boxOf ? boxOf(entry.Layer, output, usable) : box;
                _ = LayerArrangement.Arrange(area, _single);
            }
        }
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

    private void AttachTo(LayerShell shell)
    {
        if (_shell is not null)
        {
            throw new InvalidOperationException("this layer-shell driver already drives a layer shell");
        }

        _shell = shell;
        shell.NewSurface += OnNewSurface;
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
