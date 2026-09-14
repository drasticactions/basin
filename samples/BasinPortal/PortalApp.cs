using System.Reflection;
using Basin;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Freedesktop;
using Basin.Portal;
using Basin.Portal.Client;
using Basin.WindowManager.Skia.Protocol;
using Basin.Portal.Prompts.Avalonia;
using Basin.Portal.Prompts.Skia;
using Basin.Render.Skia;
using Basin.UI.Avalonia;
using Basin.UI.Skia;
using Basin.Screencast.PipeWire;
using Tmds.DBus.Protocol;
using SkiaSharp;
using Wayland;

namespace BasinPortal;

internal sealed class PortalApp : IDisposable
{
    private readonly WlDisplay _display;
    private readonly ClientLoop _loop;
    private readonly ClientGlobals _globals = new();
    private readonly ClientOutputs _outputs;
    private readonly BasinLogger _log;
    private readonly List<IDisposable> _owned = [];
    private readonly string _busName;
    private readonly PromptStyle _promptStyle;
    private BasinServices? _services;
    private PortalBus? _bus;
    private ImageCopyCaptureClient? _capture;
    private ZwlrLayerShellV1? _layerShell;
    private PromptHost? _promptHost;
    private AvaloniaPromptHost? _avaloniaHost;
    private ClientSeat? _seat;
    private SKTypeface? _typeface;

    public PortalApp(string? socket, string busName, PromptStyle promptStyle, BasinLogger log)
    {
        _log = log;
        _busName = busName;
        _promptStyle = promptStyle;
        _display = socket is { Length: > 0 } ? WlDisplay.Connect(socket) : WlDisplay.Connect();
        _loop = new ClientLoop(_display);
        _outputs = new ClientOutputs(_globals);

        var registry = _display.GetRegistry();
        registry.Global += (_, e) =>
        {
            switch (e.Interface)
            {
                case "wl_output":
                    _outputs.OnGlobal(registry, e.Name, e.Version);
                    break;
                case "zwlr_layer_shell_v1":
                    _layerShell = registry.Bind<ZwlrLayerShellV1>(e.Name, Math.Min(e.Version, 4));
                    break;
                default:
                    _globals.Bind(registry, e.Name, e.Interface, e.Version);
                    break;
            }
        };
        registry.GlobalRemove += (_, e) => _outputs.OnGlobalRemoved(e.Name);
        _display.Roundtrip();
        _outputs.AttachXdgOutputs();
        _display.Roundtrip();
    }

    public int Run()
    {
        var keymap = new ClientKeymap();
        _owned.Add(keymap);
        var seat = _globals.Seat ?? throw new InvalidOperationException("the compositor exposes no wl_seat");
        _seat = new ClientSeat(seat, _globals.SeatCapabilities, keymap);
        _globals.SeatCapabilitiesChanged += _seat.SetCapabilities;
        _owned.Add(_seat);

        ForeignToplevelList? toplevels = _globals.ToplevelList is { } list ? new ForeignToplevelList(list) : null;
        if (toplevels is not null)
        {
            _owned.Add(toplevels);
        }

        if (_globals.Capture is { } capture && _globals.Shm is { } shm)
        {
            _capture = new ImageCopyCaptureClient(capture, _globals.OutputSources, _globals.ToplevelSources, shm, _globals.Dmabuf, _outputs, toplevels, _display);
            _owned.Add(_capture);
        }

        var prompts = _promptStyle == PromptStyle.Avalonia ? StartAvaloniaPrompts() : StartSkiaPrompts(keymap);

        WireSeat();

        _bus = new PortalBus(_loop.Loop, PortalBus.DefaultBusName == _busName ? null : DBusAddress.Session, _busName);
        _services = new BasinServices(_loop.Loop);
        _services.Use(_bus);
        _services.Use(_outputs.Layout);
        if (_capture is not null)
        {
            _services.Use<IScreenCapture>(_capture);
        }

        _services.Use<IPortalPrompts>(prompts);
        _services.Use<IAppInfoResolver>(new DesktopAppInfoResolver(new DesktopEntries(), new IconSearch()));
        _services.Use<IActiveKeymap>(keymap);
        _services.Use<IKeymapLookup>(keymap);
        _services.Use(new Basin.Portal.PortalOptions());
        if (toplevels is not null)
        {
            _services.Use<IToplevelModel>(toplevels);
        }

        var pack = PortalPack.Default;
        pack = RegisterOrDrop(pack, _capture is { IsAvailable: true }, "org.freedesktop.impl.portal.Screenshot");
        RegisterScreenCast(ref pack);
        RegisterRemoteDesktop(ref pack, keymap);
        RegisterClipboard(ref pack);
        RegisterShortcuts(ref pack);
        RegisterInputCapture(ref pack);

        _services.Install(pack);
        _services.Freeze();
        _log.Info($"portal ready on {_busName}: {string.Join(", ", _services.Modules.Keys)}");

        var terminate = _loop.Loop.AddSignal(15, _ => _loop.Stop());
        var interrupt = _loop.Loop.AddSignal(2, _ => _loop.Stop());
        try
        {
            _loop.Run();
        }
        finally
        {
            terminate.Remove();
            interrupt.Remove();
        }

        return 0;
    }

    public void Dispose()
    {
        _services?.Dispose();
        _bus?.Dispose();
        for (var i = _owned.Count - 1; i >= 0; i--)
        {
            _owned[i].Dispose();
        }

        _owned.Clear();
        _typeface?.Dispose();
        _outputs.Dispose();
        if (_layerShell is { IsDestroyed: false } layerShell)
        {
            layerShell.Dispose();
        }

        _globals.Dispose();
        _loop.Dispose();
        if (!_display.IsDestroyed)
        {
            _display.Dispose();
        }
    }

    private IPortalPrompts StartSkiaPrompts(ClientKeymap keymap)
    {
        _promptHost = new PromptHost(_globals, _layerShell, _outputs, _loop, _log);
        _owned.Add(_promptHost);
        var prompts = new SkiaPortalPrompts(_promptHost, _outputs.Layout)
        {
            Typeface = LoadTypeface(),
            Keymap = keymap,
        };
        _owned.Add(prompts);
        return prompts;
    }

    private IPortalPrompts StartAvaloniaPrompts()
    {
        _avaloniaHost = new AvaloniaPromptHost(_globals, _layerShell, _outputs, _loop, _log);
        _owned.Add(_avaloniaHost);
        var ui = BasinPlatform.Start<PromptApp>(new BasinPlatformOptions
        {
            EventLoop = _loop.Loop,
            Theme = UIThemeVariant.Dark,
        });
        _owned.Add(ui);
        _owned.Add(new ToolkitPump(ui, _loop));
        var prompts = new AvaloniaPortalPrompts(_avaloniaHost, _outputs.Layout, ui);
        _owned.Add(prompts);
        _log.Info($"prompts drawn by Avalonia");
        return prompts;
    }

    private void WireSeat()
    {
        if (_seat is not { } seat)
        {
            return;
        }

        if (_promptHost is { } host)
        {
            seat.PointerEntered += (id, x, y) => host.Dispatch(id, s => s.PointerMove(x, y));
            seat.PointerMoved += (id, x, y) => host.Dispatch(id, s => s.PointerMove(x, y));
            seat.PointerLeft += id => host.Dispatch(id, s => s.PointerLeave());
            seat.PointerButton += (id, button, pressed) => host.Dispatch(id, s => s.PointerButton(button, pressed));
            seat.PointerAxis += (id, dx, dy) => host.Dispatch(id, s => s.PointerAxis(dx, dy));
            seat.Key += (id, key, pressed) => host.Dispatch(id, s => s.Key(key, pressed));
            seat.Modifiers += (id, depressed, latched, locked, group) => host.Dispatch(id, s => s.Modifiers(depressed, latched, locked, group));
        }

        if (_avaloniaHost is { } avalonia)
        {
            seat.PointerEntered += (id, x, y) => avalonia.Dispatch(id, s => s.PointerEnter(x, y));
            seat.PointerMoved += (id, x, y) => avalonia.Dispatch(id, s => s.PointerMove(x, y));
            seat.PointerLeft += id => avalonia.Dispatch(id, s => s.PointerLeave());
            seat.PointerButton += (id, button, pressed) => avalonia.Dispatch(id, s => s.PointerButton(button, pressed));
            seat.PointerAxis += (id, dx, dy) => avalonia.Dispatch(id, s => s.PointerAxis(dx, dy));
            seat.Key += (id, key, pressed) => avalonia.Dispatch(id, s => s.Key(key, pressed));
            seat.Modifiers += (id, depressed, latched, locked, group) => avalonia.Dispatch(id, s => s.Modifiers(depressed, latched, locked, group));
        }
    }

    private void RegisterScreenCast(ref Basin.ProtocolPack pack)
    {
        if (_capture is { } capture)
        {
            capture.ProbeDmabuf();
            var publisher = PipeWireScreencastPublisher.TryCreate(_loop.Loop, capture, _outputs.Layout, capture.Allocator, "basin-portal");
            if (publisher is not null)
            {
                _owned.Add(publisher);
                _services!.Use<IScreencastPublisher>(publisher);
                if (capture.IsAvailable)
                {
                    return;
                }
            }
        }

        pack = pack.Without("org.freedesktop.impl.portal.ScreenCast");
    }

    private void RegisterRemoteDesktop(ref Basin.ProtocolPack pack, ClientKeymap keymap)
    {
        if (_globals.VirtualPointers is null && _globals.VirtualKeyboards is null)
        {
            pack = pack.Without("org.freedesktop.impl.portal.RemoteDesktop");
            return;
        }

        var injector = new VirtualInputInjector(_globals.VirtualKeyboards, _globals.VirtualPointers, _globals.Seat, keymap);
        _owned.Add(injector);
        _services!.Use<IInputSink>(injector);
    }

    private void RegisterClipboard(ref Basin.ProtocolPack pack)
    {
        if (_globals.DataControl is not { } dataControl || _globals.Seat is not { } seat)
        {
            pack = pack.Without("org.freedesktop.impl.portal.Clipboard");
            return;
        }

        var store = new DataControlSelectionStore(dataControl, seat);
        _owned.Add(store);
        _services!.Use<ISelectionStore>(store);
    }

    private void RegisterShortcuts(ref Basin.ProtocolPack pack)
    {
        if (_globals.GlobalShortcuts is null)
        {
            pack = pack.Without("org.freedesktop.impl.portal.GlobalShortcuts");
            return;
        }

        var registry = new HyprlandShortcutRegistry(_globals.GlobalShortcuts);
        _owned.Add(registry);
        _services!.Use<IGlobalShortcuts>(registry);
    }

    private void RegisterInputCapture(ref Basin.ProtocolPack pack)
    {
        if (_globals.InputCapture is null || !Basin.Eis.EisLibrary.IsAvailable(out _))
        {
            pack = pack.Without("org.freedesktop.impl.portal.InputCapture");
            return;
        }

        var provider = new HyprlandInputCaptureProvider(_globals.InputCapture, _outputs);
        _owned.Add(provider);
        _services!.Use<IInputCaptureProvider>(provider);
    }

    private static Basin.ProtocolPack RegisterOrDrop(Basin.ProtocolPack pack, bool available, string wireInterface) =>
        available ? pack : pack.Without(wireInterface);

    private SKTypeface? LoadTypeface()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NotoSansCJK-Regular.ttc");
        if (stream is null)
        {
            return null;
        }

        using var data = SKData.Create(stream);
        _typeface = SkiaTypefaces.FromCollection(data, "Noto Sans CJK JP");
        return _typeface;
    }
}
