using System.Diagnostics;
using Basin;
using Basin.Capabilities;
using Basin.Cli;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Plasma;
using Basin.Renderers;
using Basin.Scene;
using Basin.Shell.Xdg;
using Microsoft.Extensions.Logging;
using Wayland.Server;

namespace PlasmaHost;

internal sealed partial class PlasmaHost : IDisposable
{
    private readonly PlasmaHostOptions _options;
    private readonly ILogger _log;
    private readonly IRenderer _renderer;
    private readonly IAllocator? _deviceAllocator;
    private readonly Basin.Host.BasinHost _host;
    private readonly OutputLayout _layout = new();
    private readonly Scene _scene = new();
    private readonly BasinServices _services;
    private readonly OutputDriver _outputs;
    private readonly PlasmaShellPlacement _placement;
    private readonly PlasmaShellManager _manager;
    private readonly Basin.Desktop.FractionalScaleManager _fractionalScale;
    private readonly PlasmaHostWindows _windows;
    private readonly PlasmaHostSeat _seat;
    private readonly Basin.Seat.Seat _coreSeat;
    private readonly Basin.Desktop.SessionLockManager _sessionLock;
    private readonly ILockOverlaySurfaces _lockOverlays;
    private Basin.Desktop.SessionLockSceneDriver _lockDriver = null!;
    private readonly List<SceneSurface> _overlayScenes = [];
    private readonly List<SlideEffect> _slides = [];
    private readonly PlasmaHostScreencast? _screencast;
    private readonly PlasmaHostFrames _frames;
    private readonly Basin.Backend.Headless.HeadlessBackend _virtualBackend;
    private StdinCommands? _stdin;
    private Process? _shell;
    private KwinOutputSettings? _kdeOutputs;
    private bool _running = true;
    private bool _commandLocked;

    public static int Run(PlasmaHostOptions options, ILogger log, out long rendered)
    {
        BasinCounters.Reset();
        rendered = 0;
        int status;
        try
        {
            using var host = new PlasmaHost(options, log);
            status = host.RunLoop();
            rendered = host._outputs.PrimaryRendered;
            host.WriteScreenshot(options.Screenshot);
        }
        catch (Exception error) when (error is InvalidOperationException or DllNotFoundException or IOException)
        {
            log.LogError("{Reason}", error.Message);
            return 1;
        }

        Console.WriteLine(
            $"FRAMES {rendered} LIVE {(BasinCounters.Enabled ? BasinCounters.LiveObjects.ToString() : "untracked")}");
        if (BasinCounters.Enabled && (BasinCounters.LiveObjects != 0 || BasinCounters.PendingFrees != 0))
        {
            BasinCounters.WriteCensus(Console.Error);
        }

        return status;
    }

    internal PlasmaHost(PlasmaHostOptions options, ILogger log)
    {
        _options = options;
        _log = log;

        var rendererName = options.Renderer;
        const string renderNode = "/dev/dri/renderD128";
        var stack = RendererCatalog.CreateWithFallback(
            ref rendererName,
            File.Exists(renderNode) ? renderNode : null,
            fallback => log.LogWarning(
                "{Renderer} renderer unavailable ({Reason}); falling back to {Fallback}",
                fallback.From, fallback.Reason ?? "no render node", fallback.To));
        _renderer = stack.Renderer;
        _deviceAllocator = stack.DeviceAllocator;

        _host = Basin.Host.BasinHost.Create(
            Basin.Host.HostOptions.ForBackend(options.Backend.ToString().ToLowerInvariant()));

        var capturePack = new SceneCapturePack(_scene, _layout);
        capturePack.Capture.Renderer = _renderer;
        var cursorTheme = new Basin.Capabilities.Defaults.CursorImageTheme();
        var inputSink = new Basin.Seat.Backends.HookInputSink();
        _frames = new PlasmaHostFrames(_renderer, log);
        _services = _host.CreateServices()
            .Use(_frames.Capability)
            .Use(_layout)
            .Use(_scene)
            .With(capturePack)
            .With(new DrmCapabilityPack(_renderer, _host.Drm))
            .Use<ICursorTheme>(cursorTheme)
            .Use<IInputSink>(inputSink)
            .Use<IActivationTokens>(new Basin.Capabilities.Defaults.DefaultActivationTokens())
            .Use<Basin.Capabilities.IFakeInputAuthority>(new PlasmaHostFakeInputAuthority())
            .Use<IBell>(Basin.Capabilities.Defaults.SilentBell.Instance);

        if (OperatingSystem.IsLinux() &&
            PlasmaHostScreencast.TryCreate(_host.Loop, capturePack.Capture, _layout, log) is { } screencast)
        {
            _screencast = screencast;
            _services.Use<IScreencastPublisher>(screencast);
            capturePack.Capture.AddDamageObserver(screencast);
        }

        _virtualBackend = new Basin.Backend.Headless.HeadlessBackend(_host.Loop);
        var virtualOutputs = new PlasmaHostVirtualOutputs(_virtualBackend);
        _services.Use<IVirtualOutputFactory>(virtualOutputs);

        var pack = DesktopPack.For("plasma-host") + PlasmaPack.Default;
        if (_host.Drm is null)
        {
            pack = pack.Without("wp_drm_lease_device_v1");
        }

        _services.Install(pack);
        if (_renderer.Device is { } renderDevice)
        {
            _services.Install(new LinuxDmabufModule(_renderer.DmabufTextureFormats, renderDevice.DevicePath));
        }

        _services.Freeze();

        _placement = _services.Require<PlasmaShellPlacement>();
        _manager = _services.Require<PlasmaShellManager>();
        _fractionalScale = _services.Require<Basin.Desktop.FractionalScaleManager>();

        var seat = _services.Require<Basin.Seat.Seat>();
        var decorations = _services.Require<XdgDecorationManager>();
        decorations.DefaultMode = DecorationMode.ServerSide;
        decorations.ChooseMode = (_, preference) => preference ?? DecorationMode.ServerSide;

        _outputs = new OutputDriver(_host, _scene, _layout, _renderer, _deviceAllocator)
        {
            Capture = capturePack,
            Frames = _services.Require<IFrameClock>(),
            Requested = options.Outputs,
            Scales = options.Scales,
            ContinuousRepaint = options.Frames > 0,
            NestedName = index => $"plasma-host-{index + 1}",
            HeadlessMode = new OutputMode(1920, 1080, 60_000),
        };
        virtualOutputs.Outputs = _outputs;
        _presenceTracker = new SurfacePresenceTracker(_layout, _fractionalScale.AnnounceScale);
        _outputs.Added += view => _presenceTracker.AddOutput(view.Output, view.Global);
        _outputs.Removed += view => _presenceTracker.RemoveOutput(view.Output);
        _outputs.Emptied += Stop;
        _outputs.ModesetRefused += card =>
            log.LogError("modeset refused by {Output} in every mode", card.Name);
        _outputs.Added += view => Console.WriteLine(
            $"OUTPUT {view.Output.Name} {view.Output.CurrentMode.Width}x{view.Output.CurrentMode.Height}");
        _outputs.Painted += _ => UpdateSurfacePresence();
        _outputs.LayoutChanged += UpdateSurfacePresence;
        _outputs.Added += view => view.Scene!.BeforeRepaint += tick =>
        {
            var animating = false;
            for (var i = 0; i < _slides.Count; i++)
            {
                animating |= _slides[i].Step(tick);
            }

            if (animating)
            {
                view.Scheduler?.ScheduleRepaint();
            }
        };

        _windows = new PlasmaHostWindows(_layout, seat, _placement, _manager);
        _frames.MenuLayer = _placement.Overlay;
        _windows.WireDecorations(
            decorations,
            _services.Require<Basin.Desktop.KdeServerDecorationManager>(),
            _services.Require<ServerDecorationPaletteManager>(),
            _frames);
        _services.Require<XdgToplevelIconManager>().IconChanged += _windows.SetIconName;
        var shadows = _services.Require<ShadowManager>();
        var slides = _services.Require<SlideManager>();
        void AttachEffects(SceneSurface scene)
        {
            _ = new ShadowEffect(scene, shadows);
            var slide = new SlideEffect(scene, slides, _ => ScreenAreaOf(scene));
            slide.Started += _outputs.ScheduleAll;
            _slides.Add(slide);
            scene.Destroyed += () => _slides.Remove(slide);
        }

        _windows.SceneCreated += AttachEffects;
        _placement.SceneCreated += (shell, scene) => AttachEffects(scene);
        _windows.Attach(
            _services.Require<XdgShell>(),
            _services.Require<XdgToplevelSource>(),
            _services.Require<LayerShell>());
        var toplevelSource = _services.Require<XdgToplevelSource>();
        var toplevels = _services.Require<IToplevelModel>();
        capturePack.Attach(toplevels, surface =>
            _windows.ViewFor(surface) is { } view
                ? new ToplevelCaptureTrees(view.Scene.Tree, null)
                : null);
        var appMenus = _services.Require<AppMenuManager>();
        appMenus.AddressChanged += (surface, service, path) =>
        {
            if (_windows.ViewFor(surface) is { } view)
            {
                toplevelSource.SetAppMenu(view.Xdg, service, path);
            }
        };
        _windows.ViewMapped += view =>
        {
            if (appMenus.MenuOf(view.Surface) is { } menu)
            {
                toplevelSource.SetAppMenu(view.Xdg, menu.ServiceName, menu.ObjectPath);
            }
        };
        _outputs.LayoutChanged += _windows.ArrangeLayerSurfaces;
        _outputs.LayoutChanged += _windows.ClampIntoLayout;
        _services.Require<ScreenEdgeManager>().Changed += _windows.ArrangeLayerSurfaces;
        if (_services.Find<IOutputConfiguration>() is { } configuration)
        {
            configuration.Applied += _ => _outputs.Relayout();
        }

        _seat = new PlasmaHostSeat(
            _host, _services, _windows, _outputs, _scene, _layout, cursorTheme, inputSink, Stop);

        _coreSeat = seat;
        _sessionLock = _services.Require<Basin.Desktop.SessionLockManager>();
        _lockOverlays = _services.Require<ILockOverlaySurfaces>();
        _windows.FocusLocked = () => _sessionLock.IsLocked || _commandLocked;
        _windows.ViewMapped += view =>
        {
            if (_sessionLock.IsLocked)
            {
                RaiseOverlay(view.Surface, view.Tree.X, view.Tree.Y);
            }
        };
        WireSessionLock();

        _outputs.CreateInitialOutputs();
        if (LoadKdeOutputSettings() is { } kdeOutputs)
        {
            _kdeOutputs = kdeOutputs;
            ApplyKdeOutputSettings();
            _outputs.Added += _ => ApplyKdeOutputSettings();
        }

        if (_options.Backend == BackendKind.Drm && _outputs.Views.Count == 0)
        {
            throw new InvalidOperationException("no connected output");
        }

        if (_host.Parent is { } parent)
        {
            parent.ParentGone += Stop;
        }
    }

    internal void Stop() => _running = false;

    private KwinOutputSettings? LoadKdeOutputSettings()
    {
        if (string.Equals(_options.Config, "false", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (_options.Config is { } path)
        {
            if (KwinOutputSettings.TryLoad(path, out var named))
            {
                return named;
            }

            _log.LogWarning("{Path} is not a kwin output configuration", path);
            return null;
        }

        return KwinOutputSettings.TryLoad(out var settings) ? settings : null;
    }

    private void ApplyKdeOutputSettings()
    {
        if (_kdeOutputs is not { } settings || _services.Find<IOutputConfiguration>() is not { } configuration)
        {
            return;
        }

        var outputs = new List<IOutput>(_outputs.Views.Count);
        foreach (var view in _outputs.Views)
        {
            outputs.Add(view.Output);
        }

        var entries = settings.EntriesFor(outputs);
        if (entries.Count == 0)
        {
            return;
        }

        if (_options.Scales.Length > 0)
        {
            var kept = new List<OutputConfigurationEntry>(entries.Count);
            foreach (var entry in entries)
            {
                kept.Add(entry with { Scale = null });
            }

            entries = kept;
        }

        if (settings.Apply(configuration, entries))
        {
            foreach (var entry in entries)
            {
                _log.LogInformation(
                    "{Output} takes the kwin settings: scale {Scale}, mode {Mode}",
                    entry.Output.Name,
                    entry.Output.Scale,
                    $"{entry.Output.CurrentMode.Width}x{entry.Output.CurrentMode.Height}");
            }
        }
        else
        {
            _log.LogWarning("the kwin output settings did not apply");
        }
    }

    private Box ScreenAreaOf(SceneSurface scene)
    {
        var (x, y) = scene.Tree.ScenePosition;
        var output = _layout.OutputAt(x, y);
        return output is null ? _layout.Bounds : _layout.BoxOf(output);
    }

    private void WireSessionLock()
    {
        _lockDriver = new Basin.Desktop.SessionLockSceneDriver(
            _sessionLock, _coreSeat, _placement.Layers.Lock, _layout, _placement.Layers.SetLocked);
        _sessionLock.Locked += () => _commandLocked = false;
        _lockDriver.Locked += () =>
        {
            foreach (var view in _windows.Views)
            {
                RaiseOverlay(view.Surface, view.Tree.X, view.Tree.Y);
            }

            foreach (var (layer, scene) in _windows.LayerSurfaces)
            {
                if (scene is not null)
                {
                    RaiseOverlay(layer.Surface, scene.Tree.X, scene.Tree.Y);
                }
            }

            foreach (var shell in _manager.Surfaces)
            {
                if (_placement.SceneOf(shell) is { } scene)
                {
                    RaiseOverlay(shell.Surface, scene.Tree.X, scene.Tree.Y);
                }
            }

            Console.WriteLine("LOCKED");
        };
        _lockDriver.Unlocked += () =>
        {
            foreach (var scene in _overlayScenes)
            {
                if (!scene.IsDestroyed)
                {
                    scene.Destroy();
                }
            }

            _overlayScenes.Clear();
            if (_windows.Views.Count > 0)
            {
                _windows.Focus(_windows.Views[0]);
            }

            Console.WriteLine("UNLOCKED");
        };
        _lockDriver.Abandoned += () => Console.WriteLine("LOCK ABANDONED (staying blanked)");
        _lockDriver.LockSurfaceAdded += (_, _) =>
        {
            foreach (var overlay in _overlayScenes)
            {
                if (!overlay.IsDestroyed)
                {
                    overlay.Tree.RaiseToTop();
                }
            }
        };
    }

    private void LockNow()
    {
        if (_sessionLock.IsLocked || _commandLocked)
        {
            return;
        }

        _commandLocked = true;
        _lockDriver.LockNow();
    }

    private void UnlockNow()
    {
        if (_sessionLock.IsLocked || !_commandLocked)
        {
            return;
        }

        _commandLocked = false;
        _lockDriver.UnlockNow();
    }

    private void RaiseOverlay(Surface surface, int x, int y)
    {
        if (!surface.IsMapped || !_lockOverlays.IsAllowed(surface))
        {
            return;
        }

        foreach (var existing in _overlayScenes)
        {
            if (!existing.IsDestroyed && ReferenceEquals(existing.Surface, surface))
            {
                return;
            }
        }

        var scene = new SceneSurface(_placement.Layers.Lock, surface);
        scene.Tree.SetPosition(x, y);
        _overlayScenes.Add(scene);
    }

    private int RunLoop()
    {
        Console.WriteLine($"SOCKET {_host.Socket}");
        Console.Out.Flush();

        var interrupt = _host.Loop.AddSignal(Signal.Interrupt, _ => Stop());
        var terminate = _host.Loop.AddSignal(Signal.Terminate, _ => Stop());
        _stdin = new StdinCommands(_host.Loop, HandleCommand);
        _stdin.CommandFailed += (command, error) =>
        {
            Console.WriteLine($"COMMAND FAILED {command}: {error.Message}");
            Console.Out.Flush();
        };

        _seat.CenterCursor();
        StartShell();

        var frames = _options.Frames;
        while (_running && (frames == 0 || _outputs.PrimaryRendered < frames))
        {
            _host.Loop.Dispatch(16);
            _host.Parent?.Flush();
        }

        _stdin.Stop();
        interrupt.Remove();
        terminate.Remove();
        return 0;
    }

    private void StartShell()
    {
        if (_options.Shell is not { Length: > 0 } shell)
        {
            return;
        }

        try
        {
            _shell = BasinDiagnostics.StartClient(
                shell, _host.Socket, [("QT_QPA_PLATFORM", "wayland"), ("DISPLAY", null)]);
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException)
        {
            _log.LogError("could not start {Shell}: {Reason}", shell, error.Message);
        }
    }

    private void HandleCommand(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts)
        {
            case ["move", var x, var y]:
                _seat.Warp(double.Parse(x), double.Parse(y));
                break;
            case ["button", var code, var state]:
                _seat.InjectButton(uint.Parse(code), state == "1");
                break;
            case ["key", var code, var state]:
                _seat.InjectKey(uint.Parse(code), state == "1");
                break;
            case ["shot", var path]:
                WriteScreenshot(path);
                break;
            case ["lock"]:
                LockNow();
                break;
            case ["unlock"]:
                UnlockNow();
                break;
            case ["where"]:
                PrintState();
                break;
            case ["quit"]:
                Stop();
                break;
        }
    }

    private void PrintState()
    {
        Console.WriteLine($"POINTER {_seat.PointerX} {_seat.PointerY}");
        foreach (var view in _outputs.Views)
        {
            var box = _layout.BoxOf(view.Output);
            Console.WriteLine(
                $"AREA {view.Output.Name} output={box} usable={_windows.UsableArea(view.Output)}");
        }

        foreach (var shell in _manager.Surfaces)
        {
            if (!shell.Surface.IsMapped)
            {
                continue;
            }

            var scene = _placement.SceneOf(shell);
            var current = shell.Surface.Current;
            var box = scene is null
                ? new Box(0, 0, current.Width, current.Height)
                : new Box(scene.Tree.X, scene.Tree.Y, current.Width, current.Height);
            var layer = PlasmaShellPlacement.LayerOf(shell.Role) is { } kind
                ? kind.ToString().ToLowerInvariant()
                : "windows";
            Console.WriteLine(
                $"SURFACE role={RoleName(shell.Role)} layer={layer} box={box} " +
                $"hidden={(shell.IsAutoHidden ? "yes" : "no")} focusable={(shell.Focusable ? "yes" : "no")}");
        }

        foreach (var view in _windows.Views)
        {
            var (width, height) = view.GeometrySize();
            var palette = _services.Require<ServerDecorationPaletteManager>().PaletteOf(view.Surface)?.Palette ?? "";
            Console.WriteLine(
                $"WINDOW \"{view.Xdg.Title}\" box={new Box(view.Tree.X, view.Tree.Y, width, height)} " +
                $"focused={(ReferenceEquals(view, _windows.FocusedView) ? "yes" : "no")} " +
                $"maximized={(view.Maximized ? "yes" : "no")} palette=\"{palette}\"");
        }

        foreach (var (layer, scene) in _windows.LayerSurfaces)
        {
            if (scene is null)
            {
                continue;
            }

            var current = layer.Surface.Current;
            Console.WriteLine(
                $"LAYER ns={layer.Namespace} layer={layer.Layer.ToString().ToLowerInvariant()} " +
                $"box={new Box(scene.Tree.X, scene.Tree.Y, current.Width, current.Height)}");
        }

        Console.Out.Flush();
    }

    private static string RoleName(PlasmaShellRole role) => role switch
    {
        PlasmaShellRole.OnScreenDisplay => "onscreendisplay",
        PlasmaShellRole.CriticalNotification => "criticalnotification",
        PlasmaShellRole.AppletPopup => "appletpopup",
        _ => role.ToString().ToLowerInvariant(),
    };

    private void WriteScreenshot(string? screenshotPath)
    {
        if (screenshotPath is not { Length: > 0 } path ||
            _outputs.Views.FirstOrDefault() is not { } view)
        {
            return;
        }

        var cursor = default(CursorBlit);
        if (_seat.CursorSprite is { } sprite)
        {
            var box = _layout.BoxOf(view.Output);
            var scale = view.Output.Scale;
            cursor = new CursorBlit(sprite.Buffer, new Box(
                (int)((_seat.PointerX - box.X) * scale) - sprite.HotspotX,
                (int)((_seat.PointerY - box.Y) * scale) - sprite.HotspotY,
                sprite.Buffer.Width,
                sprite.Buffer.Height));
        }

        if (SceneScreenshot.Write(_scene, _renderer, view.Output, path, cursor))
        {
            var mode = view.Output.CurrentMode;
            Console.WriteLine($"SHOT {path} {mode.Width}x{mode.Height}");
            Console.Out.Flush();
        }
    }

    public void Dispose()
    {
        BasinDiagnostics.StopClient(_shell);
        _screencast?.Dispose();
        _outputs.Dispose();
        _virtualBackend.Dispose();
        _scene.Root.Destroy();
        _seat.Dispose();
        _services.Dispose();
        _frames.Dispose();
        _deviceAllocator?.Dispose();
        _host.Dispose();
        _renderer.Dispose();
    }
}
