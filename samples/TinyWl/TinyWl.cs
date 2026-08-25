using System.CommandLine;
using Basin;
using Basin.Backend.Drm;
using Basin.Backend.Libinput;
using Basin.Backend.Wayland;
using Basin.Cli;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.Seat;
using Basin.Shell.Xdg;
using Wayland;
using Wayland.Server;
using Xkb;

namespace TinyWl;

internal sealed class TinyWl : IDisposable
{
    private static readonly RenderColor Background = new(0.15f, 0.15f, 0.2f, 1f);

    private static readonly XkbKeysym Escape = XkbKeysym.FromName("Escape");
    private static readonly XkbKeysym F1 = XkbKeysym.FromName("F1");

    private readonly WlServerDisplay _display;
    private readonly WaylandEventLoop _loop;
    private readonly string _socket;

    private readonly ClientBufferRegistry _buffers = new();
    private readonly ShmGlobal _shm;
    private readonly CompositorGlobal _compositor;
    private readonly SubcompositorGlobal _subcompositor;
    private readonly LinuxDmabufGlobal? _dmabuf;
    private readonly Seat _seat;
    private readonly DataDeviceManager _dataDevices;

    private readonly Scene _scene = new();
    private readonly OutputLayout _layout = new();
    private readonly LayoutPointer _pointer;
    private readonly XdgShell _shell;

    private readonly List<Toplevel> _toplevels = [];
    private readonly List<Output> _outputs = [];

    private readonly Dictionary<XdgSurfaceState, SceneSurface> _surfaceScenes = [];

    private readonly IRenderer _renderer;
    private readonly IAllocator _allocator;
    private readonly DrmFormat _format = DrmFormat.Xrgb8888;
    private readonly ulong[] _modifiers;
    private readonly OutputState _frameState = new();

    private readonly WaylandBackend? _backend;
    private readonly DrmBackend? _drm;
    private readonly LibinputBackend? _libinput;
    private readonly Basin.Session.ISession? _session;

    private readonly CursorImages? _cursors;
    private readonly IAllocator? _cursorAllocator;
    private CursorImage? _showing;
    private bool _showingClientCursor;

    private CursorMode _cursorMode = CursorMode.Passthrough;
    private Toplevel? _grabbed;
    private double _grabX, _grabY;
    private Box _grabGeometry;
    private ResizeEdges _grabEdges;

    private readonly IEventSource _interrupt;

    private readonly BasinLogger _log;

    private bool _running = true;

    public TinyWl(bool drm, string? rendererName, BasinLogger log)
    {
        _log = log;
        _display = WlServerDisplay.Create();
        _socket = _display.AddSocketAuto();
        _loop = new WaylandEventLoop(_display);

        _shm = new ShmGlobal(_display, buffers: _buffers);
        _compositor = new CompositorGlobal(_display, _buffers);
        _subcompositor = new SubcompositorGlobal(_display, _compositor);
        _seat = new Seat(_display, _compositor, capabilities: SeatCapability.Pointer);
        _dataDevices = new DataDeviceManager(_display, _seat);

        if (drm)
        {
            _session = Basin.Session.SeatdSession.Open(_loop);
            _drm = new DrmBackend(_loop, _session, Environment.GetEnvironmentVariable("BASIN_DRM_DEVICE"));
            _drm.Start();
        }
        else
        {
            _backend = new WaylandBackend(_loop);
            _backend.Start();
        }

        var renderNode = _drm?.RenderNodePath ?? DefaultRenderNode();
        var stack = CreateRenderer(ref rendererName, renderNode);
        _renderer = stack.Renderer;
        (_allocator, _modifiers) = ChooseAllocator(stack);
        _log.Info($"{rendererName} renderer, {(_modifiers.Length > 0 ? "dmabuf" : "shm")} presentation");

        if (_renderer.Device is { } device)
        {
            _dmabuf = new LinuxDmabufGlobal(
                _display, _buffers, _renderer.DmabufTextureFormats, device.DevicePath, compositor: _compositor);
        }

        _shell = new XdgShell(_display, _compositor, _seat);
        _shell.NewToplevel += OnNewToplevel;
        _shell.NewPopup += OnNewPopup;

        _pointer = new LayoutPointer(_layout);
        _cursorAllocator = new ShmAllocator();
        _cursors = new CursorImages(_cursorAllocator, bufferWidth: 64, bufferHeight: 64, logicalSize: 24);
        if (!_cursors.HasTheme)
        {
            _log.Warn($"no xcursor theme found; running without a visible cursor");
        }

        _seat.Pointer.CursorRequested += OnClientCursor;
        if (_drm is not null)
        {
            foreach (var output in _drm.Outputs)
            {
                AddOutput(output);
            }

            _drm.OutputAdded += AddOutput;
            _drm.OutputRemoved += RemoveOutput;

            _libinput = new LibinputBackend(_loop, _session!);
            WireLibinput(_libinput);
            _libinput.Start();
            _seat.Keyboard.SetKeymap(Basin.Seat.SystemKeymap.Read());
            _seat.Keyboard.SetRepeatInfo(25, 600);
        }
        else
        {
            _backend!.ParentGone += () => _running = false;
            _backend.PointerAdded += WirePointer;
            _backend.KeyboardAdded += WireKeyboard;
            AddOutput(_backend.CreateOutput("tinywl"));
        }

        SetThemeCursor();

        _interrupt = _loop.AddSignal(Signal.Interrupt, _ => _running = false);
    }

    public int Run(string? startupCommand)
    {
        BasinReport.Line($"tinywl: running Wayland compositor on WAYLAND_DISPLAY={_socket}");
        using var startup = BasinDiagnostics.StartClient(startupCommand, _socket);
        while (_running)
        {
            _backend?.Flush();
            _loop.Dispatch(-1);
        }

        BasinDiagnostics.StopClient(startup);
        return 0;
    }

    private void FocusToplevel(Toplevel? toplevel)
    {
        if (toplevel is null || ReferenceEquals(_seat.Keyboard.Focus, toplevel.Surface))
        {
            return;
        }

        if (_seat.Keyboard.Focus is { } previous && ToplevelOwning(previous) is { } previousToplevel)
        {
            previousToplevel.Window.SetActivated(false);
        }

        toplevel.Tree.RaiseToTop();
        _toplevels.Remove(toplevel);
        _toplevels.Insert(0, toplevel);
        toplevel.Window.SetActivated(true);

        _seat.Keyboard.NotifyEnter(toplevel.Surface);
    }

    private Toplevel? ToplevelOwning(Surface surface)
    {
        for (var candidate = surface; candidate is not null; candidate = candidate.SubsurfaceRole?.Parent)
        {
            foreach (var toplevel in _toplevels)
            {
                if (ReferenceEquals(candidate, toplevel.Surface))
                {
                    return toplevel;
                }
            }
        }

        return null;
    }

    private (Toplevel? Toplevel, Surface? Surface, double X, double Y) ToplevelAt(double x, double y)
    {
        if (_scene.SurfaceAt(x, y) is not { } hit || hit.Surface is not { } surface)
        {
            return (null, null, 0, 0);
        }

        return (ToplevelOwning(surface), surface, hit.X, hit.Y);
    }

    private void WireKeyboard(WaylandKeyboardDevice keyboard)
    {
        _seat.Capabilities |= SeatCapability.Keyboard;
        keyboard.Keymap += bytes => _seat.Keyboard.SetKeymapFromBuffer(bytes);
        keyboard.Modifiers += (depressed, latched, locked, group) =>
            _seat.Keyboard.NotifyModifiers(depressed, latched, locked, group);
        keyboard.Key += OnKey;
    }

    private void OnKey(uint timeMs, uint key, bool pressed)
    {
        if (pressed && _seat.Keyboard.State?.IsModActive("Mod1") == true && HandleKeybinding(_seat.Keyboard.KeysymFor(key)))
        {
            return;
        }

        _seat.Keyboard.NotifyKey(timeMs, key, pressed);
    }

    private bool HandleKeybinding(XkbKeysym symbol)
    {
        if (symbol == Escape)
        {
            _running = false;
            return true;
        }

        if (symbol == F1)
        {
            if (_toplevels.Count >= 2)
            {
                FocusToplevel(_toplevels[^1]);
            }

            return true;
        }

        return false;
    }

    private void WirePointer(WaylandPointerDevice pointer)
    {
        pointer.Enter += (output, x, y) =>
        {
            var (layoutX, layoutY) = _layout.ToLayout(output, x, y);
            _pointer.Warp(layoutX, layoutY);
            ProcessCursorMotion((uint)Environment.TickCount);

            ShowCursor();
        };
        pointer.Motion += (timeMs, x, y) =>
        {
            var output = _layout.OutputAt(_pointer.X, _pointer.Y) ?? _outputs[0].Handle;
            var (layoutX, layoutY) = _layout.ToLayout(output, x, y);
            _pointer.Warp(layoutX, layoutY);
            ProcessCursorMotion(timeMs);
        };
        pointer.Button += OnButton;
        pointer.Axis += (timeMs, axis) => _seat.Pointer.NotifyAxis(timeMs, axis);
        pointer.Leave += () => _seat.Pointer.NotifyClearFocus();
    }

    private void WireLibinput(LibinputBackend input)
    {
        input.DeviceAdded += device =>
        {
            if (device.HasKeyboard)
            {
                _seat.Capabilities |= SeatCapability.Keyboard;
            }
        };
        input.Key += (_, timeMs, key, pressed) => OnKey(timeMs, key, pressed);
        input.PointerButton += (_, timeMs, button, pressed) => OnButton(timeMs, button, pressed);
        input.PointerMotion += (_, timeMs, dx, dy, _, _) =>
        {
            _pointer.Motion(dx, dy);
            ProcessCursorMotion(timeMs);
        };
        input.PointerMotionAbsolute += (_, timeMs, normalizedX, normalizedY) =>
        {
            _pointer.MotionAbsolute(null, normalizedX, normalizedY);
            ProcessCursorMotion(timeMs);
        };
        input.PointerScroll += (_, timeMs, axis) => _seat.Pointer.NotifyAxis(timeMs, axis);
    }

    private void ProcessCursorMotion(uint timeMs)
    {
        MoveCursorImage();
        switch (_cursorMode)
        {
            case CursorMode.Move:
                ProcessCursorMove();
                return;
            case CursorMode.Resize:
                ProcessCursorResize();
                return;
        }

        var (toplevel, surface, surfaceX, surfaceY) = ToplevelAt(_pointer.X, _pointer.Y);
        if (toplevel is null)
        {
            SetThemeCursor();
        }

        if (surface is not null)
        {
            _seat.Pointer.NotifyMotionAt(timeMs, surface, surfaceX, surfaceY, _pointer.X, _pointer.Y);
        }
        else
        {
            _seat.Pointer.NotifyClearFocus();
        }
    }

    private void ProcessCursorMove()
    {
        _grabbed!.Tree.SetPosition((int)(_pointer.X - _grabX), (int)(_pointer.Y - _grabY));
    }

    private void ProcessCursorResize()
    {
        var borderX = _pointer.X - _grabX;
        var borderY = _pointer.Y - _grabY;
        var left = _grabGeometry.X;
        var right = _grabGeometry.X + _grabGeometry.Width;
        var top = _grabGeometry.Y;
        var bottom = _grabGeometry.Y + _grabGeometry.Height;

        if (_grabEdges.HasFlag(ResizeEdges.Top))
        {
            top = Math.Min((int)borderY, bottom - 1);
        }
        else if (_grabEdges.HasFlag(ResizeEdges.Bottom))
        {
            bottom = Math.Max((int)borderY, top + 1);
        }

        if (_grabEdges.HasFlag(ResizeEdges.Left))
        {
            left = Math.Min((int)borderX, right - 1);
        }
        else if (_grabEdges.HasFlag(ResizeEdges.Right))
        {
            right = Math.Max((int)borderX, left + 1);
        }

        var geometry = _grabbed!.Geometry;
        _grabbed.Tree.SetPosition(left - geometry.X, top - geometry.Y);
        _grabbed.Window.SetSize(right - left, bottom - top);
    }

    private void OnButton(uint timeMs, uint button, bool pressed)
    {
        _seat.Pointer.NotifyButton(timeMs, button, pressed);
        if (!pressed)
        {
            ResetCursorMode();
            return;
        }

        var (toplevel, _, _, _) = ToplevelAt(_pointer.X, _pointer.Y);
        FocusToplevel(toplevel);
    }

    private void BeginInteractive(Toplevel toplevel, CursorMode mode, ResizeEdges edges)
    {
        _grabbed = toplevel;
        _cursorMode = mode;
        if (mode == CursorMode.Move)
        {
            _grabX = _pointer.X - toplevel.Tree.X;
            _grabY = _pointer.Y - toplevel.Tree.Y;
            return;
        }

        var geometry = toplevel.Geometry;
        var borderX = toplevel.Tree.X + geometry.X + (edges.HasFlag(ResizeEdges.Right) ? geometry.Width : 0);
        var borderY = toplevel.Tree.Y + geometry.Y + (edges.HasFlag(ResizeEdges.Bottom) ? geometry.Height : 0);
        _grabX = _pointer.X - borderX;
        _grabY = _pointer.Y - borderY;
        _grabGeometry = new Box(
            toplevel.Tree.X + geometry.X, toplevel.Tree.Y + geometry.Y, geometry.Width, geometry.Height);
        _grabEdges = edges;
        toplevel.Window.SetResizing(true);
    }

    private void ResetCursorMode()
    {
        _grabbed?.Window.SetResizing(false);
        _cursorMode = CursorMode.Passthrough;
        _grabbed = null;
    }

    private void SetThemeCursor()
    {
        if (_showingClientCursor || _showing is null)
        {
            _showingClientCursor = false;
            _showing = _cursors?.Named("left_ptr");
            ShowCursor();
        }
    }

    private void OnClientCursor(CursorRequest request)
    {
        if (request.Surface?.Current.Buffer is not { } buffer)
        {
            _showingClientCursor = false;
            SetThemeCursor();
            return;
        }

        _showing = _cursors?.FromSurface(buffer, request.HotspotX, request.HotspotY);
        _showingClientCursor = true;
        ShowCursor();
    }

    private void ShowCursor()
    {
        if (_showing is not { } image)
        {
            return;
        }

        if (_backend?.Pointer is { } parentPointer)
        {
            parentPointer.SetCursor(image.Buffer, image.HotspotX, image.HotspotY);
            return;
        }

        foreach (var output in _outputs)
        {
            output.Scene?.SetSoftwareCursor(image.Buffer, image.HotspotX, image.HotspotY);
        }
    }

    private void MoveCursorImage()
    {
        if (_backend is not null)
        {
            return;
        }

        foreach (var output in _outputs)
        {
            var box = _layout.BoxOf(output.Handle);
            output.Scene?.MoveSoftwareCursor((int)(_pointer.X - box.X), (int)(_pointer.Y - box.Y));
        }
    }

    private void AddOutput(IOutput handle)
    {
        var entry = new Output(handle, new OutputGlobal(_display, handle));
        _outputs.Add(entry);

        _frameState.Clear();
        _frameState.SetEnabled(true);
        if (handle is DrmOutput card)
        {
            _frameState.SetMode(card.PreferredMode);
        }

        if (!handle.Commit(_frameState))
        {
            _log.Warn($"{handle.Name} refused its initial state");
        }

        _layout.Add(handle, 0, 0);
        _layout.ArrangeHorizontally(_outputs.Select(o => o.Handle));

        entry.Scene = new SceneOutput(_scene, handle);
        entry.Scheduler = new OutputScheduler(_loop, handle);
        entry.Scheduler.Repaint += () => Repaint(entry);
        entry.Scene.DamagePending += entry.Scheduler.ScheduleRepaint;
        handle.Committed += _ => entry.Scheduler.ScheduleRepaint();
        if (handle is WaylandOutput nested)
        {
            nested.CloseRequested += () => _running = false;
        }

        _log.Info($"output {handle.Name} {handle.CurrentMode.Width}x{handle.CurrentMode.Height}");
        entry.Scheduler.ScheduleRepaint();

        if (_outputs.Count == 1 && _drm is not null)
        {
            var box = _layout.BoxOf(handle);
            _pointer.Warp(box.X + (box.Width / 2.0), box.Y + (box.Height / 2.0));
            ShowCursor();
        }
    }

    private void RemoveOutput(DrmOutput handle)
    {
        if (_outputs.FirstOrDefault(o => ReferenceEquals(o.Handle, handle)) is not { } entry)
        {
            return;
        }

        _outputs.Remove(entry);
        _layout.Remove(handle);
        _layout.ArrangeHorizontally(_outputs.Select(o => o.Handle));
        entry.Scheduler?.Dispose();
        entry.Scene?.Dispose();
        entry.Swapchain?.Dispose();
        entry.Global.Dispose();
    }

    private void Repaint(Output entry)
    {
        var mode = entry.Handle.CurrentMode;
        if (mode.Width <= 0 || mode.Height <= 0)
        {
            return;
        }

        if (entry.Swapchain is null)
        {
            entry.Swapchain = new Swapchain(_allocator, mode.Width, mode.Height, _format, _modifiers);
        }
        else if (entry.Swapchain.Width != mode.Width || entry.Swapchain.Height != mode.Height)
        {
            entry.Swapchain.Resize(mode.Width, mode.Height);
        }

        var box = _layout.BoxOf(entry.Handle);
        entry.Scene!.Position = new Point(box.X, box.Y);

        _frameState.Clear();
        var committed = entry.Scene.Commit(
            _renderer, entry.Swapchain, _frameState, new SceneCommitOptions { Background = Background });
        if (committed)
        {
            entry.Scheduler!.NotifyCommitted();
        }
        else if (entry.Scene.NeedsRepaint)
        {
            entry.Scheduler!.ScheduleRepaint();
            return;
        }

        _scene.SendFrameDone((uint)Environment.TickCount);
    }

    private void OnNewToplevel(XdgToplevelWindow window)
    {
        var scene = new SceneSurface(_scene.Root, window.Surface);
        scene.Tree.Enabled = false;
        _surfaceScenes[window.Xdg] = scene;
        var toplevel = new Toplevel(window, scene);

        window.Xdg.Mapped += () =>
        {
            scene.Tree.Enabled = true;
            _toplevels.Insert(0, toplevel);
            FocusToplevel(toplevel);
        };
        window.Xdg.Unmapped += () =>
        {
            if (ReferenceEquals(_grabbed, toplevel))
            {
                ResetCursorMode();
            }

            scene.Tree.Enabled = false;
            _toplevels.Remove(toplevel);
        };
        window.Destroyed += () =>
        {
            _surfaceScenes.Remove(window.Xdg);
            _toplevels.Remove(toplevel);
        };

        window.MoveRequested += _ => BeginInteractive(toplevel, CursorMode.Move, ResizeEdges.None);
        window.ResizeRequested += (_, edges) => BeginInteractive(toplevel, CursorMode.Resize, edges);

        window.MaximizeRequested += _ => window.RequestConfigure();
        window.FullscreenRequested += _ => window.RequestConfigure();
    }

    private void OnNewPopup(XdgPopupWindow popup)
    {
        if (popup.Parent is not { } parent || !_surfaceScenes.TryGetValue(parent, out var parentScene))
        {
            return;
        }

        var scene = new SceneSurface(parentScene.Tree, popup.Surface);
        _surfaceScenes[popup.Xdg] = scene;

        void Place()
        {
            var origin = parent.EffectiveGeometry;
            var offset = popup.SurfacePosition;
            scene.Tree.SetPosition(origin.X + offset.X, origin.Y + offset.Y);
        }

        Place();
        popup.Xdg.Committed += Place;
        popup.GeometryChanged += Place;
        popup.Destroyed += () =>
        {
            popup.Xdg.Committed -= Place;
            popup.GeometryChanged -= Place;
            _surfaceScenes.Remove(popup.Xdg);
        };
    }

    private static string? DefaultRenderNode() =>
        File.Exists("/dev/dri/renderD128") ? "/dev/dri/renderD128" : null;

    private RenderStack CreateRenderer(ref string? name, string? renderNode)
    {
        name ??= renderNode is null ? "pixman" : "gl";
        try
        {
            return Basin.Renderers.RendererCatalog.Create(name, renderNode ?? string.Empty);
        }
        catch (Exception error) when (error is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            if (name == "pixman")
            {
                throw;
            }

            _log.Warn($"{name} renderer unavailable ({error.Message}); falling back to pixman");
            name = "pixman";
            return Basin.Renderers.RendererCatalog.Create(name, string.Empty);
        }
    }

    private (IAllocator Allocator, ulong[] Modifiers) ChooseAllocator(RenderStack stack)
    {
        if (stack.DeviceAllocator is { } device)
        {
            var importer = _drm is not null
                ? _drm.Outputs.Count > 0 ? _drm.Outputs[0].ScanoutFormats : DrmFormatSet.Empty
                : _backend!.ParentDmabufFormats;
            var modifiers = SwapchainFormats.CommonModifiers(device, importer, _format);
            if (modifiers.Length > 0)
            {
                return (device, modifiers);
            }

            device.Dispose();
        }

        return (_drm is not null ? new DumbAllocator(_drm) : new ShmAllocator(), []);
    }

    public void Dispose()
    {
        _interrupt.Remove();
        _scene.Root.Destroy();
        foreach (var entry in _outputs)
        {
            entry.Scheduler?.Dispose();
            entry.Scene?.Dispose();
            entry.Swapchain?.Dispose();
            entry.Global.Dispose();
        }

        _cursors?.Dispose();
        _cursorAllocator?.Dispose();
        _shell.Dispose();
        _dmabuf?.Dispose();
        _dataDevices.Dispose();
        _seat.Dispose();
        _subcompositor.Dispose();
        _compositor.Dispose();
        _libinput?.Dispose();
        _backend?.Dispose();
        _drm?.Dispose();
        _allocator.Dispose();
        _frameState.Dispose();
        _session?.Dispose();
        _display.Dispose();
        _renderer.Dispose();
    }
}
