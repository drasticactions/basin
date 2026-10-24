using System.Numerics;
using Basin.Capabilities;
using Basin.Host;
using Basin.Scene;
using Basin.UI.ImGui;
using Hexa.NET.ImGui;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace Basin.Inspector;

/// <summary>
/// The inspector: one ImGui surface that covers one output and draws a menu bar and each shown <see cref="IInspectorPanel"/>. Input passes through it except over a panel, the menu bar or a popup.
/// </summary>
public sealed unsafe class InspectorOverlay : IImGuiContent, IDisposable
{
    private readonly ImGuiUIHost _host;
    private readonly SceneTree _layer;
    private readonly UISurfaceIndex? _index;
    private readonly Func<long> _clock;
    private readonly InspectorText _text = new();
    private readonly List<IInspectorPanel> _panels = [];
    private readonly List<bool> _open = [];
    private UISurfaceNode? _node;
    private OutputView? _output;
    private Box _placed;
    private double _placedScale;
    private bool _shown;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="InspectorOverlay"/> class. It makes no surface until the first <see cref="Open(OutputView)"/> on an output that has a size.
    /// </summary>
    /// <param name="host">The ImGui host that makes the surface.</param>
    /// <param name="layer">The scene tree that receives the overlay node.</param>
    /// <param name="index">The index that routes input to the surface, or <see langword="null"/> for none.</param>
    /// <param name="clockNanos">The clock in nanoseconds for <see cref="InspectorDraw.NowNanos"/>, or <see langword="null"/> for <see cref="MonotonicClock.Nanos"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> or <paramref name="layer"/> is <see langword="null"/>.</exception>
    public InspectorOverlay(ImGuiUIHost host, SceneTree layer, UISurfaceIndex? index = null, Func<long>? clockNanos = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(layer);
        _host = host;
        _layer = layer;
        _index = index;
        _clock = clockNanos ?? (() => MonotonicClock.Nanos);
    }

    /// <summary>
    /// Gets a value indicating whether the overlay is open. It is <see langword="true"/> after <see cref="Open(OutputView)"/> also when the output has no size yet.
    /// </summary>
    public bool IsOpen => _shown;

    /// <summary>
    /// Gets the output that the overlay is on, or will open on. It stays set after <see cref="Close"/>, and is <see langword="null"/> before the first open or after a removal with no fallback.
    /// </summary>
    public OutputView? Output => _output;

    /// <summary>
    /// Gets the name of the ImGui host, for example <c>gl</c>.
    /// </summary>
    public string HostName => _host.Name;

    /// <summary>
    /// Gets the scene node of the overlay. It is <see langword="null"/> until the first open that places the overlay, and after <see cref="Dispose"/>.
    /// </summary>
    public UISurfaceNode? Node => _node;

    /// <summary>
    /// Gets the ImGui surface of the overlay, or <see langword="null"/> when the node has no surface.
    /// </summary>
    public ImGuiUISurface? Surface => _node?.Surface as ImGuiUISurface;

    /// <summary>
    /// Gets the panels in the order of <see cref="Add"/>.
    /// </summary>
    public IReadOnlyList<IInspectorPanel> Panels => _panels;

    /// <summary>
    /// Gets the text buffer that the overlay resets at the start of each frame.
    /// </summary>
    public InspectorText Text => _text;

    /// <summary>
    /// Occurs when the overlay gets a new surface, after it sets itself as the content. A consumer sets the key text, the modifiers and the font there.
    /// </summary>
    public event Action<ImGuiUISurface>? SurfaceCreated;

    /// <summary>
    /// Occurs when the overlay opens or closes, or when a panel is shown or hidden from code, the menu or its close button.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Adds a panel. <see cref="Dispose"/> disposes each panel that is <see cref="IDisposable"/>.
    /// </summary>
    /// <param name="panel">The panel to add.</param>
    /// <param name="open"><see langword="true"/> to show the panel at once.</param>
    /// <exception cref="ArgumentNullException"><paramref name="panel"/> is <see langword="null"/>.</exception>
    public void Add(IInspectorPanel panel, bool open = false)
    {
        ArgumentNullException.ThrowIfNull(panel);
        _panels.Add(panel);
        _open.Add(open);
        Invalidate();
    }

    /// <summary>
    /// Tells whether a panel is shown. The name matches without regard to ASCII case.
    /// </summary>
    /// <param name="name">The UTF-8 name of the panel.</param>
    /// <returns><see langword="true"/> when a panel has that name and is shown.</returns>
    public bool IsShown(ReadOnlySpan<byte> name)
    {
        var index = IndexOf(name);
        return index >= 0 && _open[index];
    }

    /// <summary>
    /// Tells whether a panel is shown. The name matches without regard to ASCII case. This overload allocates.
    /// </summary>
    /// <param name="name">The name of the panel.</param>
    /// <returns><see langword="true"/> when a panel has that name and is shown.</returns>
    public bool IsShown(string name) => IsShown(System.Text.Encoding.UTF8.GetBytes(name));

    /// <summary>
    /// Shows or hides a panel. The name matches without regard to ASCII case. <see cref="Changed"/> occurs only when the state changes.
    /// </summary>
    /// <param name="name">The UTF-8 name of the panel.</param>
    /// <param name="open"><see langword="true"/> to show the panel, <see langword="false"/> to hide it.</param>
    /// <returns><see langword="false"/> when no panel has that name.</returns>
    public bool Show(ReadOnlySpan<byte> name, bool open = true)
    {
        var index = IndexOf(name);
        if (index < 0)
        {
            return false;
        }

        if (_open[index] != open)
        {
            _open[index] = open;
            Invalidate();
            Changed?.Invoke();
        }

        return true;
    }

    /// <summary>
    /// Shows or hides a panel. The name matches without regard to ASCII case. This overload allocates.
    /// </summary>
    /// <param name="name">The name of the panel.</param>
    /// <param name="open"><see langword="true"/> to show the panel, <see langword="false"/> to hide it.</param>
    /// <returns><see langword="false"/> when no panel has that name.</returns>
    public bool Show(string name, bool open = true) => Show(System.Text.Encoding.UTF8.GetBytes(name), open);

    /// <summary>
    /// Opens the overlay on an output, or moves it there. When the output has no size yet, the request stays and <see cref="Refit"/> places it later.
    /// </summary>
    /// <param name="view">The output to cover.</param>
    /// <returns><see langword="true"/> when the overlay is placed on the output.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The overlay is disposed.</exception>
    public bool Open(OutputView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _output = view;
        _shown = true;
        var placed = Place();
        if (_node is { } node)
        {
            node.AutoEnable = true;
            node.InputEnabled = true;
            node.Enabled = node.Surface is not null && placed;
        }

        Invalidate();
        Changed?.Invoke();
        return placed;
    }

    /// <summary>
    /// Closes the overlay. It disables the node and its input, but keeps the surface, so the panels keep their places for the next open.
    /// </summary>
    public void Close()
    {
        if (!_shown)
        {
            return;
        }

        _shown = false;
        if (_node is { } node)
        {
            node.AutoEnable = false;
            node.InputEnabled = false;
            node.Enabled = false;
        }

        Invalidate();
        Changed?.Invoke();
    }

    /// <summary>
    /// Closes the overlay when it is open on <paramref name="view"/>. Otherwise it opens the overlay there.
    /// </summary>
    /// <param name="view">The output to open on.</param>
    /// <returns><see langword="true"/> when the overlay is open and placed after the call.</returns>
    public bool Toggle(OutputView view)
    {
        if (_shown && ReferenceEquals(view, _output))
        {
            Close();
            return false;
        }

        return Open(view);
    }

    /// <summary>
    /// Puts the overlay on another output and opens it. It does the same as <see cref="Open(OutputView)"/>.
    /// </summary>
    /// <param name="view">The output to cover.</param>
    /// <returns><see langword="true"/> when the overlay is placed on the output.</returns>
    public bool MoveTo(OutputView view) => Open(view);

    /// <summary>
    /// Handles an output that goes away. When the overlay is on <paramref name="view"/>, it moves to <paramref name="fallback"/>, or closes and forgets the output when there is no fallback.
    /// </summary>
    /// <param name="view">The output that goes away.</param>
    /// <param name="fallback">The output to move to, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is <see langword="null"/>.</exception>
    public void Remove(OutputView view, OutputView? fallback)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!ReferenceEquals(view, _output))
        {
            return;
        }

        _placed = default;
        if (fallback is null)
        {
            Close();
            _output = null;
            return;
        }

        if (_shown)
        {
            Open(fallback);
        }
        else
        {
            _output = fallback;
        }
    }

    /// <summary>
    /// Places the open overlay on the box and scale of its output again. A consumer calls it when the output layout changes.
    /// </summary>
    public void Refit()
    {
        if (_shown && _output is not null)
        {
            Place();
        }
    }

    /// <summary>
    /// Makes the surface draw a new frame. It does nothing when there is no surface.
    /// </summary>
    public void Invalidate() => Surface?.Invalidate();

    /// <inheritdoc/>
    public void Build(ImGuiFrame frame)
    {
        _text.Reset();
        if (!_shown)
        {
            return;
        }

        ImGuiApi.DockSpaceOverViewport(0, ImGuiApi.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode);
        if (ImGuiApi.BeginMainMenuBar())
        {
            if (ImGuiApi.BeginMenu("Panels"u8))
            {
                for (var i = 0; i < _panels.Count; i++)
                {
                    var open = _open[i];
                    if (ImGuiApi.MenuItem(_text.Copy(_panels[i].Name), default(ReadOnlySpan<byte>), ref open))
                    {
                        _open[i] = open;
                        Changed?.Invoke();
                    }
                }

                ImGuiApi.EndMenu();
            }

            ImGuiApi.TextUnformatted(_text.Format($"  {_host.Name}  {frame.Width}x{frame.Height}@{frame.Scale:0.##}"));
            ImGuiApi.EndMainMenuBar();
        }

        var draw = new InspectorDraw(frame, _text, this, _clock());
        for (var i = 0; i < _panels.Count; i++)
        {
            if (!_open[i])
            {
                continue;
            }

            var open = true;
            ImGuiApi.SetNextWindowPos(new Vector2(24 + ((i % 2) * 540) + ((i / 4) * 24), 40 + ((i / 2) % 2 * 380) + ((i / 4) * 24)), ImGuiCond.FirstUseEver);
            ImGuiApi.SetNextWindowSize(new Vector2(520, 360), ImGuiCond.FirstUseEver);
            if (ImGuiApi.Begin(_text.Copy(_panels[i].Name), ref open))
            {
                _panels[i].Draw(ref draw);
            }

            ImGuiApi.End();
            if (!open)
            {
                _open[i] = false;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>
    /// Disposes the node and each <see cref="IDisposable"/> panel, and removes all panels. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shown = false;
        _node?.Dispose();
        _node = null;
        foreach (var panel in _panels)
        {
            (panel as IDisposable)?.Dispose();
        }

        _panels.Clear();
        _open.Clear();
    }

    private bool Place()
    {
        if (_output is not { } view || view.Box.IsEmpty)
        {
            return false;
        }

        if (_node is null)
        {
            _node = new UISurfaceNode(_layer, _host, _index)
            {
                Target = _host.Produces.HasFlag(UITargetKind.Dmabuf) ? UITargetKind.Dmabuf : UITargetKind.Memory,
                InputEnabled = true,
                InputFromSurface = true,
                PreciseDamage = true,
            };
            _node.Faulted += error => InspectorLog.Log.Error($"the inspector surface faulted: {error.Message}");
        }

        var box = view.Box;
        if (box == _placed && view.Scale == _placedScale && _node.Surface is not null)
        {
            return true;
        }

        var had = _node.Surface;
        _node.SetPosition(box.X, box.Y);
        if (!_node.Configure(box.Width, box.Height, view.Scale) || _node.Surface is not ImGuiUISurface surface)
        {
            return false;
        }

        _placed = box;
        _placedScale = view.Scale;
        if (!ReferenceEquals(had, surface))
        {
            surface.Content = this;
            surface.Faulted += error => InspectorLog.Log.Error($"an inspector panel threw: {error}");
            SurfaceCreated?.Invoke(surface);
        }

        return true;
    }

    private int IndexOf(ReadOnlySpan<byte> name)
    {
        for (var i = 0; i < _panels.Count; i++)
        {
            if (System.Text.Ascii.EqualsIgnoreCase(_panels[i].Name, name))
            {
                return i;
            }
        }

        return -1;
    }
}
