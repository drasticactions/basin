using System.CommandLine;
using Basin.Capabilities;
using Basin;
using Basin.Backend.Headless;
using Basin.Backend.Libinput;
using Basin.Backend.Wayland;
using Basin.Cli;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Inspector;
using Basin.Ipc;
using Basin.Scene;
using Basin.Shell.River;
using Basin.Shell.Xdg;
using Wayland.Server;

namespace Inlet;

internal static class Program
{
    private static readonly RenderColor Background = new(0.06f, 0.06f, 0.08f, 1f);

    private sealed class TouchPolicy : Basin.Seat.ITouchPointerTarget
    {
        public required LayoutPointer Pointer { get; init; }

        public required Action<uint> Moved { get; init; }

        public required Action<uint, uint, bool> Button { get; init; }

        void Basin.Seat.ITouchPointerTarget.Warp(uint timeMs, double x, double y)
        {
            Pointer.Warp(x, y);
            Moved(timeMs);
        }

        void Basin.Seat.ITouchPointerTarget.Button(uint timeMs, uint button, bool pressed) =>
            Button(timeMs, button, pressed);
    }

    private static int Main(string[] args)
    {
        var cli = new BasinCommand(
            "A compositor that owns no window-management policy and hands it to a client over the river protocols.");
        var rendererOption = cli.Add(CommonOptions.Renderer(
            Basin.Renderers.RendererCatalog.Names, "vulkan"));
        var backendOption = cli.Add(CommonOptions.Backend(
            [BackendKind.Nested, BackendKind.Drm, BackendKind.Headless]));
        var outputsOption = cli.Add(CommonOptions.Outputs());
        var widthOption = cli.Add(CommonOptions.Width(1280));
        var heightOption = cli.Add(CommonOptions.Height(720));
        var scaleOption = cli.Add(CommonOptions.Scales());
        var xwaylandOption = cli.Add(new Option<bool>("--xwayland")
        {
            Description = "start Xwayland so X11 clients can connect",
        });
        var framesOption = cli.Add(CommonOptions.Frames());
        var screenshotOption = cli.Add(CommonOptions.Screenshot());
        var handoffOption = cli.Add(CommonOptions.Handoff());
        _ = IpcCli.AddOption(cli);
        var commandOption = cli.Add(CommonOptions.Command("run `sh -c <command>` on startup instead of the init executable."));
        var inspectorOption = cli.Add(new Option<bool>("--inspector")
        {
            Description = "open the inspector overlay at start",
        });

        return cli.Run(args, result =>
        {
            var backend = result.GetValue(backendOption);
            cli.ConfigureLogging(result);
            var status = Run(
                BasinLog.For("Inlet"),
                result.GetValue(rendererOption)!,
                backend.Kind == BackendKind.Drm,
                backend.Kind == BackendKind.Nested,
                Math.Max(1, result.GetValue(outputsOption)),
                result.GetValue(widthOption),
                result.GetValue(heightOption),
                result.GetValue(scaleOption)!,
                result.GetValue(xwaylandOption),
                (int)result.GetValue(framesOption),
                result.GetValue(screenshotOption),
                result.GetValue(commandOption),
                IpcCli.Read(cli, result),
                CommonOptions.ReadHandoff(result, handoffOption).Resolve(HandoffMode.Auto),
                result.GetValue(inspectorOption),
                out var rendered);
            cli.ReportFrames(rendered);
            return status;
        });
    }

    private static RenderStack CreateStack(string rendererName, BasinLogger log, out string started)
    {
        var name = rendererName;
        var stack = Basin.Renderers.RendererCatalog.CreateWithFallback(
            ref name,
            Basin.Renderers.RendererCatalog.FindRenderNode(),
            fallback => fallback.Log(log));
        started = name;
        return stack;
    }

    private static int Run(
        BasinLogger log,
        string rendererName,
        bool drm,
        bool nested,
        int outputCount,
        int width,
        int height,
        double[] scales,
        bool xwayland,
        int frames,
        string? screenshotPath,
        string? startupCommand,
        IpcChoice ipcChoice,
        HandoffMode handoff,
        bool inspectorOpen,
        out long renderedFrames)
    {
        BasinCounters.Reset();

        int status;
        long totalRendered;
        try
        {
            status = RunCompositor(
                log, rendererName, drm, nested, outputCount, width, height, scales, xwayland, frames, screenshotPath,
                startupCommand, ipcChoice, handoff, inspectorOpen, out totalRendered);
        }
        catch (Exception error) when (error is InvalidOperationException or DllNotFoundException or IOException)
        {
            log.Error(error, $"compositor stopped");
            renderedFrames = 0;
            return 1;
        }

        renderedFrames = Math.Max(0, totalRendered);

        if (totalRendered >= 0)
        {
            BasinReport.Line(Basin.Cli.CompositorLines.Frames(totalRendered));
            if (CompositorLines.Census() is { } census)
            {
                log.Error($"{census}");
            }
        }

        return status;
    }

    private static int RunCompositor(
        BasinLogger log,
        string rendererName,
        bool drm,
        bool nested,
        int outputCount,
        int width,
        int height,
        double[] scales,
        bool xwayland,
        int frames,
        string? screenshotPath,
        string? startupCommand,
        IpcChoice ipcChoice,
        HandoffMode handoff,
        bool inspectorOpen,
        out long totalRendered)
    {
        totalRendered = -1;

        if (!InitFile.TryResolve(startupCommand, log, out var init))
        {
            return 1;
        }

        InitProcess.RaiseFileLimit(log);

        var stack = CreateStack(rendererName, log, out var startedRenderer);
        using var renderStack = new RenderStackLifetime(stack);
        var renderer = stack.Renderer;
        var deviceAllocator = stack.DeviceAllocator;

        using var host = Basin.Host.BasinHost.Create(
            Basin.Host.HostOptions.ForBackend(drm ? "drm" : nested ? "nested" : "headless") with { Handoff = handoff });
        var display = host.Display;
        var socket = host.Socket;
        var loop = host.Loop;

        var drmBackend = host.Drm;
        var parent = host.Parent;
        Basin.Backend.Libinput.LibinputBackend? libinput = null;

        var layout = new OutputLayout();

        var scene = new Scene();

        var sceneLayers = new SceneLayers(scene.Root);

        using var driver = renderStack.Driver = new OutputDriver(host, scene, layout, renderer, deviceAllocator)
        {
            Background = Background,
            Requested = outputCount,
            Scales = scales,
            ContinuousRepaint = frames > 0,
            HeadlessMode = new OutputMode(width, height, 60_000),
            NestedName = index => outputCount == 1 ? "river" : $"river-{index + 1}",
        };
        driver.HandoffLayer = sceneLayers.Handoff;
        var views = driver.Views;
        var outputsCreated = false;

        double MaxScale()
        {
            var max = 1.0;
            foreach (var view in views)
            {
                max = Math.Max(max, view.Output.Scale);
            }

            return max;
        }

        driver.ModesetRefused += card => log.Error($"modeset refused by {card.Name}");
        driver.HandoffInherited += inheritance => BasinReport.Line(HandoffLines.Inherit(inheritance));
        host.HandoffRetained += retention => BasinReport.Line(HandoffLines.Retain(retention));
        driver.ScaleRefused += (view, scale) =>
            log.Warn($"scale {scale} refused by {view.Output.Name}");
        driver.ScanoutChanged += (view, choice) =>
            BasinReport.Line(CompositorLines.Scanout(view.Output.Name, choice, view.SwapModifiers.Length));

        if (drmBackend is not null)
        {
            libinput = new Basin.Backend.Libinput.LibinputBackend(loop, host.Session!);
        }

        var colorPack = new Basin.Desktop.ColorCapabilityPack(layout, renderer);
        var servicePack = new Basin.Desktop.DesktopServicePack(scene, layout, renderer, drmBackend);
        var capturePack = servicePack.Capture;
        driver.Capture = capturePack;
        var capture = capturePack.Capture;
        capture.Background = Background;
        var dmabufCapture = capturePack.DmabufCapture;
        var cursorTheme = servicePack.CursorTheme;

        var injectedInput = new Basin.Seat.Backends.HookInputSink();
        using var services = host.CreateServices()
            .Use(layout)
            .With(servicePack)
            .With(colorPack)
            .Use<IInputSink>(injectedInput);

        using var blur = Basin.Effects.RebindableBackdropBlur.For(renderer);
        if (blur is not null)
        {
            services.Use<IBackgroundEffects>(blur);
        }

        Basin.Backend.Libinput.LibinputTabletSource? tablets = null;
        if (libinput is not null)
        {
            tablets = new Basin.Backend.Libinput.LibinputTabletSource(libinput);
            services.Use<ITabletSource>(tablets);
        }

        services.Install(DesktopPack.For("inlet"));

        if (renderer.Device is { } renderDevice)
        {
            services.Install(new LinuxDmabufModule(renderer.DmabufTextureFormats, renderDevice.DevicePath, captureFormats: renderer.DmabufRenderFormats));
        }

        Basin.XWayland.XWaylandModule? xwaylandModule = null;
        if (xwayland)
        {
            xwaylandModule = new Basin.XWayland.XWaylandModule();
            services.Install(xwaylandModule);
        }

        using var inputConfigurator = new Basin.Seat.Backends.InputConfigurator();
        services.UseDefault<Basin.Capabilities.IInputDeviceConfiguration>(inputConfigurator);
        var lockDriver = new Basin.Desktop.SessionLockSceneDriver(services, sceneLayers.Lock, layout, sceneLayers.SetLocked);
        var layerDriver = new Basin.Desktop.LayerShellSceneDriver(services, layout, sceneLayers);
        services.Freeze();
        using var geometryReporter = new Basin.Host.ToplevelGeometryReporter(driver, scene, services.Require<Basin.Capabilities.IToplevelModel>());

        if (blur is not null)
        {
            scene.SurfaceBackdrop = new BackgroundBlurDriver(services.Require<BackgroundEffectManager>(), blur);
        }

        var compositor = services.Require<CompositorGlobal>();
        var seat = services.Require<Basin.Seat.Seat>();
        var shell = services.Require<XdgShell>();
        var fractionalScale = services.Require<FractionalScaleManager>();
        var cursorShapes = services.Require<CursorShapeManager>();
        var fifo = services.Require<FifoManager>();
        var frameClock = services.Require<Basin.Capabilities.IFrameClock>();

        services.Find<Basin.Desktop.LinuxDrmSyncobjManager>()?.DeclareRenderer(renderer);
        var sessionLock = services.Require<Basin.Desktop.SessionLockManager>();
        var textInput = services.Find<Basin.Desktop.TextInputManager>();
        var inputMethod = services.Find<Basin.Desktop.InputMethodRelay>();
        seat.Keyboard.FocusChanged += surface => textInput?.NotifyFocus(surface);
        var foreignToplevels = services.Require<ForeignToplevelListManager>();
        var toplevels = services.Require<IToplevelModel>();

        using var management = new RiverWindowManager(
            display, loop, scene, sceneLayers.Windows, shell, layout, [seat])
        {
            ForeignToplevels = foreignToplevels,
            Toplevels = toplevels,
            ToplevelSource = services.Require<XdgToplevelSource>(),
            Compositor = compositor,
            Decorations = services.Require<XdgDecorationManager>(),
            SessionLock = sessionLock,
        };
        capturePack.Attach(toplevels, surface =>
            management.TryCaptureTrees(surface, out var content, out var popups)
                ? new ToplevelCaptureTrees(content, popups)
                : null);
        management.Restacked += capturePack.Stack.RaiseChanged;

        if (services.Find<Basin.Desktop.ImageCopyCaptureManager>() is { } imageCopy)
        {
            imageCopy.SessionCountChanged += (source, count) =>
            {
                switch (source.Kind)
                {
                    case CaptureSourceKind.Output when source.OutputTarget is { } sessionOutput:
                        management.SetCaptureSessions(sessionOutput, count);
                        break;
                    case CaptureSourceKind.Toplevel
                        when toplevels.TryGet(source.ToplevelId, out var info) &&
                             info.Surface?.RoleObject is XdgToplevelWindow toplevel:
                        management.SetCaptureSessions(toplevel, count);
                        break;
                }
            };
        }

        if (!colorPack.Attach(services, scene))
        {
            throw new InvalidOperationException("No color manager is registered. Inlet needs one to manage color.");
        }

        var color = colorPack.Manager;
        colorPack.Surfaces.CountChanged += attached => BasinReport.Line(CompositorLines.Luts(attached));

        void RefreshSurfaceLuts() => colorPack.RefreshSurfaces();

        var pointer = new LayoutPointer(layout);

        var cursor = new Basin.Desktop.CursorController(layout) { Capture = capture };

        if (tablets is not null)
        {
            var tabletManager = services.Require<Basin.Desktop.TabletManager>();
            tabletManager.ToolProximityIn += (tool, _, axes) =>
            {
                if (views.Count > 0)
                {
                    tool.AimAt(scene, layout, cursor.CursorOutput ?? views[0].Output, axes);
                }
            };
            tabletManager.ToolMoved += (tool, axes) =>
            {
                if (views.Count > 0)
                {
                    tool.AimAt(scene, layout, cursor.CursorOutput ?? views[0].Output, axes);
                }
            };
        }

        var sessionKeys = new Dictionary<XdgToplevelWindow, (string Session, string Name)>();
        var sessionSaved = new Dictionary<XdgToplevelWindow, ToplevelSessionState>();
        var sessionStore = services.Require<ISessionStore>();
        var sessions = services.Require<Basin.Desktop.SessionManager>();
        sessions.ToplevelAdded += (session, name, toplevel) =>
        {
            sessionKeys[toplevel] = (session, name);
            toplevel.Xdg.Committed += () =>
            {
                if (!sessionKeys.TryGetValue(toplevel, out var key))
                {
                    return;
                }

                var geometry = toplevel.Xdg.EffectiveGeometry;
                var state = new ToplevelSessionState
                {
                    Geometry = new Box(0, 0, geometry.Width, geometry.Height),
                    States = toplevel.SessionStates,
                };

                if (sessionSaved.TryGetValue(toplevel, out var previous) && previous == state)
                {
                    return;
                }

                sessionSaved[toplevel] = state;
                sessionStore.Save(key.Session, key.Name, state);
            };
            toplevel.Destroyed += () =>
            {
                sessionKeys.Remove(toplevel);
                sessionSaved.Remove(toplevel);
            };
        };

        var presence = new List<SurfaceBox>();
        var presenceTracker = new SurfacePresenceTracker(layout, fractionalScale.AnnounceScale);

        void UpdateSurfacePresence()
        {
            scene.CollectSurfaces(presence);
            presenceTracker.Update(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(presence));
        }

        compositor.SurfaceCreated += surface => fractionalScale.AnnounceScale(surface, MaxScale());

        lockDriver.TextInput = textInput;
        lockDriver.Locked += () => BasinReport.Line($"LOCKED");
        lockDriver.Unlocked += () => BasinReport.Line($"UNLOCKED");
        lockDriver.Abandoned += () => BasinReport.Line($"LOCK ABANDONED (staying blanked)");
        lockDriver.LockSurfaceAdded += (lockSurface, _) =>
        {
            RefreshSurfaceLuts();
            fractionalScale.AnnounceScale(lockSurface.Surface, lockSurface.Output.Output.Scale);
        };

        layerDriver.Accept = _ => management.LayerShell.IsSupported && views.Count > 0;
        layerDriver.DefaultOutput = _ => ((management.LayerShell.DefaultOutput is { } defaultOutput
            ? driver.ViewOf(defaultOutput)
            : null) ?? views[0]).Global;
        layerDriver.UsableAreaChanged += (output, usable) =>
        {
            var box = layout.BoxOf(output);
            management.LayerShell.SetNonExclusiveArea(
                output, usable with { X = box.X + usable.X, Y = box.Y + usable.Y });
        };

        LayerSurface? onDemandFocus = null;

        void RefreshLayerFocus()
        {
            LayerSurface? exclusive = null;
            foreach (var (layer, scene) in layerDriver.Surfaces)
            {
                if (scene is null || !layer.IsMapped ||
                    layer.KeyboardInteractivity != Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.KeyboardInteractivity.Exclusive)
                {
                    continue;
                }

                if (exclusive is null || layer.Layer >= exclusive.Layer)
                {
                    exclusive = layer;
                }
            }

            if (exclusive is not null)
            {
                management.LayerShell.SetLayerFocus(seat, LayerFocus.Exclusive, exclusive.Surface);
                return;
            }

            if (onDemandFocus is { IsMapped: true } demand &&
                demand.KeyboardInteractivity == Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.KeyboardInteractivity.OnDemand)
            {
                management.LayerShell.SetLayerFocus(seat, LayerFocus.NonExclusive, demand.Surface);
                return;
            }

            onDemandFocus = null;
            management.LayerShell.SetLayerFocus(seat, LayerFocus.None, null);
        }

        layerDriver.Arranged += RefreshLayerFocus;
        layerDriver.SceneCreated += (_, _) => RefreshSurfaceLuts();
        layerDriver.Removed += layer =>
        {
            if (ReferenceEquals(onDemandFocus, layer))
            {
                onDemandFocus = null;
            }
        };
        layerDriver.TrackPopups(shell);
        layerDriver.PopupSceneCreated += (_, _, _) => RefreshSurfaceLuts();

        layerDriver.Rearrange();

        Basin.XWayland.XWaylandServer? xServer = null;
        if (xwaylandModule is not null)
        {
            xServer = xwaylandModule.Native!.Server;
            xwaylandModule.Native.WindowManagerReady += wm =>
            {
                management.Adopt(wm);
                BasinReport.Line($"XWAYLAND WM {xServer.DisplayName}");
            };
            BasinReport.Line($"XWAYLAND {xServer.DisplayName}");
        }

        var keymapNames = Basin.Seat.SystemKeymap.Read();
        seat.Keyboard.SetKeymap(keymapNames);
        BasinReport.Line($"KEYMAP {keymapNames.Layout ?? "xkb default"}{(keymapNames.Model is { } m ? $" {m}" : string.Empty)}");
        seat.Keyboard.ModifiersChanged += () => management.NotifyModifiers(seat);

        cursor.Shapes = cursorShapes;
        cursor.ColorProfiles = services.Find<IColorProfileService>();
        color.OutputDescriptionChanged += (global, description) => cursor.Describe(global.Output, description);

        if (drmBackend is not null)
        {
            var (cursorWidth, cursorHeight) = drmBackend.CursorSize;
            cursor.Load(new Basin.Backend.Drm.DumbAllocator(drmBackend), cursorWidth, cursorHeight);
        }
        else if (parent is not null)
        {
            cursor.UseParentCursor();
            cursor.Load(new ShmAllocator(), 128, 128);
        }

        cursorTheme.Images = cursor.Images;
        cursorShapes.CursorRequested += cursor.ShowImage;
        seat.Pointer.CursorRequested += cursor.HandleCursorRequest;

        void ReportCursor()
        {
            if (cursor.Images is { } images)
            {
                BasinReport.Line($"CURSOR left_ptr {images.Size}px {cursor.DrawnBy} on {cursor.CursorOutput?.Name ?? "nothing"} " + $"scale {cursor.CursorOutput?.Scale ?? 0}");
            }
        }

        void OnOutputsReconfigured()
        {
            driver.Relayout();
            pointer.Reposition();
            cursor.MoveTo(pointer.X, pointer.Y);
            ReportCursor();
        }

        if (services.Find<IOutputConfiguration>() is { } outputConfiguration)
        {
            outputConfiguration.Applied += _ => OnOutputsReconfigured();
        }

        ReportCursor();

        var lineReport = new IpcLineReport();
        var inspector = new InspectorSession(new InspectorSessionOptions
        {
            Driver = driver,
            Scene = scene,
            Layer = sceneLayers.Overlay,
            Loop = loop,
            Seat = seat,
            Services = services,
            Renderer = renderer,
            Allocator = deviceAllocator,
            RenderNode = Basin.Renderers.RendererCatalog.FindRenderNode(),
            Report = lineReport,
            Settings = new InspectorSettings { Open = inspectorOpen },
            PointerPosition = () => (pointer.X, pointer.Y),
        });

        Basin.Accessibility.DesktopAccessibility? access = null;
        var accessKeys = new HashSet<uint>();

        void DeliverKey(uint timeMs, uint key, bool pressed, bool fromInputMethod = false)
        {
            if (inspector.Input.Key(timeMs, key, pressed))
            {
                return;
            }

            if (!pressed && accessKeys.Remove(key))
            {
                return;
            }

            if (pressed && access is { } suite && seat.Keyboard.State is { } state &&
                state.IsModActive("Mod4") && state.IsModActive("Mod1") &&
                Basin.Accessibility.DesktopAccessibility.DefaultActionForKey(key) is { } action && suite.Run(action))
            {
                accessKeys.Add(key);
                return;
            }

            if (management.HandleKey(seat, key, pressed))
            {
                return;
            }

            if (!fromInputMethod && textInput is { HasKeyboardGrab: true })
            {
                textInput.ForwardKey(timeMs, key, pressed);
                return;
            }

            seat.Keyboard.NotifyKey(timeMs, key, pressed);
        }

        IOutput? reportedCursorOutput = null;

        void PointerMoved(uint timeMs)
        {
            cursor.MoveTo(pointer.X, pointer.Y);
            if (!ReferenceEquals(cursor.CursorOutput, reportedCursorOutput))
            {
                reportedCursorOutput = cursor.CursorOutput;
                ReportCursor();
            }

            management.NotifyPointerPosition(seat, pointer.X, pointer.Y);
            if (management.HasPointerOperation(seat))
            {
                return;
            }

            if (inspector.Input.Motion(timeMs, pointer.X, pointer.Y, out var inspectorCursor))
            {
                cursor.SetHover(null, overClient: false);
                cursor.ShowNamed(inspectorCursor ?? "default");
                return;
            }

            var hit = scene.SurfaceAt(pointer.X, pointer.Y);
            seat.Pointer.NotifyMotionAt(timeMs, hit?.Surface, hit?.X ?? 0, hit?.Y ?? 0, pointer.X, pointer.Y);
            cursor.SetHover(seat.Pointer.Focus, overClient: true);
        }

        void DeliverPointerButton(uint timeMs, uint button, bool pressed)
        {
            if (inspector.Input.Button(timeMs, button, pressed))
            {
                return;
            }

            if (management.HandlePointerButton(seat, timeMs, button, pressed))
            {
                return;
            }

            if (pressed && scene.SurfaceAt(pointer.X, pointer.Y) is { Surface: { } clicked })
            {
                LayerSurface? onDemand = null;
                foreach (var entry in layerDriver.Surfaces)
                {
                    if (entry.Scene is not null && ReferenceEquals(entry.Layer.Surface, clicked) &&
                        entry.Layer.KeyboardInteractivity ==
                            Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.KeyboardInteractivity.OnDemand)
                    {
                        onDemand = entry.Layer;
                        break;
                    }
                }

                if (onDemand is not null)
                {
                    onDemandFocus = onDemand;
                    RefreshLayerFocus();
                }
                else
                {
                    management.NotifyInteraction(seat, clicked);
                }
            }

            seat.Pointer.NotifyButton(timeMs, button, pressed);
            seat.Pointer.NotifyFrame();
        }

        using var pointerRefresh = new PointerRefresh(scene, loop, () => PointerMoved((uint)Environment.TickCount));

        var touchBinder = new Basin.Seat.Backends.SeatBinder(seat, layout, pointer, cursor)
        {
            RelativePointer = services.Find<Basin.Desktop.RelativePointerManager>(),
            Constraints = services.Find<Basin.Desktop.PointerConstraintDriver>(),
        };
        touchBinder.Key += (timeMs, key, pressed) => DeliverKey(timeMs, key, pressed);
        touchBinder.UseKeyboardAids(loop);
        touchBinder.Motion += (timeMs, _, _, _, _) => PointerMoved(timeMs);
        touchBinder.Button += DeliverPointerButton;
        touchBinder.Axis += (timeMs, axis) =>
        {
            if (inspector.Input.Axis(timeMs, axis))
            {
                return;
            }

            seat.Pointer.NotifyAxis(timeMs, axis);
            seat.Pointer.NotifyFrame();
        };
        touchBinder.PointerLeft += () => seat.Pointer.NotifyClearFocus();
        var touchDriver = new Basin.Seat.Backends.SeatTouchDriver(touchBinder, seat);
        touchDriver.Router.Chrome = inspector.Input;
        var touchPolicy = new TouchPolicy
        {
            Pointer = pointer,
            Moved = PointerMoved,
            Button = DeliverPointerButton,
        };
        touchDriver.Router.HitTester = new Basin.Seat.Backends.SceneTouchHitTester(scene)
        {
            Suppressed = () => management.HasPointerOperation(seat),
        };
        touchDriver.AttachPointer(touchPolicy).ClaimWithoutSurface = true;
        touchDriver.Router.Activity =
            services.Find<Basin.Capabilities.IIdleSource>() as Basin.Seat.SeatIdleSource;
        touchDriver.Routed += (slot, kind, surface) =>
        {
            if (kind == Basin.Seat.TouchTargetKind.Client && surface is not null)
            {
                management.NotifyInteraction(seat, surface);
            }
            else if (kind == Basin.Seat.TouchTargetKind.Pointer)
            {
                log.Debug($"touch {slot} drives the pointer");
            }
        };

        driver.Frames = frameClock;
        driver.ModeChanged += view =>
        {
            management.RequestManage();
            BasinReport.Line($"MODE {view.Output.Name} {view.Width}x{view.Height}");
        };
        driver.Painted += _ => UpdateSurfacePresence();
        driver.Added += view =>
        {
            cursor.AddOutput(view.Output, view.Scene!);
            presenceTracker.AddOutput(view.Output, view.Global);
            colorPack.AddOutput(view.Global, view.Output, view.Scene);
            management.AddOutput(view.Global);
            if (view.Output is Basin.Backend.Drm.DrmOutput added)
            {
                BasinReport.Line($"OUTPUT{(outputsCreated ? " + $" : string.Empty)} {added.Name} " + $"{added.PreferredMode.Width}x{added.PreferredMode.Height}");
            }

            if (view.Output.Scale != 1)
            {
                BasinReport.Line($"SCALE {view.Output.Name} {view.Output.Scale}");
            }

        };
        driver.HostScaleFollowed += view =>
        {
            BasinReport.Line($"SCALE {view.Output.Name} {view.Output.Scale}");
            OnOutputsReconfigured();
        };
        driver.Removed += view =>
        {
            if (view.Output is Basin.Backend.Drm.DrmOutput card)
            {
                BasinReport.Line($"OUTPUT - {card.Name}");
                management.RemoveOutput(card);
            }

            cursor.RemoveOutput(view.Output);
            presenceTracker.RemoveOutput(view.Output);
            colorPack.RemoveOutput(view.Global);
        };
        driver.LayoutChanged += () =>
        {
            layerDriver.Rearrange();
            lockDriver.Reconfigure();
            UpdateSurfacePresence();
        };

        driver.CreateInitialOutputs();
        driver.AdoptSecondaryCards();
        outputsCreated = true;
        if (drmBackend is not null && views.Count == 0)
        {
            log.Error($"no connected output");
            inspector.Dispose();
            return 1;
        }

        inspector.Start();

        if (libinput is not null)
        {
            var start = layout.BoxOf(views[0].Output);
            pointer.Warp(start.X + (start.Width / 2.0), start.Y + (start.Height / 2.0));
            cursor.MoveTo(pointer.X, pointer.Y);

            management.LibinputConfig.InputDeviceFor = id => libinput.Configuration.DeviceFor(id);
            management.LibinputConfig.Configuration = inputConfigurator;
            var keyboardDevices = new Dictionary<Basin.Backend.Libinput.InputDevice, Basin.Seat.KeyboardDevice>();

            void Register(Basin.Backend.Libinput.InputDevice device)
            {
                var type = device.HasKeyboard ? Basin.Shell.River.InputDeviceType.Keyboard
                    : device.HasTouch ? Basin.Shell.River.InputDeviceType.Touch
                    : Basin.Shell.River.InputDeviceType.Pointer;
                management.InputManager.AddDevice(device, device.Name, type);
                if (device.HasKeyboard)
                {
                    var keyboard = seat.Keyboard.CreateDevice();
                    keyboardDevices[device] = keyboard;
                    management.XkbConfig.AddKeyboard(device, seat, keyboard);
                }

                BasinReport.Line($"INPUT {type} {device.Name}");
            }

            libinput.DeviceAdded += Register;
            libinput.DeviceRemoved += device =>
            {
                management.InputManager.RemoveDevice(device);
                management.XkbConfig.RemoveKeyboard(device);
                if (keyboardDevices.Remove(device, out var keyboard))
                {
                    keyboard.Dispose();
                }
            };

            touchBinder.KeyboardFor = device => keyboardDevices.GetValueOrDefault(device);

            management.InputManager.SeatCreated += name => BasinReport.Line($"SEAT + {name}");
            management.InputManager.SeatDestroyed += name => BasinReport.Line($"SEAT - {name}");
            management.InputManager.DeviceAssigned += (device, name) =>
                BasinReport.Line($"ASSIGN {(device as Basin.Backend.Libinput.InputDevice)?.Name} -> {name}");

            ulong ConfiguredId(object handle) =>
                handle is Basin.Backend.Libinput.InputDevice device ? libinput.Configuration.IdFor(device) : 0;

            management.InputManager.RepeatInfoChanged += (handle, rate, delay) =>
            {
                var id = ConfiguredId(handle);
                inputConfigurator.Set(id, Basin.Capabilities.InputSetting.RepeatRate, Basin.Capabilities.InputSettingValue.Of((uint)Math.Max(0, rate)));
                inputConfigurator.Set(id, Basin.Capabilities.InputSetting.RepeatDelay, Basin.Capabilities.InputSettingValue.Of((uint)Math.Max(0, delay)));
            };
            management.InputManager.ScrollFactorChanged += (handle, factor) =>
                inputConfigurator.Set(ConfiguredId(handle), Basin.Capabilities.InputSetting.ScrollFactor, Basin.Capabilities.InputSettingValue.Number(factor));
            management.InputManager.MappedToOutput += (handle, output) =>
                inputConfigurator.Set(ConfiguredId(handle), Basin.Capabilities.InputSetting.MapToOutput, Basin.Capabilities.InputSettingValue.Text(output?.Name ?? string.Empty));

            touchBinder.BindLibinput(libinput);
        }

        NestedSeam? seam = null;
        if (parent is not null)
        {
            parent.KeyboardAdded += _ => BasinReport.Line($"INPUT Keyboard host");
            parent.PointerAdded += _ => BasinReport.Line($"INPUT Pointer host");
            touchBinder.BindParent(parent);
            seam = new NestedSeam(
                parent,
                services.Find<Basin.Capabilities.ISelectionStore>(),
                services.Find<Basin.Capabilities.IDragTracker>(),
                services.Find<Basin.Capabilities.IIdleSource>());
        }

        inputConfigurator.Rejected += outcome => BasinReport.Line(
            $"INPUT-RULE {outcome.Result.ToString().ToLowerInvariant()} {Basin.Capabilities.InputSettingNames.Key(outcome.Setting)} device={outcome.DeviceName ?? "none"}");
        inputConfigurator.Attach(seat, touchBinder, libinput?.Configuration);

        injectedInput.OnModifiers = (keyboard, depressed, latched, locked, group) =>
        {
            seat.Keyboard.Activate(keyboard);
            seat.Keyboard.NotifyModifiers(depressed, latched, locked, group);
            management.NotifyModifiers(seat);
            return true;
        };
        var injector = new Basin.Seat.Backends.SeatInjector(touchBinder, seat, layout, pointer)
        {
            Moved = PointerMoved,
            DeliverButton = DeliverPointerButton,
            DeliverKey = (timeMs, key, pressed) => DeliverKey(timeMs, key, pressed),
            DeliverAxis = (timeMs, axis, value, source) =>
            {
                var routed = new PointerAxis((Wayland.WlPointer.Axis)axis, value, Source: (Wayland.WlPointer.AxisSource)source);
                if (inspector.Input.Axis(timeMs, routed))
                {
                    return;
                }

                seat.Pointer.NotifyAxis(timeMs, routed);
                seat.Pointer.NotifyFrame();
            },
        };
        injectedInput.Route(injector, seat, (keyboard, timeMs, key, pressed) =>
            DeliverKey(
                timeMs, key, pressed,
                fromInputMethod: inputMethod is not null && inputMethod.IsInputMethodClient(keyboard?.Tag)));

        var runLoop = new Basin.Host.CompositorRunLoop(host, driver)
        {
            Outro = new Basin.Host.WindowsFadeOutro(sceneLayers, TimeSpan.FromMilliseconds(300)),
        };
        driver.Emptied += runLoop.Stop;
        if (parent is not null)
        {
            parent.ParentGone += runLoop.Stop;
        }

        management.ExitSessionRequested += () =>
        {
            BasinReport.Line($"EXIT requested by the window manager");
            runLoop.Stop();
        };
        management.WindowManagerLost += () => BasinReport.Line($"WM LOST");
        management.WindowManagerUnresponsive += () => BasinReport.Line($"WM UNRESPONSIVE");

        BasinReport.Line(Basin.Cli.CompositorLines.Socket(socket));
        using var settle = new Basin.Host.FrameSettle(driver);
        using var ipc = ipcChoice.Attach(loop, services, socket, new IpcSessionInfo
        {
            Compositor = "inlet",
            Backend = drm ? "drm" : nested ? "nested" : "headless",
            Renderer = rendererName,
            XwaylandDisplay = () => xServer?.DisplayName,
            Quit = runLoop.Stop,
            Settle = settle,
        });
        ipc.SyntheticInput = injector;
        using var processes = new ProcessTracker(loop, () => ProcessTracker.SessionEnvironment(socket, xServer?.DisplayName, ipc.Path));
        access = new Basin.Accessibility.DesktopAccessibility(new Basin.Accessibility.DesktopAccessibilityOptions
        {
            Name = "inlet",
            Host = host,
            Scene = scene,
            Seat = seat,
            Pointer = pointer,
            Processes = processes,
            TextInput = services.Find<Basin.Desktop.TextInputManager>(),
            Toplevels = toplevels,
            Aids = touchBinder.Aids,
            Log = log,
        });
        access.Apply(new Basin.Config.AccessibilitySettings());
        cursor.Curtain = access.Curtain;
        ipc.GuardWithCurtain(access.Curtain);
        _ = access.StartAsync(Basin.AccessibilityMode.Auto);
        lineReport.AddInputLines(ipc.Methods);
        inspector.Register(ipc.Methods);
        host.RendererReplaced += inspector.OnRendererReplaced;
        host.RendererRebuilt += inspector.OnRendererRebuilt;
        driver.RendererFactory = () => Basin.Renderers.RendererCatalog.Create(
            startedRenderer, Basin.Renderers.RendererCatalog.FindRenderNode() ?? string.Empty);
        lineReport.AddSessionLines(ipc.Methods);
        ipc.Start();
        ipc.StartLineFront();

        var startup = init is null ? null : InitProcess.Start(init, socket, xServer?.DisplayName, log);

        var started = Environment.TickCount64;
        var reported = 0L;
        runLoop.Frames = frames;
        runLoop.Iterating += () =>
        {
            if (driver.PrimaryRendered - reported < 300)
            {
                return;
            }

            reported = driver.PrimaryRendered;
            var seconds = (Environment.TickCount64 - started) / 1000.0;
            BasinReport.Line($"STATS {reported} frames in {seconds:F1}s ({reported / seconds:F1}/s) " + $"manage={management.ManageSequences} render={management.RenderSequences} " + $"timedout={Transaction.TimedOutCount}");
        };
        runLoop.Run();

        if (screenshotPath is not null)
        {
            for (var i = 0; i < views.Count; i++)
            {
                var path = ScreenshotPath(screenshotPath, i);
                var shot = SceneScreenshot.WritePresented(views[i].Scene?.LastTarget, driver.Renderer, path)
                    == ScreenshotOutcome.Written;
                BasinReport.Line(shot
                    ? $"SHOT {path} after {views[i].Rendered} frames"
                    : $"SHOT failed: no readable presented frame on {views[i].Output.Name} after {views[i].Rendered} frames");
            }
        }

        access.Dispose();
        host.DisconnectClients();
        totalRendered = driver.PrimaryRendered;

        startup?.Stop(log);
        colorPack.Luts.Dispose();
        inspector.Dispose();
        driver.Dispose();
        scene.Root.Destroy();

        seam?.Dispose();
        cursor.Dispose();
        tablets?.Dispose();
        libinput?.Dispose();
        return runLoop.ExitCode;
    }

    private static string ScreenshotPath(string path, int index)
    {
        if (index == 0)
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path);
        var file = $"{Path.GetFileNameWithoutExtension(path)}.{index + 1}{Path.GetExtension(path)}";
        return string.IsNullOrEmpty(directory) ? file : Path.Combine(directory, file);
    }
}
