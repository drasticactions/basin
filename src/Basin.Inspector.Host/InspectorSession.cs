using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Inspector.Ipc;
using Basin.Ipc;
using Basin.Scene;
using Basin.UI.ImGui;

namespace Basin.Inspector;

public sealed class InspectorSession : IDisposable
{
    private readonly InspectorSessionOptions _options;
    private readonly bool _ownsHost;
    private readonly SceneTree _layer;
    private readonly SceneTree? _ownedLayer;
    private readonly Action<OutputView> _order;
    private ImGuiUIHost? _host;
    private UIDriver? _driver;
    private InspectorOverlay? _overlay;
    private string? _hostDeclined;
    private OutputsPanel? _outputs;
    private ClientsPanel? _clients;
    private ScenePanel? _scene;
    private MemoryPanel? _memory;
    private FrameSettle? _settle;
    private bool _disposed;

    public InspectorSession(InspectorSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _host = options.Host;
        _ownsHost = options.Host is null;
        Settings = options.Settings;
        _settle = options.Settle;
        if (options.Layer is { } layer)
        {
            _layer = layer;
        }
        else
        {
            _ownedLayer = new SceneTree(options.Below?.Parent ?? options.Scene.Root);
            _layer = _ownedLayer;
        }

        _order = _ => Order();
        Order();
        Input = new InspectorInput(options.Scene, options.Seat.Keyboard, options.Seat.Pointer, () => IsOpen);
        if (_ownedLayer is not null)
        {
            options.Driver.Added += _order;
            options.Driver.BeforeRepaint += _order;
        }

        options.Driver.Removed += OnOutputRemoved;
        options.Driver.LayoutChanged += OnLayoutChanged;
        options.Seat.Keyboard.ModifiersChanged += Input.ForwardModifiers;
        options.Seat.Keyboard.KeymapChanged += OnKeymapChanged;
    }

    public InspectorSettings Settings { get; private set; }

    public SceneTree Layer => _layer;

    public InspectorInput Input { get; }

    public InspectorOverlay? Overlay => _overlay;

    public ImGuiUIHost? Host => _host;

    public UIDriver? Driver => _driver;

    public FrameSettle? Settle
    {
        get => _settle;
        set
        {
            if (ReferenceEquals(value, _settle))
            {
                return;
            }

            if (_driver is not null)
            {
                _settle?.Remove(_driver);
                value?.Add(_driver);
            }

            _settle = value;
        }
    }

    public bool IsOpen => _overlay is { IsOpen: true };

    public string? Declined => !Settings.Enabled ? "[inspector] enabled is false" : _hostDeclined;

    public event Action? Changed;

    public InspectorOverlay? Build()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Settings.Enabled)
        {
            return null;
        }

        if (_overlay is not null)
        {
            return _overlay;
        }

        if (BuildHost() is not { } host)
        {
            return null;
        }

        var overlay = new InspectorOverlay(host, _layer, Input.Index);
        overlay.SurfaceCreated += Prepare;
        overlay.Changed += OnOverlayChanged;
        _outputs = new OutputsPanel(_options.Driver);
        overlay.Add(_outputs);
        if (_options.Services?.Find<IToplevelModel>() is { } toplevels)
        {
            overlay.Add(new WindowsPanel(toplevels));
        }

        if (_options.Services?.Find<IClientDirectory>() is { } clients)
        {
            _clients = new ClientsPanel(clients);
            overlay.Add(_clients);
        }

        _scene = new ScenePanel(_options.Scene.Root, _options.Driver);
        overlay.Add(_scene);
        _memory = new MemoryPanel();
        overlay.Add(_memory);
        _options.AddPanels?.Invoke(overlay);
        foreach (var panel in Settings.Panels)
        {
            overlay.Show(panel);
        }

        _overlay = overlay;
        ApplyRefresh();
        return overlay;
    }

    public OutputView? DefaultView()
    {
        if (Settings.Output is { } name)
        {
            foreach (var view in _options.Driver.Views)
            {
                if (view.Output.Name == name)
                {
                    return view;
                }
            }
        }

        if (_options.DefaultOutput?.Invoke() is { } chosen)
        {
            return chosen;
        }

        if (_options.PointerPosition is { } pointer)
        {
            var (x, y) = pointer();
            if (_options.Driver.ViewAt(x, y) is { } under)
            {
                return under;
            }
        }

        return _options.Driver.Views.FirstOrDefault();
    }

    public void Start()
    {
        if (!Settings.Enabled || !Settings.Open)
        {
            return;
        }

        if (Build() is { } overlay && DefaultView() is { } view)
        {
            overlay.Open(view);
        }

        Line(Describe());
    }

    public string Describe() => InspectorIpc.Describe(_overlay, Declined);

    public void Toggle()
    {
        if (Build() is { } overlay)
        {
            if (overlay.IsOpen)
            {
                overlay.Close();
            }
            else if (DefaultView() is { } view)
            {
                overlay.Open(view);
            }
        }

        Line(Describe());
    }

    public void Close() => _overlay?.Close();

    public void Apply(InspectorSettings next)
    {
        ArgumentNullException.ThrowIfNull(next);
        Settings = next;
        if (_overlay is not { } overlay)
        {
            return;
        }

        if (!next.Enabled)
        {
            overlay.Close();
        }

        if (overlay.Surface is { } surface)
        {
            surface.FontSize = (float)next.FontSize;
        }

        ApplyRefresh();
    }

    public void Register(IpcMethodRegistry methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        if (_options.Report is not { } report)
        {
            throw new InvalidOperationException("an inspector session registers its methods only with a Report");
        }

        InspectorIpc.Register(methods, report, new InspectorIpcOptions
        {
            Overlay = Build,
            Driver = _options.Driver,
            DefaultOutput = DefaultView,
            Declined = () => Declined,
        });
        report.Register(methods, "inspector/input", "inspector-input", () => report.Line(
            $"INSPECTOR-INPUT motions={Input.Motions} presses={Input.Presses} scrolls={Input.Scrolls} keys={Input.Keys} touches={Input.Touches}"));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _options.Driver.Removed -= OnOutputRemoved;
        _options.Driver.LayoutChanged -= OnLayoutChanged;
        _options.Seat.Keyboard.ModifiersChanged -= Input.ForwardModifiers;
        _options.Seat.Keyboard.KeymapChanged -= OnKeymapChanged;
        if (_ownedLayer is not null)
        {
            _options.Driver.Added -= _order;
            _options.Driver.BeforeRepaint -= _order;
        }

        _overlay?.Dispose();
        _overlay = null;
        if (_ownedLayer is { IsDestroyed: false } owned)
        {
            owned.Destroy();
        }

        if (_driver is not null)
        {
            _settle?.Remove(_driver);
        }

        if (_ownsHost)
        {
            _driver?.Dispose();
            _host?.Dispose();
        }

        _driver = null;
        _host = null;
    }

    private ImGuiUIHost? BuildHost()
    {
        if (_host is not null)
        {
            return _host;
        }

        if (_hostDeclined is not null)
        {
            return null;
        }

        if (_options.Renderer is not { } renderer)
        {
            _hostDeclined = "no renderer to draw the inspector on";
            return null;
        }

        var host = Renderers.ImGuiHosts.For(
            renderer, _options.Allocator, _options.RenderNode, out var declined, out var vulkanDeclined,
            new ImGuiHostOptions { FontSize = (float)Settings.FontSize });
        if (vulkanDeclined is not null)
        {
            InspectorLog.Log.Warn($"imgui cannot draw on the renderer's VulkanDevice: {vulkanDeclined}");
        }

        if (host is null)
        {
            _hostDeclined = declined ?? "no ImGui host could be built";
            return null;
        }

        InspectorLog.Log.Info($"the inspector draws on the {host.Name} host");
        _host = host;
        _driver = new UIDriver(host, _options.Loop);
        _driver.Start();
        _settle?.Add(_driver);
        return host;
    }

    private void Prepare(ImGuiUISurface surface)
    {
        surface.KeyText = _options.Seat.Keyboard;
        surface.FontSize = (float)Settings.FontSize;
        surface.Modifiers = Masks();
        if (_options.Services?.Find<ISelectionStore>() is { } store)
        {
            surface.SetClipboard(store, _options.Loop);
        }
    }

    private ImGuiModifierMasks Masks() =>
        _options.Seat.Keyboard.Keymap is not { } keymap
            ? ImGuiModifierMasks.Standard
            : ImGuiModifierMasks.FromIndices(
                keymap.GetModIndex("Control"), keymap.GetModIndex("Shift"),
                keymap.GetModIndex("Mod1"), keymap.GetModIndex("Mod4"));

    private void ApplyRefresh()
    {
        var hz = Settings.RefreshHz;
        if (_outputs is { } outputs)
        {
            outputs.RefreshHz = hz;
        }

        if (_memory is { } memory)
        {
            memory.RefreshHz = Math.Min(hz, 4);
        }

        if (_scene is { } scene)
        {
            scene.RefreshHz = Math.Min(hz, 4);
        }

        if (_clients is { } clients)
        {
            clients.RefreshHz = Math.Min(hz, 2);
        }

        _overlay?.Invalidate();
    }

    private void Order()
    {
        if (_ownedLayer is not { IsDestroyed: false } layer)
        {
            return;
        }

        if (_options.Below is { IsDestroyed: false } below && ReferenceEquals(below.Parent, layer.Parent))
        {
            layer.PlaceBelow(below);
        }
        else
        {
            layer.RaiseToTop();
        }
    }

    private void OnOverlayChanged()
    {
        Order();
        if (!IsOpen)
        {
            Input.Release();
        }

        Changed?.Invoke();
    }

    private void OnOutputRemoved(OutputView view)
    {
        OutputView? fallback = null;
        foreach (var candidate in _options.Driver.Views)
        {
            if (!ReferenceEquals(candidate, view))
            {
                fallback = candidate;
                break;
            }
        }

        _overlay?.Remove(view, fallback);
    }

    private void OnLayoutChanged() => _overlay?.Refit();

    private void OnKeymapChanged()
    {
        if (_overlay?.Surface is { } surface)
        {
            surface.Modifiers = Masks();
        }
    }

    private void Line(string text)
    {
        if (_options.Report is { } report)
        {
            report.Line(text);
        }
        else
        {
            BasinReport.Line(text);
        }
    }
}
