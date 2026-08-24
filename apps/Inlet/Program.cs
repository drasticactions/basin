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
using Basin.Scene;
using Basin.Shell.River;
using Basin.Shell.Xdg;
using Microsoft.Extensions.Logging;
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
        var commandOption = cli.Add(new Option<string?>("--command", "-c")
        {
            Description = "run `sh -c <command>` on startup instead of the init executable.",
            HelpName = "CMD",
        });

        return cli.Run(args, result =>
        {
            var backend = result.GetValue(backendOption);
            using var loggers = cli.CreateLoggerFactory(result);
            var status = Run(
                loggers.CreateLogger("Inlet"),
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
                out var rendered);
            cli.ReportFrames(rendered);
            return status;
        });
    }

    private static RenderStack CreateStack(string rendererName, ILogger log)
    {
        const string renderNode = "/dev/dri/renderD128";
        var name = rendererName;
        return Basin.Renderers.RendererCatalog.CreateWithFallback(
            ref name,
            File.Exists(renderNode) ? renderNode : null,
            fallback => Report(log, fallback));
    }

    private static void Report(ILogger log, Basin.Renderers.RendererFallback fallback)
    {
        if (fallback.Reason is null)
        {
            log.LogWarning(
                "{Renderer} requested but no render node was found; using software rendering", fallback.From);
            return;
        }

        log.LogWarning(
            "{Renderer} renderer unavailable ({Reason}); falling back to {Fallback}",
            fallback.From, fallback.Reason, fallback.To);
    }

    private static int Run(
        ILogger log,
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
        out long renderedFrames)
    {
        BasinCounters.Reset();

        var status = RunCompositor(
            log, rendererName, drm, nested, outputCount, width, height, scales, xwayland, frames, screenshotPath,
            startupCommand, out var totalRendered);

        renderedFrames = Math.Max(0, totalRendered);

        if (totalRendered >= 0)
        {
            Console.WriteLine($"FRAMES {totalRendered} LIVE {(BasinCounters.Enabled ? BasinCounters.LiveObjects.ToString() : "untracked")}");
            if (BasinCounters.Enabled && (BasinCounters.LiveObjects != 0 || BasinCounters.PendingFrees != 0))
            {
                BasinCounters.WriteCensus(Console.Error);
            }
        }

        return status;
    }

    private static int RunCompositor(
        ILogger log,
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
        out long totalRendered)
    {
        totalRendered = -1;

        if (!InitFile.TryResolve(startupCommand, log, out var init))
        {
            return 1;
        }

        InitProcess.RaiseFileLimit(log);

        var stack = CreateStack(rendererName, log);
        using var renderer = stack.Renderer;
        var deviceAllocator = stack.DeviceAllocator;

        using var host = Basin.Host.BasinHost.Create(
            Basin.Host.HostOptions.ForBackend(drm ? "drm" : nested ? "nested" : "headless"));
        var display = host.Display;
        var socket = host.Socket;
        var loop = host.Loop;

        var drmBackend = host.Drm;
        var parent = host.Parent;
        Basin.Backend.Libinput.LibinputBackend? libinput = null;

        var layout = new OutputLayout();

        var scene = new Scene();

        var sceneLayers = new SceneLayers(scene.Root);

        using var driver = new OutputDriver(host, scene, layout, renderer, deviceAllocator)
        {
            Background = Background,
            Requested = outputCount,
            Scales = scales,
            ContinuousRepaint = frames > 0,
            HeadlessMode = new OutputMode(width, height, 60_000),
            NestedName = index => outputCount == 1 ? "river" : $"river-{index + 1}",
        };
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

        driver.ModesetRefused += card => log.LogError("modeset refused by {Output}", card.Name);
        driver.ScaleRefused += (view, scale) =>
            log.LogError("scale {Scale} refused by {Output}", scale, view.Output.Name);
        driver.ScanoutChanged += (view, choice) => Console.WriteLine(choice switch
        {
            ScanoutChoice.DeviceBuffers =>
                $"SCANOUT {view.Output.Name} device modifiers={view.SwapModifiers.Length}",
            ScanoutChoice.DumbLinear =>
                $"SCANOUT {view.Output.Name} dumb linear; every frame reads the framebuffer back",
            _ =>
                $"SCANOUT {view.Output.Name} device buffers refused by the plane; falling back to dumb linear",
        });

        if (drmBackend is not null)
        {
            libinput = new Basin.Backend.Libinput.LibinputBackend(loop, host.Session!);
        }

        var capturePack = new SceneCapturePack(scene, layout);
        driver.Capture = capturePack;
        var capture = capturePack.Capture;
        capture.Renderer = renderer;
        capture.Background = Background;
        var dmabufCapture = capturePack.DmabufCapture;
        var cursorTheme = new Basin.Capabilities.Defaults.CursorImageTheme();

        var injectedInput = new Basin.Seat.Backends.HookInputSink();
        using var services = host.CreateServices()
            .Use(layout)
            .With(capturePack)
            .With(new Basin.Desktop.DrmCapabilityPack(renderer, drmBackend))
            .Use<ICursorTheme>(cursorTheme)
            .Use<IColorProfileService>(new Basin.Color.Lcms2ColorProfileService())
            .Use<IInputSink>(injectedInput)
            .Use<IActivationTokens>(new Basin.Capabilities.Defaults.DefaultActivationTokens())
            .Use<IBell>(Basin.Capabilities.Defaults.SilentBell.Instance);

        Basin.Backend.Libinput.LibinputTabletSource? tablets = null;
        if (libinput is not null)
        {
            tablets = new Basin.Backend.Libinput.LibinputTabletSource(libinput);
            services.Use<ITabletSource>(tablets);
        }

        var pack = DesktopPack.For("inlet");
        if (drmBackend is null)
        {
            pack = pack.Without("wp_drm_lease_device_v1");
        }

        services.Install(pack);

        if (renderer.Device is { } renderDevice)
        {
            services.Install(new LinuxDmabufModule(renderer.DmabufTextureFormats, renderDevice.DevicePath));
        }

        Basin.XWayland.XWaylandModule? xwaylandModule = null;
        if (xwayland)
        {
            xwaylandModule = new Basin.XWayland.XWaylandModule();
            services.Install(xwaylandModule);
        }

        services.Freeze();

        var compositor = services.Require<CompositorGlobal>();
        var seat = services.Require<Basin.Seat.Seat>();
        var shell = services.Require<XdgShell>();
        var fractionalScale = services.Require<FractionalScaleManager>();
        var cursorShapes = services.Require<CursorShapeManager>();
        var fifo = services.Require<FifoManager>();
        var frameClock = services.Require<Basin.Capabilities.IFrameClock>();

        services.Find<Basin.Desktop.LinuxDrmSyncobjManager>()?.DeclareRenderer(renderer);
        var layerShell = services.Require<LayerShell>();
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

        var color = services.Require<Basin.Desktop.ColorManager>();
        var outputDescription = ImageDescription.Srgb;
        Basin.Desktop.SurfaceLutDriver.DeclareSrgb(color);

        var luts = new Basin.Color.ColorLutCache(renderer);
        var lutDriver = new Basin.Desktop.SurfaceLutDriver(
            scene, color, surface => luts.LutFor(color.DescriptionOf(surface), outputDescription));
        lutDriver.CountChanged += attached => Console.WriteLine($"COLOR luts={attached}");
        lutDriver.WatchToplevels(shell);

        void RefreshSurfaceLuts() => lutDriver.Refresh();

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

        var lockDriver = new Basin.Desktop.SessionLockSceneDriver(
            sessionLock, seat, sceneLayers.Lock, layout, sceneLayers.SetLocked)
        {
            TextInput = textInput,
        };
        lockDriver.Locked += () => Console.WriteLine("LOCKED");
        lockDriver.Unlocked += () => Console.WriteLine("UNLOCKED");
        lockDriver.Abandoned += () => Console.WriteLine("LOCK ABANDONED (staying blanked)");
        lockDriver.LockSurfaceAdded += (lockSurface, _) =>
        {
            RefreshSurfaceLuts();
            fractionalScale.AnnounceScale(lockSurface.Surface, lockSurface.Output.Output.Scale);
        };

        var layerDriver = new Basin.Desktop.LayerShellSceneDriver(layerShell, layout, sceneLayers)
        {
            Accept = _ => management.LayerShell.IsSupported && views.Count > 0,
            DefaultOutput = _ => ((management.LayerShell.DefaultOutput is { } defaultOutput
                ? driver.ViewOf(defaultOutput)
                : null) ?? views[0]).Global,
        };
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
            xServer = services.Require<Basin.XWayland.XWaylandServer>();
            xwaylandModule.WindowManagerReady += wm =>
            {
                management.Adopt(wm);
                Console.WriteLine($"XWAYLAND WM {xServer.DisplayName}");
            };
            Console.WriteLine($"XWAYLAND {xServer.DisplayName}");
        }

        var keymapNames = Basin.Seat.SystemKeymap.Read();
        seat.Keyboard.SetKeymap(keymapNames);
        Console.WriteLine($"KEYMAP {keymapNames.Layout ?? "xkb default"}{(keymapNames.Model is { } m ? $" {m}" : string.Empty)}");
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
                Console.WriteLine(
                    $"CURSOR left_ptr {images.Size}px {cursor.DrawnBy} on {cursor.CursorOutput?.Name ?? "nothing"} " +
                    $"scale {cursor.CursorOutput?.Scale ?? 0}");
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

        void DeliverKey(uint timeMs, uint key, bool pressed, bool fromInputMethod = false)
        {
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

            var hit = scene.SurfaceAt(pointer.X, pointer.Y);
            seat.Pointer.NotifyMotionAt(timeMs, hit?.Surface, hit?.X ?? 0, hit?.Y ?? 0, pointer.X, pointer.Y);
            cursor.SetHover(seat.Pointer.Focus, overClient: true);
        }

        void DeliverPointerButton(uint timeMs, uint button, bool pressed)
        {
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

        var touchBinder = new Basin.Seat.Backends.SeatBinder(seat, layout, pointer, cursor);
        touchBinder.Key += (timeMs, key, pressed) => DeliverKey(timeMs, key, pressed);
        touchBinder.Motion += (timeMs, _, _, _, _) => PointerMoved(timeMs);
        touchBinder.Button += DeliverPointerButton;
        touchBinder.Axis += (timeMs, axis) =>
        {
            seat.Pointer.NotifyAxis(timeMs, axis);
            seat.Pointer.NotifyFrame();
        };
        touchBinder.PointerLeft += () => seat.Pointer.NotifyClearFocus();
        var touchDriver = new Basin.Seat.Backends.SeatTouchDriver(touchBinder, seat);
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
                log.LogDebug("touch {Slot} drives the pointer", slot);
            }
        };

        driver.Frames = frameClock;
        driver.ModeChanged += view =>
        {
            management.RequestManage();
            Console.WriteLine($"MODE {view.Output.Name} {view.Width}x{view.Height}");
        };
        driver.Painted += _ => UpdateSurfacePresence();
        driver.Added += view =>
        {
            cursor.AddOutput(view.Output, view.Scene!);
            presenceTracker.AddOutput(view.Output, view.Global);
            color.SetOutputDescription(view.Global, outputDescription);
            management.AddOutput(view.Global);
            if (view.Output is Basin.Backend.Drm.DrmOutput added)
            {
                Console.WriteLine(
                    $"OUTPUT{(outputsCreated ? " +" : string.Empty)} {added.Name} " +
                    $"{added.PreferredMode.Width}x{added.PreferredMode.Height}");
            }

            if (scales.Length > 0 && view.Output.Scale != 1)
            {
                Console.WriteLine($"SCALE {view.Output.Name} {view.Output.Scale}");
            }

        };
        driver.HostScaleFollowed += view =>
        {
            Console.WriteLine($"SCALE {view.Output.Name} {view.Output.Scale}");
            OnOutputsReconfigured();
        };
        driver.Removed += view =>
        {
            if (view.Output is Basin.Backend.Drm.DrmOutput card)
            {
                Console.WriteLine($"OUTPUT - {card.Name}");
                management.RemoveOutput(card);
            }

            cursor.RemoveOutput(view.Output);
            presenceTracker.RemoveOutput(view.Output);
        };
        driver.LayoutChanged += () =>
        {
            layerDriver.Rearrange();
            lockDriver.Reconfigure();
            UpdateSurfacePresence();
        };

        driver.CreateInitialOutputs();
        outputsCreated = true;
        if (drmBackend is not null && views.Count == 0)
        {
            log.LogError("no connected output");
            return 1;
        }

        if (libinput is not null)
        {
            var start = layout.BoxOf(views[0].Output);
            pointer.Warp(start.X + (start.Width / 2.0), start.Y + (start.Height / 2.0));
            cursor.MoveTo(pointer.X, pointer.Y);

            var inputConfiguration = new Basin.Backend.Libinput.LibinputDeviceConfiguration(libinput);
            management.LibinputConfig.Configuration = inputConfiguration;
            var keyboardDevices = new Dictionary<Basin.Backend.Libinput.InputDevice, Basin.Seat.KeyboardDevice>();

            void Register(Basin.Backend.Libinput.InputDevice device)
            {
                var type = device.HasKeyboard ? InputDeviceType.Keyboard
                    : device.HasTouch ? InputDeviceType.Touch
                    : InputDeviceType.Pointer;
                management.InputManager.AddDevice(device, device.Name, type);
                if (device.HasKeyboard)
                {
                    var keyboard = seat.Keyboard.CreateDevice();
                    keyboardDevices[device] = keyboard;
                    management.XkbConfig.AddKeyboard(device, seat, keyboard);
                }

                Console.WriteLine($"INPUT {type} {device.Name}");
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

            management.InputManager.SeatCreated += name => Console.WriteLine($"SEAT + {name}");
            management.InputManager.SeatDestroyed += name => Console.WriteLine($"SEAT - {name}");
            management.InputManager.DeviceAssigned += (device, name) =>
                Console.WriteLine($"ASSIGN {(device as Basin.Backend.Libinput.InputDevice)?.Name} -> {name}");

            touchBinder.BindLibinput(libinput);
        }

        NestedSeam? seam = null;
        if (parent is not null)
        {
            parent.KeyboardAdded += parentKeyboard =>
            {
                Console.WriteLine("INPUT Keyboard host");
                parentKeyboard.RepeatInfo += (rate, delay) => seat.Keyboard.SetRepeatInfo(rate, delay);
            };
            parent.PointerAdded += _ => Console.WriteLine("INPUT Pointer host");
            touchBinder.BindParent(parent);
            seam = new NestedSeam(
                parent,
                services.Find<Basin.Capabilities.ISelectionStore>(),
                services.Find<Basin.Capabilities.IDragTracker>(),
                services.Find<Basin.Capabilities.IIdleSource>());
        }

        injectedInput.OnKey = (keyboard, timeMs, key, pressed) =>
        {
            seat.Keyboard.Activate(keyboard);
            DeliverKey(
                timeMs, key, pressed,
                fromInputMethod: inputMethod is not null && inputMethod.IsInputMethodClient(keyboard?.Tag));
            return true;
        };
        injectedInput.OnModifiers = (keyboard, depressed, latched, locked, group) =>
        {
            seat.Keyboard.Activate(keyboard);
            seat.Keyboard.NotifyModifiers(depressed, latched, locked, group);
            management.NotifyModifiers(seat);
            return true;
        };
        injectedInput.OnPointerMotion = (timeMs, dx, dy) =>
        {
            pointer.Motion(dx, dy);
            PointerMoved(timeMs);
            return true;
        };
        var injector = new Basin.Seat.Backends.SeatInjector(touchBinder, seat, layout, pointer)
        {
            Moved = PointerMoved,
        };
        injectedInput.OnPointerMotionAbsolute = injector.MotionAbsolute;
        injectedInput.OnPointerButton = (timeMs, button, pressed) =>
        {
            DeliverPointerButton(timeMs, button, pressed);
            return true;
        };

        var running = true;
        driver.Emptied += () => running = false;
        if (parent is not null)
        {
            parent.ParentGone += () => running = false;
        }

        management.ExitSessionRequested += () =>
        {
            Console.WriteLine("EXIT requested by the window manager");
            running = false;
        };
        management.WindowManagerLost += () => Console.WriteLine("WM LOST");
        management.WindowManagerUnresponsive += () => Console.WriteLine("WM UNRESPONSIVE");

        Console.WriteLine($"SOCKET {socket}");
        Console.Out.Flush();

        var startup = init is null ? null : InitProcess.Start(init, socket, xServer?.DisplayName, log);

        var interrupt = loop.AddSignal(Signal.Interrupt, _ => running = false);
        var terminate = loop.AddSignal(Signal.Terminate, _ => running = false);

        var started = Environment.TickCount64;
        var reported = 0L;
        while (running && (frames == 0 || driver.PrimaryRendered < frames))
        {
            if (driver.PrimaryRendered - reported >= 300)
            {
                reported = driver.PrimaryRendered;
                var seconds = (Environment.TickCount64 - started) / 1000.0;
                Console.WriteLine(
                    $"STATS {reported} frames in {seconds:F1}s ({reported / seconds:F1}/s) " +
                    $"manage={management.ManageSequences} render={management.RenderSequences} " +
                    $"timedout={Transaction.TimedOutCount}");
            }

            loop.Dispatch(16);
            if (fifo.HasPendingBarriers)
            {
                driver.ScheduleAll();
            }

            parent?.Flush();
        }

        if (screenshotPath is not null)
        {
            for (var i = 0; i < views.Count; i++)
            {
                var path = ScreenshotPath(screenshotPath, i);
                var shot = SceneScreenshot.WritePresented(views[i].Scene?.LastTarget, renderer, path)
                    == ScreenshotOutcome.Written;
                Console.WriteLine(shot
                    ? $"SHOT {path} after {views[i].Rendered} frames"
                    : $"SHOT failed: no readable presented frame on {views[i].Output.Name} after {views[i].Rendered} frames");
            }
        }

        totalRendered = driver.PrimaryRendered;

        startup?.Stop(log);
        interrupt.Remove();
        terminate.Remove();
        driver.Dispose();
        scene.Root.Destroy();

        seam?.Dispose();
        cursor.Dispose();
        deviceAllocator?.Dispose();
        tablets?.Dispose();
        libinput?.Dispose();
        return 0;
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
