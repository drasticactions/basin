using System.Numerics;
using Basin.Capabilities;
using Basin.Host;
using Basin.Scene;
using Basin.UI.ImGui;
using Hexa.NET.ImGui;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace Basin.Inspector;

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

    public InspectorOverlay(ImGuiUIHost host, SceneTree layer, UISurfaceIndex? index = null, Func<long>? clockNanos = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(layer);
        _host = host;
        _layer = layer;
        _index = index;
        _clock = clockNanos ?? (() => MonotonicClock.Nanos);
    }

    public bool IsOpen => _shown;

    public OutputView? Output => _output;

    public string HostName => _host.Name;

    public UISurfaceNode? Node => _node;

    public ImGuiUISurface? Surface => _node?.Surface as ImGuiUISurface;

    public IReadOnlyList<IInspectorPanel> Panels => _panels;

    public InspectorText Text => _text;

    public event Action<ImGuiUISurface>? SurfaceCreated;

    public event Action? Changed;

    public void Add(IInspectorPanel panel, bool open = false)
    {
        ArgumentNullException.ThrowIfNull(panel);
        _panels.Add(panel);
        _open.Add(open);
        Invalidate();
    }

    public bool IsShown(ReadOnlySpan<byte> name)
    {
        var index = IndexOf(name);
        return index >= 0 && _open[index];
    }

    public bool IsShown(string name) => IsShown(System.Text.Encoding.UTF8.GetBytes(name));

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

    public bool Show(string name, bool open = true) => Show(System.Text.Encoding.UTF8.GetBytes(name), open);

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

    public bool Toggle(OutputView view)
    {
        if (_shown && ReferenceEquals(view, _output))
        {
            Close();
            return false;
        }

        return Open(view);
    }

    public bool MoveTo(OutputView view) => Open(view);

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

    public void Refit()
    {
        if (_shown && _output is not null)
        {
            Place();
        }
    }

    public void Invalidate() => Surface?.Invalidate();

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
