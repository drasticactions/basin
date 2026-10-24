using Basin.Backend.Hosted;
using Basin.Hosted;
using Basin.Scene;
using Basin.Seat;
using SDL3;
using SkiaSharp;
using static Basin.Sdl.SdlViewLog;

namespace Basin.Sdl;

/// <summary>
/// Pairs one SDL window with one <see cref="BasinViewOutput"/> of the host. It translates SDL input into a list that drains inside the frame.
/// It resizes, rescales and repaints the output as the window changes.
/// </summary>
public sealed class SdlOutputWindow : ISdlEventSink, IDisposable
{
    private readonly SdlLoop _loop;
    private readonly BasinCompositorHost _host;
    private readonly List<BasinViewInput> _input = new(256);
    private readonly Action<FrameTick> _drain;
    private bool _forceFrame = true;
    private bool _captureRequested;
    private readonly double? _scaleOverride;
    private readonly RenderColor _background;
    private readonly bool _grabKeyboard;
    private readonly HostModifierState _modifiers = new();
    private readonly Action<bool> _onLock;
    private readonly Action<double, double> _onWarp;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SdlOutputWindow"/> class. It creates the window, a lease of the device and a view output.
    /// A popup falls back to a borderless utility window when the driver has no popup windows.
    /// </summary>
    /// <param name="loop">The SDL loop that delivers the window events.</param>
    /// <param name="host">The host that owns the output.</param>
    /// <param name="device">The render device that creates the lease.</param>
    /// <param name="options">The window settings, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="InvalidOperationException">SDL cannot create the window, or the device cannot create a lease.</exception>
    public SdlOutputWindow(SdlLoop loop, BasinCompositorHost host, ISdlRenderDevice device, SdlOutputWindowOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(device);
        options ??= new SdlOutputWindowOptions();
        _loop = loop;
        _host = host;
        _scaleOverride = options.Scale;
        _background = options.Transparent ? RenderColor.Transparent : new RenderColor(0f, 0f, 0f, 1f);
        _grabKeyboard = options.GrabKeyboard;
        _device = device;
        if (options.PopupParent is { } parent)
        {
            var popupFlags = device.WindowFlags | (options.Tooltip ? SDL.WindowFlags.Tooltip : SDL.WindowFlags.PopupMenu) |
                (options.Transparent ? SDL.WindowFlags.Transparent : 0);
            try
            {
                Window = new SdlWindow(parent, options.PopupX, options.PopupY, options.Width, options.Height, popupFlags);
            }
            catch (InvalidOperationException error)
            {
                Log.Debug($"no popup windows on this SDL driver ({error.Message}); a borderless window stands in");
                Window = new SdlWindow(
                    options.Title, options.Width, options.Height,
                    device.WindowFlags | SDL.WindowFlags.Borderless | SDL.WindowFlags.Utility);
                _ = SDL.GetWindowPosition(parent.Handle, out var parentX, out var parentY);
                _ = SDL.SetWindowPosition(Window.Handle, parentX + options.PopupX, parentY + options.PopupY);
            }
        }
        else
        {
            var flags = device.WindowFlags | (options.Resizable ? SDL.WindowFlags.Resizable : 0) |
                (options.Transparent ? SDL.WindowFlags.Transparent : 0) |
                (options.Borderless ? SDL.WindowFlags.Borderless : 0);
            Window = new SdlWindow(options.Title, options.Width, options.Height, flags);
        }

        try
        {
            Lease = device.CreateLease(Window);
        }
        catch
        {
            Window.Dispose();
            throw;
        }

        var (width, height) = Window.PixelSize;
        Scale = HostScale();
        RefreshRate = Window.RefreshRate;
        View = host.CreateViewOutput(width, height, Scale, options.OutputName);
        View.RequestRender = () => _forceFrame = true;
        FollowRefresh();
        if (device.EglDisplay != 0)
        {
            host.Renderer.TryEnableEgl(device.EglDisplay);
        }

        ShareDmabufRenderer();
        FollowHdr();

        _drain = _ => DrainInput();
        host.Session.BeforeDispatch += _drain;
        _onLock = OnPointerLock;
        _onWarp = OnPointerWarp;
        host.PointerLockRequested += _onLock;
        host.PointerWarpRequested += _onWarp;
        loop.AddSink(this);
    }

    /// <summary>
    /// Gets the SDL window that the output shows in.
    /// </summary>
    public SdlWindow Window { get; }

    /// <summary>
    /// Gets the lease that draws the window. A new lease replaces it after a context loss.
    /// </summary>
    public ISdlRenderLease Lease { get; private set; }

    private readonly ISdlRenderDevice _device;

    /// <summary>
    /// Gets the view output of the host that the window shows.
    /// </summary>
    public BasinViewOutput View { get; }

    /// <summary>
    /// Gets the output scale, snapped from the display scale or taken from <see cref="SdlOutputWindowOptions.Scale"/>.
    /// </summary>
    public double Scale { get; private set; }

    /// <summary>
    /// Gets or sets the function that receives each input record when the list drains, before the host dispatches.
    /// Without it, the window drops its input.
    /// </summary>
    public Action<BasinViewInput>? InputSink { get; set; }

    /// <summary>
    /// Gets or sets the function that applies a new size in device pixels and a new scale.
    /// When it is <see langword="null"/>, <see cref="ApplySize"/> reconfigures the view output itself.
    /// </summary>
    public Action<int, int, double>? ResizeHandler { get; set; }

    /// <summary>
    /// Occurs when the user asks to close the window. The window stays open until the consumer disposes it.
    /// </summary>
    public event Action<SdlOutputWindow>? CloseRequested;

    /// <summary>
    /// Occurs when the window moves to another display, after it reads the refresh rate and the HDR state again.
    /// </summary>
    public event Action<SdlOutputWindow>? DisplayChanged;

    /// <summary>
    /// Occurs after <see cref="ApplySize"/> resized the lease and the output.
    /// </summary>
    public event Action<SdlOutputWindow>? Reconfigured;

    /// <summary>
    /// Occurs when SDL reports that the window was minimized, maximized, restored, or entered or left fullscreen. The second argument is the SDL event type.
    /// </summary>
    public event Action<SdlOutputWindow, SDL.EventType>? StateChanged;

    /// <summary>
    /// Gets the refresh rate of the window display in hertz, or 0 when SDL reports none.
    /// </summary>
    public float RefreshRate { get; private set; }

    /// <summary>
    /// Gets the number of frames that the window presented.
    /// </summary>
    public long Presented { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the window is minimized, hidden or occluded.
    /// </summary>
    public bool IsHidden { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the window is visible and has damage or a forced frame.
    /// </summary>
    public bool NeedsFrame => !IsHidden && (_forceFrame || View.SceneOutput.NeedsRepaint);

    /// <summary>
    /// Gets the RGBA pixels of the frame after the last <see cref="RequestCapture"/>. It is <see langword="null"/> before that frame or when the capture failed.
    /// </summary>
    public byte[]? LastCapture { get; private set; }

    /// <summary>
    /// Asks the next presented frame to store its pixels in <see cref="LastCapture"/>.
    /// </summary>
    public void RequestCapture() => _captureRequested = true;

    /// <summary>
    /// Adds an input record to the list that drains in the next dispatch, and marks the loop active.
    /// </summary>
    /// <param name="input">The input record, with positions in view-local logical units.</param>
    public void Enqueue(in BasinViewInput input)
    {
        _input.Add(input);
        _loop.MarkActivity();
    }

    private void DrainInput()
    {
        FlushHeldKey();
        if (_input.Count == 0)
        {
            return;
        }

        var sink = InputSink;
        for (var i = 0; i < _input.Count; i++)
        {
            sink?.Invoke(_input[i]);
        }

        _input.Clear();
    }

    /// <summary>
    /// Translates the keyboard, mouse, touch and pinch events and the window events of this window. Key repeats are dropped.
    /// </summary>
    /// <param name="e">The SDL event.</param>
    /// <returns><see langword="true"/> when the window queued input or changed state.</returns>
    public bool Handle(in SDL.Event e)
    {
        var type = (SDL.EventType)e.Type;
        if (type is >= SDL.EventType.KeyboardFirst and <= SDL.EventType.MouseLast)
        {
            return HandleInput(type, in e);
        }

        if (type is >= SDL.EventType.FingerFirst and <= SDL.EventType.PinchLast)
        {
            return HandleTouch(type, in e);
        }

        if (type is < SDL.EventType.WindowFirst or > SDL.EventType.WindowLast || e.Window.WindowID != Window.Id)
        {
            return false;
        }

        var time = TimeMs(e.Window.Timestamp);
        switch (type)
        {
            case SDL.EventType.WindowMouseEnter:
                _hasPointer = true;
                _ = SDL.GetMouseState(out var enterX, out var enterY);
                var (logicalX, logicalY) = ToLogical(enterX, enterY);
                Enqueue(new BasinViewInput(BasinViewInputKind.PointerEnter, time, logicalX, logicalY, 0, false, 0, 0, 0));
                return true;
            case SDL.EventType.WindowMouseLeave:
                _hasPointer = false;
                Enqueue(new BasinViewInput(BasinViewInputKind.PointerLeave, time, 0, 0, 0, false, 0, 0, 0));
                return true;
            case SDL.EventType.WindowFocusGained:
                if (_grabKeyboard)
                {
                    _ = SDL.SetWindowKeyboardGrab(Window.Handle, true);
                }

                Enqueue(new BasinViewInput(BasinViewInputKind.FocusIn, time, 0, 0, 0, false, 0, 0, 0));
                FocusGained?.Invoke(this);
                return true;
            case SDL.EventType.WindowFocusLost:
                if (_grabKeyboard)
                {
                    _ = SDL.SetWindowKeyboardGrab(Window.Handle, false);
                }

                _modifiers.Clear();
                Enqueue(new BasinViewInput(BasinViewInputKind.FocusOut, time, 0, 0, 0, false, 0, 0, 0));
                return true;
            case SDL.EventType.WindowPixelSizeChanged:
            case SDL.EventType.WindowDisplayScaleChanged:
            case SDL.EventType.WindowResized:
                ApplySize();
                return true;
            case SDL.EventType.WindowExposed:
            case SDL.EventType.WindowShown:
                IsHidden = false;
                _forceFrame = true;
                return true;
            case SDL.EventType.WindowRestored:
            case SDL.EventType.WindowMaximized:
            case SDL.EventType.WindowEnterFullscreen:
            case SDL.EventType.WindowLeaveFullscreen:
                IsHidden = false;
                _forceFrame = true;
                StateChanged?.Invoke(this, type);
                return true;
            case SDL.EventType.WindowMinimized:
                IsHidden = true;
                StateChanged?.Invoke(this, type);
                return true;
            case SDL.EventType.WindowHidden:
            case SDL.EventType.WindowOccluded:
                IsHidden = true;
                return true;
            case SDL.EventType.WindowHDRStateChanged:
                FollowHdr();
                return true;
            case SDL.EventType.WindowDisplayChanged:
                RefreshRate = Window.RefreshRate;
                FollowRefresh();
                FollowHdr();
                DisplayChanged?.Invoke(this);
                ApplySize();
                return true;
            case SDL.EventType.WindowCloseRequested:
                CloseRequested?.Invoke(this);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Occurs when the window gains keyboard focus, after it queues the focus input.
    /// </summary>
    public event Action<SdlOutputWindow>? FocusGained;

    /// <summary>
    /// Gets a value indicating whether a pointer lock is in effect on this window, so motion reaches the client as relative input.
    /// </summary>
    public bool IsRelative { get; private set; }

    private bool _hasPointer;

    private void OnPointerLock(bool locked)
    {
        if (locked && !_hasPointer && SDL.GetMouseFocus() != Window.Handle)
        {
            return;
        }

        if (locked == IsRelative)
        {
            return;
        }

        IsRelative = locked;
        HostRelative = SDL.SetWindowRelativeMouseMode(Window.Handle, locked) && locked;
        if (locked && !HostRelative)
        {
            Log.Info($"SDL refused relative mouse mode ({SDL.GetError()}); relative motion still reaches the client");
        }
    }

    /// <summary>
    /// Gets a value indicating whether SDL accepted relative mouse mode for the pointer lock. It is <see langword="false"/> when the driver refused it.
    /// </summary>
    public bool HostRelative { get; private set; }

    private void OnPointerWarp(double layoutX, double layoutY)
    {
        var origin = View.SceneOutput.Position;
        var factor = Window.PixelDensity / View.Target.Scale;
        var x = (layoutX - origin.X) / factor;
        var y = (layoutY - origin.Y) / factor;
        var (width, height) = Window.PixelSize;
        if (x < 0 || y < 0 || x * factor * View.Target.Scale > width || y * factor * View.Target.Scale > height)
        {
            return;
        }

        SDL.WarpMouseInWindow(Window.Handle, (float)x, (float)y);
    }

    private void FollowRefresh()
    {
        if (RefreshRate > 0)
        {
            View.SetRefresh((int)Math.Round(RefreshRate * 1000.0));
        }
    }

    /// <summary>
    /// Gets a value indicating whether the window grabs the keyboard while it has focus.
    /// </summary>
    public bool HasKeyboardGrab => _grabKeyboard;

    private static uint TimeMs(ulong timestampNs) => (uint)(timestampNs / 1_000_000UL);

    /// <summary>
    /// Converts a position in window points to view-local logical units, through the pixel density and the output scale.
    /// </summary>
    /// <param name="pointX">The horizontal position in points.</param>
    /// <param name="pointY">The vertical position in points.</param>
    /// <returns>The position in logical units.</returns>
    public (double X, double Y) ToLogical(float pointX, float pointY)
    {
        var factor = Window.PixelDensity / View.Target.Scale;
        return (pointX * factor, pointY * factor);
    }

    private bool HandleInput(SDL.EventType type, in SDL.Event e)
    {
        switch (type)
        {
            case SDL.EventType.KeyDown:
            case SDL.EventType.KeyUp:
                if (e.Key.WindowID != Window.Id || e.Key.Repeat)
                {
                    return false;
                }

                Key(TimeMs(e.Key.Timestamp), SdlKeyMap.ToHostKey(e.Key.Scancode), e.Key.Down);
                return true;
            case SDL.EventType.KeymapChanged:
                _host.NotifyHostKeymapChanged();
                return true;
            case SDL.EventType.MouseMotion:
                if (e.Motion.WindowID != Window.Id)
                {
                    return false;
                }

                _hasPointer = true;
                if (IsRelative)
                {
                    var (dx, dy) = ToLogical(e.Motion.XRel, e.Motion.YRel);
                    Enqueue(new BasinViewInput(BasinViewInputKind.PointerRelative, TimeMs(e.Motion.Timestamp), dx, dy, 0, false, dx, dy, 0));
                    return true;
                }

                var (x, y) = ToLogical(e.Motion.X, e.Motion.Y);
                Enqueue(new BasinViewInput(BasinViewInputKind.PointerMotion, TimeMs(e.Motion.Timestamp), x, y, 0, false, 0, 0, 0));
                return true;
            case SDL.EventType.MouseButtonDown:
            case SDL.EventType.MouseButtonUp:
                if (e.Button.WindowID != Window.Id || SdlKeyMap.ButtonCode(e.Button.Button) is not (var button and not 0))
                {
                    return false;
                }

                var (bx, by) = ToLogical(e.Button.X, e.Button.Y);
                var buttonTime = TimeMs(e.Button.Timestamp);
                Enqueue(new BasinViewInput(BasinViewInputKind.PointerMotion, buttonTime, bx, by, 0, false, 0, 0, 0));
                Enqueue(new BasinViewInput(BasinViewInputKind.PointerButton, buttonTime, bx, by, button, e.Button.Down, 0, 0, 0));
                return true;
            case SDL.EventType.MouseWheel:
                if (e.Wheel.WindowID != Window.Id)
                {
                    return false;
                }

                var sign = e.Wheel.Direction == SDL.MouseWheelDirection.Flipped ? -1.0 : 1.0;
                Enqueue(new BasinViewInput(
                    BasinViewInputKind.PointerAxis, TimeMs(e.Wheel.Timestamp), 0, 0, 0, false,
                    -e.Wheel.X * sign, e.Wheel.Y * sign, 0));
                return true;
            default:
                return false;
        }
    }

    private bool HandleTouch(SDL.EventType type, in SDL.Event e)
    {
        if (type is >= SDL.EventType.PinchFirst and <= SDL.EventType.PinchLast)
        {
            if (e.Pinch.WindowID != Window.Id)
            {
                return false;
            }

            var kind = type switch
            {
                SDL.EventType.PinchBegin => BasinViewInputKind.GesturePinchBegin,
                SDL.EventType.PinchUpdate => BasinViewInputKind.GesturePinchUpdate,
                _ => BasinViewInputKind.GesturePinchEnd,
            };
            Enqueue(new BasinViewInput(kind, TimeMs(e.Pinch.Timestamp), 0, 0, 0, false, e.Pinch.Scale, 0, 0));
            return true;
        }

        if (e.TFinger.WindowID != Window.Id)
        {
            return false;
        }

        var time = TimeMs(e.TFinger.Timestamp);
        var id = (int)(e.TFinger.FingerID & 0x7FFFFFFF);
        var (width, height) = Window.PixelSize;
        var density = Window.PixelDensity;
        var (x, y) = ToLogical((float)(e.TFinger.X * width / density), (float)(e.TFinger.Y * height / density));
        var touch = type switch
        {
            SDL.EventType.FingerDown => BasinViewInputKind.TouchDown,
            SDL.EventType.FingerMotion => BasinViewInputKind.TouchMotion,
            SDL.EventType.FingerUp => BasinViewInputKind.TouchUp,
            _ => BasinViewInputKind.TouchCancel,
        };
        Enqueue(new BasinViewInput(touch, time, x, y, 0, false, 0, 0, id));
        return true;
    }

    private void Key(uint time, HostKeyCode code, bool pressed)
    {
        if (code == HostKeyCode.None)
        {
            return;
        }

        Span<HostKeyEvent> filtered = stackalloc HostKeyEvent[2];
        var count = _modifiers.Feed(new HostKeyEvent(time, code, pressed), filtered);
        EmitKeys(filtered[..count]);
        if (count == 0)
        {
            _loop.WakeAfter((int)HostModifierState.PhantomWindowMillis + 1);
        }
    }

    private void FlushHeldKey()
    {
        Span<HostKeyEvent> filtered = stackalloc HostKeyEvent[1];
        EmitKeys(filtered[.._modifiers.Idle((uint)SDL.GetTicks(), filtered)]);
    }

    private void EmitKeys(ReadOnlySpan<HostKeyEvent> events)
    {
        foreach (var key in events)
        {
            if (HostKeyMap.TryToEvdev(key.Code, out var evdev))
            {
                Enqueue(new BasinViewInput(BasinViewInputKind.Key, key.TimeMs, 0, 0, evdev, key.Pressed, 0, 0, 0));
            }
        }
    }

    /// <summary>
    /// Reads the pixel size and the scale of the window, resizes the lease and the output, and forces a full frame.
    /// </summary>
    public void ApplySize()
    {
        var (width, height) = Window.PixelSize;
        var scale = HostScale();
        Lease.Resize(width, height);
        if (ResizeHandler is { } handler)
        {
            handler(width, height, scale);
        }
        else
        {
            View.Reconfigure(width, height, scale, OutputTransform.Normal);
        }

        Scale = scale;
        _forceFrame = true;
        Reconfigured?.Invoke(this);
    }

    /// <summary>
    /// Gets the HDR state that the next frame of the lease presents.
    /// </summary>
    public SdlHdrState Hdr => Lease.Hdr;

    /// <summary>
    /// Occurs when the state that the lease presents changes, after <see cref="ApplyHdr"/> or after a frame finds that a swapchain rebuild chose another state.
    /// </summary>
    public event Action<SdlOutputWindow>? HdrChanged;

    /// <summary>
    /// Applies the HDR state that SDL reports for the window now.
    /// </summary>
    public void FollowHdr() => ApplyHdr(Window.ReadHdr());

    /// <summary>
    /// Asks the lease to present a state, or SDR when the lease refuses it. It then seeds the output color state with what the lease presents.
    /// </summary>
    /// <param name="state">The requested HDR state.</param>
    public void ApplyHdr(SdlHdrState state)
    {
        var before = Lease.Hdr;
        if (!Lease.TrySetHdr(state))
        {
            _ = Lease.TrySetHdr(SdlHdrState.Sdr);
        }

        var now = Lease.Hdr;
        SeedColor(now);
        if (state != before)
        {
            _forceFrame = true;
        }

        if (now != before)
        {
            _forceFrame = true;
            HdrChanged?.Invoke(this);
        }
    }

    private SdlHdrState _seeded;

    private void SeedColor(SdlHdrState state)
    {
        _seeded = state;
        if (_host.Color is not { } color)
        {
            return;
        }

        color.Configuration.Seed(View.Output, new Basin.Color.OutputColorState
        {
            HighDynamicRange = state.Enabled,
            SdrBrightnessNits = (uint)Math.Round(state.Enabled ? state.SdrWhiteNits : SdlHdrState.Sdr.SdrWhiteNits),
            BrightnessOverrides = state.Enabled
                ? new Basin.Capabilities.OutputBrightnessOverrides((int)Math.Round(state.PeakNits), -1, -1)
                : Basin.Capabilities.OutputBrightnessOverrides.None,
            Source = Basin.Capabilities.OutputColorProfileSource.Srgb,
        });
        color.Outputs?.Refresh();
    }

    private void FollowPresentedHdr()
    {
        var presented = Lease.Hdr;
        if (presented == _seeded)
        {
            return;
        }

        SeedColor(presented);
        _forceFrame = true;
        HdrChanged?.Invoke(this);
    }

    private void ShareDmabufRenderer()
    {
        if (_device.DmabufRenderer is { } renderer && _device.RenderNode is { } node)
        {
            _host.Renderer.UseDmabufRenderer(renderer, node);
        }
    }

    private bool Recover()
    {
        _host.Renderer.NotifyContextLost();
        var stale = _device.DmabufRenderer;
        Lease.Dispose();
        if (stale is not null)
        {
            _host.Renderer.StopDmabufRenderer(stale);
        }

        if (!_device.TryRecover())
        {
            return false;
        }

        Lease = _device.CreateLease(Window);
        ShareDmabufRenderer();
        FollowHdr();
        _forceFrame = true;
        return true;
    }

    private double HostScale() => OutputScaling.Snap(_scaleOverride ?? Window.DisplayScale);

    internal bool Render()
    {
        if (!Lease.Begin(out var canvas, out var context, out var age))
        {
            if (context is { IsAbandoned: true } && (!Recover() || !Lease.Begin(out canvas, out context, out age)))
            {
                return false;
            }

            if (canvas is null)
            {
                return false;
            }
        }

        var presented = false;
        try
        {
            presented = Compose(canvas, context, age);
            return presented;
        }
        finally
        {
            if (!presented)
            {
                Lease.Discard();
            }
        }
    }

    private bool Compose(SKCanvas canvas, GRContext? context, int age)
    {
        FollowPresentedHdr();
        if (_forceFrame)
        {
            View.SceneOutput.Ring.AddWhole();
            age = 0;
        }

        var target = View.Target;
        var save = canvas.Save();
        canvas.Scale((float)target.Scale);
        var composited = false;
        try
        {
            if (_host.Renderer.BindFrame(canvas, context))
            {
                try
                {
                    var options = new SceneCommitOptions { Background = _background };
                    composited = _host.Session.CommitOutput(View.SceneOutput, _host.Renderer, target, age, options);
                }
                finally
                {
                    _host.Renderer.UnbindFrame();
                }
            }
        }
        finally
        {
            canvas.RestoreToCount(save);
        }

        _forceFrame = false;
        if (!composited)
        {
            return false;
        }

        _host.NotifyComposited();
        _host.NotifyPresented(
            View.SceneOutput, _host.CapturePresented ? Capture(target.Width, target.Height, context) : null, target.Width, target.Height);

        if (_captureRequested)
        {
            _captureRequested = false;
            LastCapture = Capture(target.Width, target.Height, context);
        }

        if (_host.TakeScreenshotRequest(out var path, out var done))
        {
            done(WriteScreenshot(target.Width, target.Height, path, context));
        }

        Lease.Present();
        Presented++;
        return true;
    }

    private byte[]? Capture(int width, int height, GRContext? context)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var pixels = new byte[info.BytesSize];
        unsafe
        {
            fixed (byte* data = pixels)
            {
                if (Lease.ReadPixels(info, (nint)data, info.RowBytes) || HostedViewCapture.Render(_host, View, info, (nint)data, context))
                {
                    return pixels;
                }
            }
        }

        return null;
    }

    private bool WriteScreenshot(int width, int height, string path, GRContext? context) =>
        Capture(width, height, context) is { } pixels && HostedViewCapture.WritePng(pixels, width, height, path);

    /// <summary>
    /// Leaves the loop and the host events, and disposes the view output, the lease and the window. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loop.RemoveSink(this);
        _host.Session.BeforeDispatch -= _drain;
        _host.PointerLockRequested -= _onLock;
        _host.PointerWarpRequested -= _onWarp;
        View.Dispose();
        Lease.Dispose();
        Window.Dispose();
    }
}
