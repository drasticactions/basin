using Basin;
using Basin.Capabilities;
using Basin.Host;
using Basin.Inspector;
using Basin.Inspector.Ipc;
using Basin.Ipc;
using Basin.UI.ImGui;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private const string InspectorScaleName = "inspector-scale";

    private InspectorOverlay? _inspector;
    private string? _inspectorUnavailable;
    private OutputsPanel? _inspectorOutputs;
    private MemoryPanel? _inspectorMemory;
    private ScenePanel? _inspectorScene;
    private ClientsPanel? _inspectorClients;
    private readonly List<IGrabTarget> _grabTargets = [];
    private readonly Dictionary<IGrabTarget, double> _inspectorScales = [];

    internal Config CurrentConfig => _config;

    internal IReadOnlyList<IGrabTarget> GrabTargets
    {
        get
        {
            _grabTargets.Clear();
            foreach (var window in _windows)
            {
                if (window.EffectTree is { IsDestroyed: false })
                {
                    _grabTargets.Add(window);
                }
            }

            foreach (var window in _xwindows)
            {
                if (window.EffectTree is { IsDestroyed: false })
                {
                    _grabTargets.Add(window);
                }
            }

            return _grabTargets;
        }
    }

    internal IGrabTarget? FocusedTarget => FocusedGrabTarget();

    internal string TargetName(IGrabTarget window) => NameOf(window);

    internal double InspectorScaleOf(IGrabTarget window) => _inspectorScales.GetValueOrDefault(window, 1.0);

    internal void SetInspectorScale(IGrabTarget window, double scale)
    {
        if (window.EffectTree is not { IsDestroyed: false } tree)
        {
            return;
        }

        var stack = _effects.StackFor(tree);
        if (Math.Abs(scale - 1) < 0.001)
        {
            _inspectorScales.Remove(window);
            _ = stack.Remove(InspectorScaleName);
        }
        else
        {
            _inspectorScales[window] = scale;
            var (width, height) = window.GeometrySize;
            var node = stack.Get(InspectorScaleName) ?? stack.Add(Basin.Scene.TransformStack.ZOrder.Transform2D, InspectorScaleName);
            node.Matrix = RenderTransform.Multiply(
                RenderTransform.Translation(width / 2.0, height / 2.0),
                RenderTransform.Multiply(RenderTransform.Scale(scale, scale), RenderTransform.Translation(-width / 2.0, -height / 2.0)));
        }

        _report.Line(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"SCALE {NameOf(window)} scale={scale:F2}"));
        ScheduleEffectRepaint();
    }

    internal string? ApplyLiveSetting(string key, string value)
    {
        if (!Basin.Config.TomlValue.TryParse(value, out var literal))
        {
            return $"'{value}' is not a TOML value";
        }

        var dot = key.LastIndexOf('.');
        var draft = SettingsDraftOrOpen();
        if (SettingsCatalog.Find(key) is { } catalog)
        {
            draft.Set(catalog, literal);
        }
        else
        {
            draft.Set(key[..dot], key[(dot + 1)..], literal);
        }

        ApplySettingsDraft();
        var error = draft.ErrorOf(draft.LastPath ?? string.Empty);
        _report.Line($"SETTING {key}={value} applied={(error is null ? "yes" : "no")}");
        return error;
    }

    private InspectorOverlay? Inspector()
    {
        if (!_config.InspectorEnabled)
        {
            _inspectorUnavailable = "[inspector] enabled is false";
            return null;
        }

        if (_inspector is not null)
        {
            return _inspector;
        }

        if (ImGuiHost() is not { } host)
        {
            _inspectorUnavailable = _imguiDeclined ?? "no ImGui host";
            return null;
        }

        _inspectorUnavailable = null;
        var overlay = new InspectorOverlay(host, _layers.Overlay, UIIndex);
        overlay.SurfaceCreated += surface =>
        {
            PrepareImGuiSurface(surface);
            surface.FontSize = (float)_config.InspectorFontSize;
        };
        _inspectorOutputs = new OutputsPanel(_driver);
        overlay.Add(_inspectorOutputs);
        overlay.Add(new WindowsPanel(_toplevels));
        if (_services.Find<IClientDirectory>() is { } clients)
        {
            _inspectorClients = new ClientsPanel(clients);
            overlay.Add(_inspectorClients);
        }

        _inspectorScene = new ScenePanel(_scene.Root, _driver);
        overlay.Add(_inspectorScene);
        _inspectorMemory = new MemoryPanel();
        overlay.Add(_inspectorMemory);
        overlay.Add(new InspectorEffectsPanel(this));
        overlay.Add(new InspectorTransformPanel(this));
        foreach (var panel in _config.InspectorPanels)
        {
            overlay.Show(panel);
        }

        _inspector = overlay;
        ApplyInspectorRefresh(_config);
        return overlay;
    }

    private OutputView? InspectorView()
    {
        if (_config.InspectorOutput is { } name)
        {
            foreach (var view in Views)
            {
                if (view.Output.Name == name)
                {
                    return view;
                }
            }
        }

        return ViewAtCursor() ?? Views.FirstOrDefault();
    }

    private void ToggleInspector()
    {
        if (Inspector() is not { } overlay)
        {
            _report.Line(InspectorIpc.Describe(null, _inspectorUnavailable));
            return;
        }

        if (InspectorView() is not { } view)
        {
            _report.Line(InspectorIpc.Describe(overlay, null));
            return;
        }

        if (overlay.IsOpen)
        {
            overlay.Close();
            if (overlay.Surface is { } surface && ReferenceEquals(_uiRouter?.KeyboardFocus, surface))
            {
                ReleaseUIKeyboard(restore: true);
            }
        }
        else
        {
            overlay.Open(view);
        }

        _report.Line(InspectorIpc.Describe(overlay, null));
        RefreshPointer();
    }

    private void StartInspector()
    {
        if (_config.InspectorEnabled && _config.InspectorOpen && Inspector() is { } overlay && InspectorView() is { } view)
        {
            overlay.Open(view);
        }
    }

    private void RefitInspector() => _inspector?.Refit();

    private void ApplyInspectorSettings(Config next)
    {
        if (_inspector is not { } overlay)
        {
            return;
        }

        if (next.InspectorEnabled)
        {
            _inspectorUnavailable = null;
        }

        if (!next.InspectorEnabled && overlay.IsOpen)
        {
            overlay.Close();
        }

        if (overlay.Surface is { } surface)
        {
            surface.FontSize = (float)next.InspectorFontSize;
        }

        ApplyInspectorRefresh(next);
    }

    private void ApplyInspectorRefresh(Config config)
    {
        if (_inspectorOutputs is { } outputs)
        {
            outputs.RefreshHz = config.InspectorRefreshHz;
        }

        if (_inspectorMemory is { } memory)
        {
            memory.RefreshHz = Math.Min(config.InspectorRefreshHz, 4);
        }

        if (_inspectorScene is { } scene)
        {
            scene.RefreshHz = Math.Min(config.InspectorRefreshHz, 4);
        }

        if (_inspectorClients is { } clients)
        {
            clients.RefreshHz = Math.Min(config.InspectorRefreshHz, 2);
        }

        _inspector?.Invalidate();
    }

    private void RegisterInspectorCommands(IpcMethodRegistry methods) =>
        InspectorIpc.Register(methods, _report, new InspectorIpcOptions
        {
            Overlay = Inspector,
            Driver = _driver,
            DefaultOutput = InspectorView,
            Declined = () => _inspectorUnavailable,
        });

    private void DisposeInspector()
    {
        _inspector?.Dispose();
        _inspector = null;
        _inspectorScales.Clear();
    }
}
