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

    private const uint BtnLeft = 0x110;

    private static int Main(string[] args)
    {
        var cli = new BasinCommand(
            "A compositor that owns no window-management policy and hands it to a client over the river protocols.");
        var rendererOption = cli.Add(CommonOptions.Renderer(
            Basin.Renderers.RendererCatalog.Names, "vulkan"));
        var backendOption = cli.Add(CommonOptions.Backend(
            [BackendKind.Nested, BackendKind.Drm, BackendKind.Headless]));
        var outputsOption = cli.Add(new Option<int>("--outputs")
        {
            Description = "how many outputs to create",
            HelpName = "N",
            DefaultValueFactory = _ => 1,
        });
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

        using var host = Basin.Host.BasinHost.Create(new Basin.Host.HostOptions
        {
            Backend = drm ? Basin.Host.HostBackend.Drm
                : nested ? Basin.Host.HostBackend.Nested
                : Basin.Host.HostBackend.Headless,
        });
        var display = host.Display;
        var socket = host.Socket;
        var loop = host.Loop;

        var drmBackend = host.Drm;
        var parent = host.Parent;
        Basin.Backend.Libinput.LibinputBackend? libinput = null;

        var layout = new OutputLayout();

        var scene = new Scene();

        var backgroundLayer = new SceneTree(scene.Root);
        var bottomLayer = new SceneTree(scene.Root);
        var windowTree = new SceneTree(scene.Root);
        var topLayer = new SceneTree(scene.Root);
        var overlayLayer = new SceneTree(scene.Root);
        var lockTree = new SceneTree(scene.Root);

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

        var injectedInput = new InletInputSink();
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

        services.Install(DesktopPack.For("inlet"));

        if (renderer.Device is { } renderDevice)
        {
            services.Install(new LinuxDmabufModule(renderer.DmabufTextureFormats, renderDevice.DevicePath));
        }

        if (xwayland)
        {
            services.Install(new Basin.XWayland.XWaylandModule());
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
        capture.Toplevels = toplevels;

        using var management = new RiverWindowManager(
            display, loop, scene, windowTree, shell, layout, [seat])
        {
            ForeignToplevels = foreignToplevels,
            Toplevels = toplevels,
            ToplevelSource = services.Require<XdgToplevelSource>(),
            Compositor = compositor,
            Decorations = services.Require<XdgDecorationManager>(),
            SessionLock = sessionLock,
        };
        capture.ToplevelContent = id =>
            toplevels.TryGet(id, out var info) && info.Surface is { } captured &&
            management.TryCaptureTrees(captured, out var content, out var popups)
                ? new Basin.Scene.ToplevelCaptureTrees(content, popups)
                : default;

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
        color.SupportedTransferFunctions =
            [ColorTransferFunction.Srgb, ColorTransferFunction.Gamma22, ColorTransferFunction.ExtLinear];
        color.SupportedPrimaries = [ColorPrimaries.Srgb];

        var luts = new Basin.Color.ColorLutCache(renderer);
        var lastLutCount = -1;

        void RefreshSurfaceLuts()
        {
            var attached = scene.AttachLuts(
                surface => luts.LutFor(color.DescriptionOf(surface), outputDescription));
            if (attached != lastLutCount)
            {
                lastLutCount = attached;
                Console.WriteLine($"COLOR luts={attached}");
            }
        }

        color.SurfaceDescriptionChanged += (_, _) => RefreshSurfaceLuts();
        shell.NewToplevel += toplevel => toplevel.Xdg.Mapped += RefreshSurfaceLuts;

        var pointer = new LayoutPointer(layout);

        var cursor = new Basin.Desktop.CursorController(layout) { Capture = capture };

        OutputView? ViewOf(IOutput? output)
        {
            if (output is null)
            {
                return null;
            }

            foreach (var view in views)
            {
                if (ReferenceEquals(view.Output, output))
                {
                    return view;
                }
            }

            return null;
        }

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

        var presence = new List<SceneSurfaceBox>();

        void UpdateSurfacePresence()
        {
            scene.CollectSurfaces(presence);
            foreach (var (surface, box) in presence)
            {
                var preferred = 1.0;
                foreach (var view in views)
                {
                    var outputBox = layout.BoxOf(view.Output);
                    var overlaps = box.X < outputBox.Right && box.Right > outputBox.X &&
                                   box.Y < outputBox.Bottom && box.Bottom > outputBox.Y;
                    surface.SetOutputPresence(view.Global, overlaps);
                    if (overlaps)
                    {
                        preferred = Math.Max(preferred, view.Output.Scale);
                    }
                }

                fractionalScale.AnnounceScale(surface, preferred);
            }
        }

        compositor.SurfaceCreated += surface => fractionalScale.AnnounceScale(surface, MaxScale());

        var layers = new List<(LayerSurface Layer, SceneSurface? Scene)>();
        var layerPopups = new Dictionary<XdgPopupWindow, SceneSurface>();
        var lockSurfaces = new List<(Basin.Desktop.LockSurface Lock, SceneSurface Scene)>();

        void SetSceneLocked(bool locked)
        {
            backgroundLayer.Enabled = !locked;
            bottomLayer.Enabled = !locked;
            windowTree.Enabled = !locked;
            topLayer.Enabled = !locked;
            overlayLayer.Enabled = !locked;
        }

        void ConfigureLockSurfaces()
        {
            foreach (var (lockSurface, lockScene) in lockSurfaces)
            {
                var box = layout.BoxOf(lockSurface.Output.Output);
                lockScene.Tree.SetPosition(box.X, box.Y);
                lockSurface.Configure(box.Width, box.Height);
            }
        }

        sessionLock.Locked += () =>
        {
            SetSceneLocked(true);
            seat.Keyboard.NotifyClearFocus();
            textInput?.NotifyFocus(null);
            seat.Pointer.NotifyClearFocus();
            Console.WriteLine("LOCKED");
        };
        sessionLock.Unlocked += () =>
        {
            foreach (var (_, lockScene) in lockSurfaces)
            {
                lockScene.Destroy();
            }

            lockSurfaces.Clear();
            SetSceneLocked(false);
            Console.WriteLine("UNLOCKED");
        };
        sessionLock.Abandoned += () => Console.WriteLine("LOCK ABANDONED (staying blanked)");
        sessionLock.NewLockSurface += lockSurface =>
        {
            var lockScene = new SceneSurface(lockTree, lockSurface.Surface);
            RefreshSurfaceLuts();
            var box = layout.BoxOf(lockSurface.Output.Output);
            lockScene.Tree.SetPosition(box.X, box.Y);
            fractionalScale.AnnounceScale(lockSurface.Surface, lockSurface.Output.Output.Scale);
            lockSurfaces.Add((lockSurface, lockScene));
            lockSurface.Mapped += () => seat.Keyboard.NotifyEnter(lockSurface.Surface);
            lockSurface.Unmapped += () =>
            {
                var index = lockSurfaces.FindIndex(entry => entry.Lock == lockSurface);
                if (index < 0)
                {
                    return;
                }

                lockSurfaces[index].Scene.Destroy();
                lockSurfaces.RemoveAt(index);
            };
        };

        void ArrangeLayers()
        {
            foreach (var view in views)
            {
                var box = layout.BoxOf(view.Output);
                var onOutput = layers.Where(entry => entry.Layer.Output?.Output == view.Output).ToList();
                var usable = Basin.Desktop.LayerArrangement.Arrange(box, onOutput);
                management.LayerShell.SetNonExclusiveArea(
                    view.Output, usable with { X = box.X + usable.X, Y = box.Y + usable.Y });
            }
        }

        LayerSurface? onDemandFocus = null;

        void RefreshLayerFocus()
        {
            LayerSurface? exclusive = null;
            foreach (var (layer, scene) in layers)
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

        layerShell.NewSurface += layer =>
        {
            if (!management.LayerShell.IsSupported || views.Count == 0)
            {
                layer.Close();
                return;
            }

            layer.Output ??= (ViewOf(management.LayerShell.DefaultOutput) ?? views[0]).Global;
            layers.Add((layer, null));

            layer.InitialCommit += ArrangeLayers;
            layer.Mapped += () =>
            {
                var tree = layer.Layer switch
                {
                    LayerKind.Background => backgroundLayer,
                    LayerKind.Bottom => bottomLayer,
                    LayerKind.Top => topLayer,
                    _ => overlayLayer,
                };
                var index = layers.FindIndex(entry => entry.Layer == layer);
                layers[index] = (layer, new SceneSurface(tree, layer.Surface));
                ArrangeLayers();
                RefreshLayerFocus();
                RefreshSurfaceLuts();
            };
            layer.Unmapped += () =>
            {
                var index = layers.FindIndex(entry => entry.Layer == layer);
                if (index < 0)
                {
                    return;
                }

                layers[index].Scene?.Destroy();
                layers.RemoveAt(index);
                if (ReferenceEquals(onDemandFocus, layer))
                {
                    onDemandFocus = null;
                }

                ArrangeLayers();
                RefreshLayerFocus();
            };
            layer.Committed += () =>
            {
                ArrangeLayers();
                RefreshLayerFocus();
            };
            layer.PopupAdopted += popup =>
            {
                var index = layers.FindIndex(entry => entry.Layer == layer);
                if (index < 0 || layers[index].Scene is not { } layerScene || layer.Output?.Output is not { } output)
                {
                    return;
                }

                WireLayerPopup(popup, layerScene.Tree, output);
            };
        };

        void WireLayerPopup(XdgPopupWindow popup, SceneTree parentTree, IOutput output)
        {
            var scene = new SceneSurface(parentTree, popup.Surface);
            layerPopups[popup] = scene;

            void Place()
            {
                var origin = popup.Parent?.Role is XdgPopupWindow ? popup.Parent.EffectiveGeometry : default;
                scene.Tree.SetPosition(origin.X + popup.SurfacePosition.X, origin.Y + popup.SurfacePosition.Y);
            }

            void Constrain()
            {
                if (!parentTree.TryMapSceneToLocal(0, 0, out var localX, out var localY))
                {
                    return;
                }

                var origin = popup.Parent?.Role is XdgPopupWindow ? popup.Parent.EffectiveGeometry : default;
                var originX = (int)-localX + origin.X;
                var originY = (int)-localY + origin.Y;
                var box = layout.BoxOf(output);
                popup.Unconstrain(new Box(box.X - originX, box.Y - originY, box.Width, box.Height));
            }

            Constrain();
            Place();
            popup.Xdg.Committed += Place;
            popup.GeometryChanged += Place;
            popup.Repositioned += Constrain;
            scene.Destroyed += () =>
            {
                popup.Xdg.Committed -= Place;
                popup.GeometryChanged -= Place;
                popup.Repositioned -= Constrain;
                layerPopups.Remove(popup);
            };
            popup.Destroyed += scene.Destroy;
        }

        shell.NewPopup += popup =>
        {
            if (popup.Parent?.Role is not XdgPopupWindow)
            {
                return;
            }

            var root = popup;
            var chain = popup.Parent;
            while (chain?.Role is XdgPopupWindow parentPopup)
            {
                root = parentPopup;
                chain = parentPopup.Parent;
            }

            if (chain is not null)
            {
                return;
            }

            popup.Xdg.Mapped += () =>
            {
                if (layerPopups.ContainsKey(popup) ||
                    popup.Parent?.Role is not XdgPopupWindow parentPopup ||
                    !layerPopups.TryGetValue(parentPopup, out var parentScene) ||
                    root.LayerParent?.Output?.Output is not { } output)
                {
                    return;
                }

                WireLayerPopup(popup, parentScene.Tree, output);
            };
        };

        ArrangeLayers();

        Basin.XWayland.XWaylandServer? xServer = null;
        if (xwayland)
        {
            var shellGlobal = services.Require<Basin.XWayland.XwaylandShellGlobal>();
            xServer = services.Require<Basin.XWayland.XWaylandServer>();
            xServer.Ready += wmFd =>
            {
                var wm = new Basin.XWayland.XWaylandWm(wmFd, loop, shellGlobal, seat);
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
                var onDemand = layers.Find(entry =>
                    entry.Scene is not null && ReferenceEquals(entry.Layer.Surface, clicked) &&
                    entry.Layer.KeyboardInteractivity ==
                        Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.KeyboardInteractivity.OnDemand).Layer;
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

        var touchPoints = new TouchPoints();
        var emulator = new Basin.Seat.TouchPointerEmulator(seat.Touch);

        void TouchDown(uint timeMs, int slot, double x, double y)
        {
            var over = management.HasPointerOperation(seat) ? null : scene.SurfaceAt(x, y);
            if (over is { Surface: { } touched } hit && seat.Touch.Accepts(touched))
            {
                touchPoints.Down(slot, x, y, hit.Node);
                management.NotifyInteraction(seat, touched);
                seat.Touch.NotifyDown(touched, timeMs, slot, hit.X, hit.Y);
                return;
            }

            if (!emulator.TryClaim(slot, over?.Surface))
            {
                return;
            }

            log.LogDebug("touch {Slot} at {X},{Y} drives the pointer", slot, x, y);
            pointer.Warp(x, y);
            PointerMoved(timeMs);
            DeliverPointerButton(timeMs, BtnLeft, true);
        }

        void TouchMotion(uint timeMs, int slot, double x, double y)
        {
            if (emulator.Owns(slot))
            {
                pointer.Warp(x, y);
                PointerMoved(timeMs);
                return;
            }

            if (touchPoints.TryMotion(slot, x, y, out var localX, out var localY))
            {
                seat.Touch.NotifyMotion(timeMs, slot, localX, localY);
            }
        }

        void TouchUp(uint timeMs, int slot)
        {
            if (emulator.Release(slot))
            {
                DeliverPointerButton(timeMs, BtnLeft, false);
                return;
            }

            if (touchPoints.Up(slot))
            {
                seat.Touch.NotifyUp(timeMs, slot);
            }
        }

        void TouchCancel()
        {
            touchPoints.Clear();
            if (emulator.Cancel())
            {
                DeliverPointerButton((uint)Environment.TickCount, BtnLeft, false);
            }

            seat.Touch.NotifyCancel();
        }

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

            if (scales.Length == 0 && view.Output is WaylandOutput hosted)
            {
                hosted.HostScaleChanged += () =>
                {
                    if (Math.Abs(hosted.HostScale - hosted.Scale) < 0.0001)
                    {
                        return;
                    }

                    using var hostState = new OutputState();
                    if (!hosted.Commit(hostState.SetScale(hosted.HostScale)))
                    {
                        return;
                    }

                    Console.WriteLine($"SCALE {hosted.Name} {hosted.Scale}");
                    OnOutputsReconfigured();
                };
            }
        };
        driver.Removed += view =>
        {
            if (view.Output is Basin.Backend.Drm.DrmOutput card)
            {
                Console.WriteLine($"OUTPUT - {card.Name}");
                management.RemoveOutput(card);
            }

            cursor.RemoveOutput(view.Output);
        };
        driver.LayoutChanged += () =>
        {
            ArrangeLayers();
            ConfigureLockSurfaces();
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
                UpdateTouchCapability();
            }

            void UpdateTouchCapability() =>
                seat.SetCapability(Basin.Seat.SeatCapability.Touch, libinput.HasTouchDevice);

            IOutput? TouchOutput(Basin.Backend.Libinput.InputDevice device) =>
                ((device.OutputName is { } name
                    ? views.FirstOrDefault(v => v.Output.Name == name)
                    : null) ?? (views.Count > 0 ? views[0] : null))?.Output;

            libinput.DeviceAdded += Register;
            libinput.DeviceRemoved += device =>
            {
                management.InputManager.RemoveDevice(device);
                management.XkbConfig.RemoveKeyboard(device);
                if (keyboardDevices.Remove(device, out var keyboard))
                {
                    keyboard.Dispose();
                }

                UpdateTouchCapability();
            };

            libinput.Key += (device, timeMs, key, pressed) =>
            {
                seat.Keyboard.Activate(keyboardDevices.GetValueOrDefault(device));
                DeliverKey(timeMs, key, pressed);
            };

            libinput.PointerMotion += (_, timeMs, dx, dy, _, _) =>
            {
                pointer.Motion(dx, dy);
                PointerMoved(timeMs);
            };

            libinput.PointerMotionAbsolute += (_, timeMs, normalizedX, normalizedY) =>
            {
                pointer.MotionAbsolute(null, normalizedX, normalizedY);
                PointerMoved(timeMs);
            };

            libinput.PointerButton += (_, timeMs, button, pressed) =>
                DeliverPointerButton(timeMs, button, pressed);

            libinput.PointerScroll += (_, timeMs, axis) =>
            {
                seat.Pointer.NotifyAxis(timeMs, axis);
                seat.Pointer.NotifyFrame();
            };

            libinput.TouchDown += (device, timeMs, slot, normalizedX, normalizedY) =>
            {
                if (TouchOutput(device) is not { } on)
                {
                    return;
                }

                var (x, y) = layout.FromNormalized(on, normalizedX, normalizedY);
                TouchDown(timeMs, slot, x, y);
            };

            libinput.TouchMotion += (device, timeMs, slot, normalizedX, normalizedY) =>
            {
                if (TouchOutput(device) is not { } on)
                {
                    return;
                }

                var (x, y) = layout.FromNormalized(on, normalizedX, normalizedY);
                TouchMotion(timeMs, slot, x, y);
            };

            libinput.TouchUp += (_, timeMs, slot) => TouchUp(timeMs, slot);
            libinput.TouchFrame += _ => seat.Touch.NotifyFrame();
            libinput.TouchCancel += _ => TouchCancel();

            management.InputManager.SeatCreated += name => Console.WriteLine($"SEAT + {name}");
            management.InputManager.SeatDestroyed += name => Console.WriteLine($"SEAT - {name}");
            management.InputManager.DeviceAssigned += (device, name) =>
                Console.WriteLine($"ASSIGN {(device as Basin.Backend.Libinput.InputDevice)?.Name} -> {name}");

            libinput.Start();
        }

        void WireParentKeyboard(WaylandKeyboardDevice parentKeyboard)
        {
            Console.WriteLine("INPUT Keyboard host");
            parentKeyboard.RepeatInfo += (rate, delay) => seat.Keyboard.SetRepeatInfo(rate, delay);
            parentKeyboard.Key += (timeMs, key, pressed) =>
            {
                seat.Keyboard.Activate(null);
                DeliverKey(timeMs, key, pressed);
            };
            parentKeyboard.Modifiers += (depressed, latched, locked, group) =>
            {
                seat.Keyboard.Activate(null);
                seat.Keyboard.NotifyModifiers(depressed, latched, locked, group);
                management.NotifyModifiers(seat);
            };
        }

        void WireParentPointer(WaylandPointerDevice parentPointer)
        {
            Console.WriteLine("INPUT Pointer host");

            cursor.AttachParent(parentPointer);

            void MoveTo(uint timeMs, WaylandOutput on, double physicalX, double physicalY)
            {
                var (layoutX, layoutY) = layout.ToLayout(on, physicalX, physicalY);
                pointer.Warp(layoutX, layoutY);
                PointerMoved(timeMs);
            }

            parentPointer.Enter += (on, x, y) => MoveTo((uint)Environment.TickCount, on, x, y);
            parentPointer.Motion += (timeMs, x, y) =>
            {
                if (layout.OutputAt(pointer.X, pointer.Y) is WaylandOutput on)
                {
                    MoveTo(timeMs, on, x, y);
                }
                else if (views[0].Output is WaylandOutput first)
                {
                    MoveTo(timeMs, first, x, y);
                }
            };
            parentPointer.Leave += () => seat.Pointer.NotifyClearFocus();
            parentPointer.Button += (timeMs, button, pressed) =>
                DeliverPointerButton(timeMs, button, pressed);
            parentPointer.Axis += (timeMs, axis) =>
            {
                seat.Pointer.NotifyAxis(timeMs, axis);
                seat.Pointer.NotifyFrame();
            };
        }

        void WireParentTouch(WaylandTouchDevice parentTouch)
        {
            Console.WriteLine("INPUT Touch host");
            seat.SetCapability(Basin.Seat.SeatCapability.Touch, true);

            parentTouch.Down += (on, timeMs, slot, physicalX, physicalY) =>
            {
                var (x, y) = layout.ToLayout(on, physicalX, physicalY);
                TouchDown(timeMs, slot, x, y);
            };
            parentTouch.Motion += (on, timeMs, slot, physicalX, physicalY) =>
            {
                var (x, y) = layout.ToLayout(on, physicalX, physicalY);
                TouchMotion(timeMs, slot, x, y);
            };
            parentTouch.Up += TouchUp;
            parentTouch.Frame += () => seat.Touch.NotifyFrame();
            parentTouch.Cancel += TouchCancel;
        }

        NestedSeam? seam = null;
        if (parent is not null)
        {
            parent.KeyboardAdded += WireParentKeyboard;
            parent.PointerAdded += WireParentPointer;
            parent.TouchAdded += WireParentTouch;
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
        injectedInput.OnPointerMotionAbsolute = (timeMs, x, y, extentWidth, extentHeight) =>
        {
            var box = layout.Bounds;
            if (box.Width <= 0 || box.Height <= 0)
            {
                return true;
            }

            pointer.Warp(
                box.X + (x / extentWidth * box.Width),
                box.Y + (y / extentHeight * box.Height));
            PointerMoved(timeMs);
            return true;
        };
        injectedInput.OnPointerButton = (timeMs, button, pressed) =>
        {
            DeliverPointerButton(timeMs, button, pressed);
            return true;
        };

        var running = true;
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

        long PrimaryRendered() => views.Count > 0 ? views[0].Rendered : 0;

        var started = Environment.TickCount64;
        var reported = 0L;
        while (running && (frames == 0 || PrimaryRendered() < frames))
        {
            if (PrimaryRendered() - reported >= 300)
            {
                reported = PrimaryRendered();
                var seconds = (Environment.TickCount64 - started) / 1000.0;
                Console.WriteLine(
                    $"STATS {reported} frames in {seconds:F1}s ({reported / seconds:F1}/s) " +
                    $"manage={management.ManageSequences} render={management.RenderSequences} " +
                    $"timedout={Transaction.TimedOutCount}");
            }

            loop.Dispatch(16);
            if (fifo.HasPendingBarriers)
            {
                foreach (var view in views)
                {
                    view.Scheduler?.ScheduleRepaint();
                }
            }

            parent?.Flush();
        }

        if (screenshotPath is not null)
        {
            for (var i = 0; i < views.Count; i++)
            {
                var path = ScreenshotPath(screenshotPath, i);
                var shot = views[i].Scene?.LastTarget is { } presented &&
                    BufferCapture.TryWritePng(presented, renderer, path);
                Console.WriteLine(shot
                    ? $"SHOT {path} after {views[i].Rendered} frames"
                    : $"SHOT failed: no readable presented frame on {views[i].Output.Name} after {views[i].Rendered} frames");
            }
        }

        totalRendered = PrimaryRendered();

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
