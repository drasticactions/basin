using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Basin.Hosted;

namespace Basin.Avalonia;

/// <summary>
/// An Avalonia control that shows one view output of a shared <see cref="BasinCompositorHost"/> and forwards host input to it.
/// The compositor work runs on the Avalonia composition thread.
/// </summary>
public sealed class BasinToplevelView : Control, ICaptureTarget
{
    private readonly BasinCompositorHost _host;
    private readonly Func<BasinCompositorHost, BasinViewOutput> _createView;
    private readonly BasinInputChannel _input = new();
    private readonly HashSet<uint> _pressedKeys = [];
    private readonly HashSet<uint> _pressedButtons = [];
    private CompositionCustomVisual? _visual;
    private BasinViewVisual? _handler;
    private Action<BasinViewInput>? _sink;
    private bool _handlersAttached;

    /// <summary>
    /// Initializes a new instance of the <see cref="BasinToplevelView"/> class. The control is focusable.
    /// </summary>
    /// <param name="host">The compositor host that the view output belongs to.</param>
    /// <param name="createView">Makes the view output on the composition thread, on the first frame or wake.</param>
    public BasinToplevelView(BasinCompositorHost host, Func<BasinCompositorHost, BasinViewOutput> createView)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(createView);
        _host = host;
        _createView = createView;
        Focusable = true;
    }

    /// <summary>
    /// Gets or sets the receiver of the input that this control collects.
    /// Setting a non-null value attaches the pointer and key handlers once. Input is delivered on the composition thread before each dispatch.
    /// </summary>
    public Action<BasinViewInput>? InputSink
    {
        get => _sink;
        set
        {
            _sink = value;
            if (value is not null)
            {
                AttachHandlers();
                InvalidateVisual();
            }
        }
    }

    /// <inheritdoc/>
    public Func<uint, bool, bool>? KeyFilter { get; set; }

    /// <summary>
    /// Gets a value indicating whether the last <see cref="CaptureInput"/> call asked for capture.
    /// </summary>
    public bool InputCaptured { get; private set; }

    /// <summary>
    /// Occurs on the composition thread after <see cref="CaptureInput"/> has applied the pointer constraint. The argument is the new capture state.
    /// </summary>
    public event Action<bool>? CaptureChanged;

    /// <summary>
    /// Gets the view output. It is <see langword="null"/> before the first frame or wake and after shutdown.
    /// </summary>
    public BasinViewOutput? Output => _handler?.View;

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var element = ElementComposition.GetElementVisual(this);
        if (element is null)
        {
            return;
        }

        if (_visual is null)
        {
            _handler = new BasinViewVisual(_host, _createView, DrainInput);
            _visual = element.Compositor.CreateCustomVisual(_handler);
        }

        ElementComposition.SetElementChildVisual(this, _visual);
        _visual.Size = new(Bounds.Width, Bounds.Height);
    }

    /// <inheritdoc/>
    protected override void ArrangeCore(Rect finalRect)
    {
        base.ArrangeCore(finalRect);
        if (_visual is not null)
        {
            _visual.Size = new(Bounds.Width, Bounds.Height);
        }
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_sink is not null)
        {
            context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        }
    }

    /// <summary>
    /// Runs an action on the composition thread, then dispatches and flushes the compositor. It does nothing before the control has a visual.
    /// </summary>
    /// <param name="action">The work to run with the compositor's thread affinity.</param>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_visual is { } visual)
        {
            Dispatcher.UIThread.Post(() => visual.SendHandlerMessage(action));
        }
    }

    /// <summary>
    /// Asks the composition thread to dispatch the compositor and invalidate the views that are dirty.
    /// </summary>
    public void RequestFrame()
    {
        if (_visual is { } visual)
        {
            Dispatcher.UIThread.Post(() => visual.SendHandlerMessage(BasinViewVisual.WakeMessage));
        }
    }

    /// <summary>
    /// Asks the composition thread to render this view on the next composition frame.
    /// </summary>
    public void RequestRender()
    {
        if (_visual is { } visual)
        {
            Dispatcher.UIThread.Post(() => visual.SendHandlerMessage(BasinViewVisual.RenderMessage));
        }
    }

    /// <summary>
    /// Asks the composition thread to dispose the view output and stop all work of the handler.
    /// </summary>
    /// <returns>A task that completes when the handler is down. It is already complete when the control never got a visual.</returns>
    public Task ShutdownAsync()
    {
        if (_visual is not { } visual)
        {
            return Task.CompletedTask;
        }

        var message = new BasinShutdownMessage();
        Dispatcher.UIThread.Post(() => visual.SendHandlerMessage(message));
        return message.Completed;
    }

    /// <summary>
    /// Sends a focus change to the input sink. On deactivation it first releases every key that is still pressed.
    /// </summary>
    /// <param name="active"><see langword="true"/> when the host window became active.</param>
    public void NotifyActivated(bool active)
    {
        if (!active)
        {
            ReleasePressedKeys();
        }

        Send(active ? BasinViewInputKind.FocusIn : BasinViewInputKind.FocusOut);
    }

    /// <inheritdoc/>
    public void InjectKey(uint code, bool pressed)
    {
        if (code == 0)
        {
            return;
        }

        if (pressed ? !_pressedKeys.Add(code) : !_pressedKeys.Remove(code))
        {
            return;
        }

        Send(BasinViewInputKind.Key, code: code, pressed: pressed);
    }

    /// <summary>
    /// Records the capture state, then activates or deactivates the pointer constraint of the focused surface on the composition thread.
    /// It raises <see cref="CaptureChanged"/> after that.
    /// </summary>
    /// <param name="captured"><see langword="true"/> to lock or confine the pointer, <see langword="false"/> to release it.</param>
    public void CaptureInput(bool captured)
    {
        InputCaptured = captured;
        Post(() =>
        {
            var constraints = _host.Services.Find<Basin.Desktop.PointerConstraintsManager>();
            if (_host.Seat.Pointer.Focus is { } focus && constraints?.ConstraintFor(focus) is { } constraint)
            {
                if (captured)
                {
                    constraint.Activate();
                }
                else
                {
                    constraint.Deactivate();
                }
            }

            CaptureChanged?.Invoke(captured);
        });
    }

    private void DrainInput()
    {
        while (_input.TryRead(out var input))
        {
            _sink?.Invoke(new BasinViewInput(
                (BasinViewInputKind)input.Kind,
                input.TimeMs,
                input.X,
                input.Y,
                input.Code,
                input.Pressed,
                input.DeltaX,
                input.DeltaY,
                input.TouchId));
        }
    }

    private static uint Now => (uint)Environment.TickCount;

    private void Send(
        BasinViewInputKind kind,
        PointerEventArgs? pointer = null,
        uint code = 0,
        bool pressed = false,
        double dx = 0,
        double dy = 0,
        int touchId = 0)
    {
        var position = pointer?.GetPosition(this) ?? default;
        _input.Write(new BasinInputEvent
        {
            Kind = (InputKind)kind,
            WindowId = 0,
            TimeMs = Now,
            X = position.X,
            Y = position.Y,
            Code = code,
            Pressed = pressed,
            DeltaX = dx,
            DeltaY = dy,
            TouchId = pointer?.Pointer.Id ?? touchId,
        });
        _visual?.SendHandlerMessage(BasinViewVisual.WakeMessage);
    }

    private void AttachHandlers()
    {
        if (_handlersAttached)
        {
            return;
        }

        _handlersAttached = true;
        PointerEntered += OnPointerEntered;
        PointerMoved += OnPointerMoved;
        PointerExited += OnPointerExited;
        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheel;
        PointerCaptureLost += OnPointerCaptureLost;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Touch)
        {
            Send(BasinViewInputKind.PointerEnter, e);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e) =>
        Send(e.Pointer.Type == PointerType.Touch ? BasinViewInputKind.TouchMotion : BasinViewInputKind.PointerMotion, e);

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Touch)
        {
            Send(BasinViewInputKind.PointerLeave);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Touch)
        {
            Send(BasinViewInputKind.TouchDown, e);
            e.Handled = true;
            return;
        }

        Focus();
        ReleaseStaleModifiers(e.KeyModifiers);
        Send(BasinViewInputKind.PointerMotion, e);
        var button = AvaloniaKeyMap.ButtonFor(e.GetCurrentPoint(this).Properties.PointerUpdateKind);
        if (button != 0 && _pressedButtons.Add(button))
        {
            Send(BasinViewInputKind.PointerButton, e, button, pressed: true);
        }

        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Touch)
        {
            Send(BasinViewInputKind.TouchUp, e);
            e.Handled = true;
            return;
        }

        var button = AvaloniaKeyMap.ButtonFor(e.GetCurrentPoint(this).Properties.PointerUpdateKind);
        if (button != 0 && _pressedButtons.Remove(button))
        {
            Send(BasinViewInputKind.PointerButton, e, button, pressed: false);
        }

        e.Handled = true;
    }

    private void OnPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        Send(BasinViewInputKind.PointerAxis, e, dx: e.Delta.X, dy: e.Delta.Y);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Touch)
        {
            Send(BasinViewInputKind.TouchCancel, touchId: e.Pointer.Id);
        }
    }

    private void ReleaseStaleModifiers(KeyModifiers held)
    {
        if (AvaloniaKeyMap.StaleModifiers(held, _pressedKeys) is not { } stale)
        {
            return;
        }

        foreach (var code in stale)
        {
            _pressedKeys.Remove(code);
            Send(BasinViewInputKind.Key, code: code, pressed: false);
        }
    }

    private void ReleasePressedKeys()
    {
        foreach (var code in _pressedKeys)
        {
            Send(BasinViewInputKind.Key, code: code, pressed: false);
        }

        _pressedKeys.Clear();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        ReleaseStaleModifiers(e.KeyModifiers);
        if (e.Key == Key.ImeProcessed)
        {
            return;
        }

        var code = CodeFor(e);
        if (code == 0)
        {
            return;
        }

        if (KeyFilter is { } filter && filter(code, true))
        {
            e.Handled = true;
            return;
        }

        if (_pressedKeys.Add(code))
        {
            Send(BasinViewInputKind.Key, code: code, pressed: true);
            e.Handled = true;
        }
    }

    private static uint CodeFor(KeyEventArgs e)
    {
        var code = AvaloniaKeyMap.EvdevFor(e.PhysicalKey);
        return code != 0 ? code : AvaloniaKeyMap.EvdevFor(e.Key);
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.ImeProcessed)
        {
            return;
        }

        var code = CodeFor(e);
        if (code == 0)
        {
            return;
        }

        if (KeyFilter is { } filter && filter(code, false))
        {
            e.Handled = true;
            return;
        }

        if (_pressedKeys.Remove(code))
        {
            Send(BasinViewInputKind.Key, code: code, pressed: false);
            e.Handled = true;
        }

        ReleaseStaleModifiers(e.KeyModifiers);
    }
}
