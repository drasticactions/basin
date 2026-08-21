using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Embedding;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Basin.Capabilities;
using Basin.Diagnostics;
using Pixman;

namespace Basin.UI.Avalonia;

public sealed class AvaloniaUISurface : IUISurface
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly UISurfaceObservers _observers = new();
    private readonly BasinTopLevelImpl _impl;
    private readonly AvaloniaUIHost? _host;
    private readonly EmbeddableControlRoot? _root;
    private readonly TouchDevice _touch = new();
    private readonly IKeyboardDevice _keyboard;
    private readonly HashSet<uint> _pressedKeys = [];
    private global::Avalonia.Point _pointer;
    private RawInputModifiers _modifiers;
    private bool _pointerInside;
    private bool _disposed;

    internal AvaloniaUISurface(BasinTopLevelImpl impl, bool ownsRoot, AvaloniaUIHost? host = null)
    {
        _impl = impl;
        _host = host;
        _keyboard = AvaloniaLocator.Current.GetRequiredService<IKeyboardDevice>();
        impl.Surface = this;
        if (ownsRoot)
        {
            _root = new EmbeddableControlRoot(impl)
            {
                Background = null,
                TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            };
            _root.Prepare();
            _root.StartRendering();
        }
    }

    public event Action? MoveDragRequested;

    public event Action<WindowEdge>? ResizeDragRequested;

    public UISurfaceSize Size => _impl.Framebuffer.Size;

    public object? Content
    {
        get => _root?.Content;
        set
        {
            if (_root is null)
            {
                throw new InvalidOperationException("This surface's content belongs to Avalonia, not to the compositor.");
            }

            _root.Content = value;
        }
    }

    public TopLevel? Root => _root;

    public bool WantsTextInput => _root?.FocusManager?.GetFocusedElement() is TextBox;

    public double PositionX { get; private set; }

    public double PositionY { get; private set; }

    public void SetPosition(double x, double y)
    {
        _thread.Assert();
        PositionX = x;
        PositionY = y;
        _impl.ScreenPosition = new PixelPoint(
            (int)Math.Round(x * _impl.RenderScaling),
            (int)Math.Round(y * _impl.RenderScaling));
    }

    public bool Configure(int logicalWidth, int logicalHeight, double scale)
    {
        _thread.Assert();
        return !_disposed && _impl.Resize(logicalWidth, logicalHeight, scale, WindowResizeReason.Layout);
    }

    public bool TryAcquire(out UIFrame frame)
    {
        _thread.Assert();
        if (_disposed)
        {
            frame = default;
            return false;
        }

        return _impl.Framebuffer.TryAcquire(out frame);
    }

    public void AddObserver(IUISurfaceObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IUISurfaceObserver observer) => _observers.Remove(observer);

    public bool AcceptsInputAt(double x, double y)
    {
        _thread.Assert();
        var size = _impl.Framebuffer.Size;
        return !_disposed && x >= 0 && y >= 0 && x < size.Width && y < size.Height;
    }

    public string? CursorAt(double x, double y) => _impl.CursorName;

    public void NotifyPointerEnter(double x, double y)
    {
        _thread.Assert();
        if (!Ready)
        {
            return;
        }

        _pointerInside = true;
        _pointer = new global::Avalonia.Point(x, y);
        Raise(new RawPointerEventArgs(
            _impl.MouseDevice, 0, InputRootOf(), RawPointerEventType.Move, _pointer, _modifiers));
    }

    public void NotifyPointerMotion(uint timeMs, double x, double y)
    {
        _thread.Assert();
        if (!Ready)
        {
            return;
        }

        _pointer = new global::Avalonia.Point(x, y);
        Raise(new RawPointerEventArgs(
            _impl.MouseDevice, timeMs, InputRootOf(), RawPointerEventType.Move, _pointer, _modifiers));
    }

    public void NotifyPointerButton(uint timeMs, uint button, bool pressed)
    {
        _thread.Assert();
        if (!Ready)
        {
            return;
        }

        var type = EvdevInput.PointerEventType(button, pressed);
        if (type is not { } value)
        {
            return;
        }

        _modifiers = EvdevInput.WithButton(_modifiers, button, pressed);
        Raise(new RawPointerEventArgs(_impl.MouseDevice, timeMs, InputRootOf(), value, _pointer, _modifiers));
    }

    public void NotifyPointerAxis(uint timeMs, double dx, double dy)
    {
        _thread.Assert();
        if (!Ready || (dx == 0 && dy == 0))
        {
            return;
        }

        var delta = new Vector(-dx / EvdevInput.AxisStep, -dy / EvdevInput.AxisStep);
        Raise(new RawMouseWheelEventArgs(_impl.MouseDevice, timeMs, InputRootOf(), _pointer, delta, _modifiers));
    }

    public void NotifyPointerLeave()
    {
        _thread.Assert();
        if (!Ready || !_pointerInside)
        {
            return;
        }

        _pointerInside = false;
        Raise(new RawPointerEventArgs(
            _impl.MouseDevice, 0, InputRootOf(), RawPointerEventType.LeaveWindow, _pointer, _modifiers));
    }

    public void NotifyKeyboardEnter(ReadOnlySpan<uint> pressed)
    {
        _thread.Assert();
        _pressedKeys.Clear();
        _modifiers = EvdevInput.PointerModifiers(_modifiers);
        foreach (var key in pressed)
        {
            _pressedKeys.Add(key);
            _modifiers = EvdevInput.WithKey(_modifiers, key, pressed: true);
        }
    }

    public void NotifyKey(uint timeMs, uint key, bool pressed)
    {
        _thread.Assert();
        if (!Ready)
        {
            return;
        }

        if (pressed)
        {
            _pressedKeys.Add(key);
        }
        else
        {
            _pressedKeys.Remove(key);
        }

        _modifiers = EvdevInput.WithKey(_modifiers, key, pressed);
        var physical = EvdevInput.PhysicalKeyOf(key);
        Raise(new RawKeyEventArgs(
            _keyboard,
            timeMs,
            InputRootOf(),
            pressed ? RawKeyEventType.KeyDown : RawKeyEventType.KeyUp,
            physical.ToQwertyKey(),
            _modifiers,
            physical,
            physical.ToQwertyKeySymbol(_modifiers.HasFlag(RawInputModifiers.Shift))));
    }

    public void NotifyModifiers(uint depressed, uint latched, uint locked, uint group)
    {
    }

    public void NotifyKeyboardLeave()
    {
        _thread.Assert();
        _pressedKeys.Clear();
        _modifiers = EvdevInput.PointerModifiers(_modifiers);
        if (!_disposed)
        {
            _impl.LostFocus?.Invoke();
        }
    }

    public void NotifyTouchDown(uint timeMs, int id, double x, double y) =>
        RaiseTouch(timeMs, id, x, y, RawPointerEventType.TouchBegin);

    public void NotifyTouchMotion(uint timeMs, int id, double x, double y) =>
        RaiseTouch(timeMs, id, x, y, RawPointerEventType.TouchUpdate);

    public void NotifyTouchUp(uint timeMs, int id) =>
        RaiseTouch(timeMs, id, _pointer.X, _pointer.Y, RawPointerEventType.TouchEnd);

    public void NotifyTouchCancel()
    {
        _thread.Assert();
        if (!Ready)
        {
            return;
        }

        Raise(new RawTouchEventArgs(
            _touch, 0, InputRootOf(), RawPointerEventType.TouchCancel, _pointer, _modifiers, 0));
    }

    public void NotifyTextCommit(ReadOnlySpan<char> text)
    {
        _thread.Assert();
        if (!Ready || text.IsEmpty)
        {
            return;
        }

        Raise(new RawTextInputEventArgs(_keyboard, 0, InputRootOf(), new string(text)));
    }

    public void NotifyPreedit(ReadOnlySpan<char> text, int cursorBegin, int cursorEnd)
    {
    }

    public IUISurface? CreatePopup(in Box anchor, UIPopupGravity gravity) => null;

    public void Dispose()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host?.Forget(this);
        _root?.StopRendering();
        _touch.Dispose();
        _impl.Surface = null;
        _root?.Dispose();
        if (!_impl.IsDisposed)
        {
            _impl.Dispose();
        }

        _observers.Destroyed(this);
    }

    internal void SetPositionFromToolkit(double x, double y)
    {
        PositionX = x;
        PositionY = y;
    }

    internal void NotifyFramePublished() => _observers.Damaged(this, _impl.Framebuffer.WholeDamage);

    internal void RequestMoveDrag() => MoveDragRequested?.Invoke();

    internal void RequestResizeDrag(WindowEdge edge) => ResizeDragRequested?.Invoke(edge);

    private void RaiseTouch(uint timeMs, int id, double x, double y, RawPointerEventType type)
    {
        _thread.Assert();
        if (!Ready)
        {
            return;
        }

        _pointer = new global::Avalonia.Point(x, y);
        Raise(new RawTouchEventArgs(_touch, timeMs, InputRootOf(), type, _pointer, _modifiers, id));
    }

    private bool Ready => !_disposed && _impl.InputRoot is not null && _impl.Input is not null;

    private IInputRoot InputRootOf() => _impl.InputRoot!;

    private void Raise(RawInputEventArgs args) => _impl.Input?.Invoke(args);
}
