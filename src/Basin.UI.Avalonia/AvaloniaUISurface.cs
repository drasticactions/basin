using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Embedding;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Basin.Capabilities;
using Basin.Diagnostics;
using Pixman;

namespace Basin.UI.Avalonia;

/// <summary>
/// A chrome surface whose content is an Avalonia top level. Input from the compositor becomes Avalonia raw input.
/// </summary>
public sealed class AvaloniaUISurface : IUISurface, IAccessibleUISurface
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly UISurfaceObservers _observers;
    private readonly BasinTopLevelImpl _impl;
    private readonly AvaloniaUIHost? _host;
    private readonly EmbeddableControlRoot? _root;
    private readonly TouchDevice _touch = new();
    private readonly IKeyboardDevice _keyboard;
    private readonly HashSet<uint> _pressedKeys = [];
    private readonly bool _attached;
    private global::Avalonia.Point _pointer;
    private const int TouchCapacity = 10;
    private readonly int[] _touchIds = new int[TouchCapacity];
    private readonly global::Avalonia.Point[] _touchPoints = new global::Avalonia.Point[TouchCapacity];
    private readonly bool[] _touchLive = new bool[TouchCapacity];
    private AccessKitPeerTree? _accessKit;
    private bool _accessible = true;
    private RawInputModifiers _modifiers;
    private bool _keyHandled;
    private bool _pointerInside;
    private volatile bool _damagePending;
    private volatile bool _disposed;

    internal AvaloniaUISurface(BasinTopLevelImpl impl, bool ownsRoot, AvaloniaUIHost? host = null)
    {
        _impl = impl;
        _host = host;
        _attached = impl.Context.Attached;
        using (impl.Context.Affinity?.Adopt() ?? default)
        {
            _observers = new UISurfaceObservers();
        }

        _keyboard = AvaloniaLocator.Current.GetRequiredService<IKeyboardDevice>();
        impl.Surface = this;
        if (ownsRoot)
        {
            _root = new EmbeddableControlRoot(impl)
            {
                Background = null,
                TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            };
            global::Avalonia.Automation.AutomationProperties.SetControlTypeOverride(_root, global::Avalonia.Automation.Peers.AutomationControlType.Window);
            _root.Prepare();
            _root.StartRendering();
        }
    }

    private void AssertThread()
    {
        if (!_attached)
        {
            _thread.Assert();
        }
    }

    private void OnUiThread(Action action)
    {
        if (!_attached || Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the surface belongs to an attached host. Input is then posted to the Avalonia UI thread.
    /// </summary>
    public bool IsAttached => _attached;

    /// <summary>
    /// Occurs when the Avalonia window asks for an interactive move.
    /// </summary>
    public event Action? MoveDragRequested;

    /// <summary>
    /// Occurs when the Avalonia window asks for an interactive resize, with the edge to drag.
    /// </summary>
    public event Action<WindowEdge>? ResizeDragRequested;

    /// <inheritdoc/>
    public UISurfaceSize Size => _impl.Size;

    /// <summary>
    /// Gets or sets the content of the top level. It is <see langword="null"/> for a popup.
    /// </summary>
    /// <exception cref="InvalidOperationException">The set is on a popup, whose content belongs to Avalonia.</exception>
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

    /// <summary>
    /// Gets the top level that the surface owns, or <see langword="null"/> for a popup.
    /// </summary>
    public TopLevel? Root => _root;

    internal Visual? VisualRoot => _disposed ? null : _root ?? _impl.InputRoot as Visual;

    /// <summary>
    /// Gets the surface that a popup belongs to, or <see langword="null"/> for a surface that is not a popup.
    /// </summary>
    public AvaloniaUISurface? Parent => (_impl as BasinPopupImpl)?.ParentImpl.Surface;

    /// <summary>
    /// Gets the surface at the top of the parent chain. It is this surface when it has no parent.
    /// </summary>
    public AvaloniaUISurface Owner
    {
        get
        {
            var owner = this;
            while (owner.Parent is { } parent)
            {
                owner = parent;
            }

            return owner;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the focused element of the top level is a <see cref="TextBox"/>.
    /// </summary>
    public bool WantsTextInput => _root?.FocusManager?.GetFocusedElement() is TextBox;

    /// <inheritdoc/>
    public double PositionX { get; private set; }

    /// <inheritdoc/>
    public double PositionY { get; private set; }

    /// <summary>
    /// Records the layout position and gives Avalonia the matching screen position in physical pixels.
    /// </summary>
    /// <param name="x">The left edge in layout coordinates.</param>
    /// <param name="y">The top edge in layout coordinates.</param>
    public void SetPosition(double x, double y)
    {
        _thread.Assert();
        PositionX = x;
        PositionY = y;
        _impl.ScreenPosition = new PixelPoint(
            (int)Math.Round(x * _impl.RenderScaling),
            (int)Math.Round(y * _impl.RenderScaling));
        UpdateAccessibleBounds();
    }

    /// <inheritdoc/>
    public bool Configure(int logicalWidth, int logicalHeight, double scale)
    {
        _thread.Assert();
        if (_disposed || !_impl.Resize(logicalWidth, logicalHeight, scale, WindowResizeReason.Layout))
        {
            return false;
        }

        UpdateAccessibleBounds();
        return true;
    }

    /// <inheritdoc/>
    public bool TryAcquire(out UIFrame frame)
    {
        AssertThread();
        if (_disposed)
        {
            frame = default;
            return false;
        }

        return _impl.TryAcquire(out frame);
    }

    internal void Trim(long nowMillis)
    {
        if (!_disposed)
        {
            _impl.Trim(nowMillis);
        }
    }

    /// <inheritdoc/>
    public void AddObserver(IUISurfaceObserver observer) => _observers.Add(observer);

    /// <inheritdoc/>
    public void RemoveObserver(IUISurfaceObserver observer) => _observers.Remove(observer);

    /// <summary>
    /// Gets a value indicating whether the surface takes input. Avalonia turns it off for popups that are not hit-test visible.
    /// </summary>
    public bool AcceptsInput { get; private set; } = true;

    /// <inheritdoc/>
    public bool AcceptsInputAt(double x, double y)
    {
        AssertThread();
        var size = _impl.Size;
        return !_disposed && AcceptsInput && x >= 0 && y >= 0 && x < size.Width && y < size.Height;
    }

    /// <summary>
    /// Gets the cursor that Avalonia set last. The point is not used.
    /// </summary>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    /// <returns>An xcursor name, or <see langword="null"/> to let the compositor choose.</returns>
    public string? CursorAt(double x, double y) => _impl.CursorName;

    /// <inheritdoc/>
    public void NotifyPointerEnter(double x, double y)
    {
        AssertThread();
        OnUiThread(() =>
        {
            if (!Ready)
            {
                return;
            }

            _pointerInside = true;
            _pointer = new global::Avalonia.Point(x, y);
            Raise(new RawPointerEventArgs(
                _impl.MouseDevice, 0, InputRootOf(), RawPointerEventType.Move, _pointer, _modifiers));
        });
    }

    /// <inheritdoc/>
    public void NotifyPointerMotion(uint timeMs, double x, double y)
    {
        AssertThread();
        OnUiThread(() =>
        {
            if (!Ready)
            {
                return;
            }

            _pointer = new global::Avalonia.Point(x, y);
            Raise(new RawPointerEventArgs(
                _impl.MouseDevice, timeMs, InputRootOf(), RawPointerEventType.Move, _pointer, _modifiers));
        });
    }

    /// <inheritdoc/>
    public void NotifyPointerButton(uint timeMs, uint button, bool pressed)
    {
        AssertThread();
        OnUiThread(() =>
        {
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
        });
    }

    /// <inheritdoc/>
    public void NotifyPointerAxis(uint timeMs, double dx, double dy)
    {
        AssertThread();
        OnUiThread(() =>
        {
            if (!Ready || (dx == 0 && dy == 0))
            {
                return;
            }

            var delta = new Vector(-dx / EvdevInput.AxisStep, -dy / EvdevInput.AxisStep);
            Raise(new RawMouseWheelEventArgs(_impl.MouseDevice, timeMs, InputRootOf(), _pointer, delta, _modifiers));
        });
    }

    /// <inheritdoc/>
    public void NotifyPointerLeave()
    {
        AssertThread();
        OnUiThread(() =>
        {
            if (!Ready || !_pointerInside)
            {
                return;
            }

            _pointerInside = false;
            Raise(new RawPointerEventArgs(
                _impl.MouseDevice, 0, InputRootOf(), RawPointerEventType.LeaveWindow, _pointer, _modifiers));
        });
    }

    /// <summary>
    /// Tells Avalonia that a button went down outside the surface, so that light-dismiss popups close.
    /// </summary>
    public void NotifyPressOutside()
    {
        AssertThread();
        OnUiThread(() =>
        {
            if (!Ready)
            {
                return;
            }

            Raise(new RawPointerEventArgs(
                _impl.MouseDevice, 0, InputRootOf(), RawPointerEventType.NonClientLeftButtonDown, _pointer, _modifiers));
        });
    }

    /// <summary>
    /// Gives keyboard focus to the surface and activates its window. The keys that are down set the first modifier state.
    /// </summary>
    /// <param name="pressed">The evdev codes of the keys that are down at entry.</param>
    public void NotifyKeyboardEnter(ReadOnlySpan<uint> pressed)
    {
        AssertThread();
        var keys = pressed.ToArray();
        HasKeyboardFocus = true;
        OnUiThread(() =>
        {
            _pressedKeys.Clear();
            _modifiers = EvdevInput.PointerModifiers(_modifiers);
            foreach (var key in keys)
            {
                _pressedKeys.Add(key);
                _modifiers = EvdevInput.WithKey(_modifiers, key, pressed: true);
            }

            _accessKit?.SetWindowFocused(true);
            if (!_disposed && _impl is BasinWindowImpl window)
            {
                window.Activated?.Invoke();
            }
        });
    }

    /// <summary>
    /// Gets or sets the accessible name of the top level. It is <see langword="null"/> for a popup, and a set on a popup does nothing.
    /// </summary>
    public string? AccessibleName
    {
        get => _root is null ? null : global::Avalonia.Automation.AutomationProperties.GetName(_root);
        set
        {
            if (_root is not null)
            {
                global::Avalonia.Automation.AutomationProperties.SetName(_root, value);
            }
        }
    }

    /// <inheritdoc/>
    public bool Accessible
    {
        get => _accessible;
        set
        {
            AssertThread();
            if (_accessible == value)
            {
                return;
            }

            _accessible = value;
            if (!value)
            {
                DetachAccessibility();
            }
            else if (_host?.IsAccessible == true)
            {
                AttachAccessibility();
            }
        }
    }

    internal bool HasKeyboardFocus { get; private set; }

    internal static string? AccessKitUnavailable()
    {
        try
        {
            using var probe = new AccessKit.Node(AccessKit.Role.Unknown);
            return null;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException or BadImageFormatException)
        {
            return $"libaccesskit is not loadable ({error.Message})";
        }
    }

    internal void AttachAccessibility()
    {
        var focused = HasKeyboardFocus;
        OnUiThread(() =>
        {
            if (_disposed || _root is null || _accessKit is not null || !_accessible)
            {
                return;
            }

            try
            {
                _accessKit = new AccessKitPeerTree(_root);
            }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException)
            {
                BasinLog.For("avalonia").Info($"accessibility: no adapter, {error.Message}");
                return;
            }

            _accessKit.SetWindowFocused(focused);
        });
        UpdateAccessibleBounds();
    }

    internal void DetachAccessibility() => OnUiThread(() =>
    {
        _accessKit?.Dispose();
        _accessKit = null;
    });

    private void UpdateAccessibleBounds()
    {
        var size = _impl.Size;
        var bounds = new AccessKit.Rect(PositionX, PositionY, PositionX + size.Width, PositionY + size.Height);
        OnUiThread(() => _accessKit?.SetRootWindowBounds(bounds));
    }

    /// <inheritdoc/>
    public void NotifyKey(uint timeMs, uint key, bool pressed)
    {
        AssertThread();
        OnUiThread(() =>
        {
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
            var args = new RawKeyEventArgs(
                _keyboard,
                timeMs,
                InputRootOf(),
                pressed ? RawKeyEventType.KeyDown : RawKeyEventType.KeyUp,
                physical.ToQwertyKey(),
                _modifiers,
                physical,
                physical.ToQwertyKeySymbol(_modifiers.HasFlag(RawInputModifiers.Shift)));
            Raise(args);
            _keyHandled = pressed && args.Handled;
        });
    }

    /// <summary>
    /// Receives the modifier state. This implementation ignores it and tracks the modifiers from the keys.
    /// </summary>
    /// <param name="depressed">The xkb mask of the depressed modifiers.</param>
    /// <param name="latched">The xkb mask of the latched modifiers.</param>
    /// <param name="locked">The xkb mask of the locked modifiers.</param>
    /// <param name="group">The xkb layout group.</param>
    public void NotifyModifiers(uint depressed, uint latched, uint locked, uint group)
    {
    }

    /// <inheritdoc/>
    public void NotifyKeyboardLeave()
    {
        AssertThread();
        HasKeyboardFocus = false;
        OnUiThread(() =>
        {
            _pressedKeys.Clear();
            _modifiers = EvdevInput.PointerModifiers(_modifiers);
            if (!_disposed)
            {
                _impl.LostFocus?.Invoke();
                (_impl as BasinWindowImpl)?.Deactivated?.Invoke();
                _accessKit?.SetWindowFocused(false);
            }
        });
    }

    /// <summary>
    /// Tells the surface that a touch point went down on it. The surface tracks up to 10 touch points.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The touch point id. It is unique while the point is down.</param>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    public void NotifyTouchDown(uint timeMs, int id, double x, double y) =>
        RaiseTouch(timeMs, id, x, y, RawPointerEventType.TouchBegin);

    /// <inheritdoc/>
    public void NotifyTouchMotion(uint timeMs, int id, double x, double y) =>
        RaiseTouch(timeMs, id, x, y, RawPointerEventType.TouchUpdate);

    /// <inheritdoc/>
    public void NotifyTouchUp(uint timeMs, int id) =>
        RaiseTouch(timeMs, id, double.NaN, double.NaN, RawPointerEventType.TouchEnd);

    /// <inheritdoc/>
    public void NotifyTouchCancel()
    {
        AssertThread();
        OnUiThread(() =>
        {
            if (!Ready)
            {
                return;
            }

            for (var slot = 0; slot < TouchCapacity; slot++)
            {
                if (!_touchLive[slot])
                {
                    continue;
                }

                _touchLive[slot] = false;
                Raise(new RawTouchEventArgs(
                    _touch, 0, InputRootOf(), RawPointerEventType.TouchCancel, _touchPoints[slot], _modifiers, _touchIds[slot]));
            }
        });
    }

    /// <summary>
    /// Inserts committed text as Avalonia text input. The text is dropped when Avalonia handled the key press that came just before it.
    /// </summary>
    /// <param name="text">The committed text.</param>
    public void NotifyTextCommit(ReadOnlySpan<char> text)
    {
        AssertThread();
        if (text.IsEmpty)
        {
            return;
        }

        var committed = new string(text);
        OnUiThread(() =>
        {
            if (_keyHandled)
            {
                _keyHandled = false;
                return;
            }

            if (Ready)
            {
                Raise(new RawTextInputEventArgs(_keyboard, 0, InputRootOf(), committed));
            }
        });
    }

    /// <summary>
    /// Receives the preedit text. This implementation ignores it.
    /// </summary>
    /// <param name="text">The preedit text.</param>
    /// <param name="cursorBegin">The start of the cursor in the preedit.</param>
    /// <param name="cursorEnd">The end of the cursor in the preedit.</param>
    public void NotifyPreedit(ReadOnlySpan<char> text, int cursorBegin, int cursorEnd)
    {
    }

    /// <summary>
    /// Returns no popup. Avalonia opens its own popups and reports them through <see cref="AvaloniaUIHost.PopupAppeared"/>.
    /// </summary>
    /// <param name="anchor">The anchor box in surface-local logical coordinates.</param>
    /// <param name="gravity">The direction in which the popup extends from the anchor.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public IUISurface? CreatePopup(in Box anchor, UIPopupGravity gravity) => null;

    /// <summary>
    /// Stops rendering, disposes the top level and the buffers, and tells the observers. A second call does nothing.
    /// On an attached host the observers are not told.
    /// </summary>
    public void Dispose()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host?.Forget(this);
        _accessKit?.Dispose();
        _accessKit = null;
        _root?.StopRendering();
        _touch.Dispose();
        _impl.Surface = null;
        _root?.Dispose();
        if (!_impl.IsDisposed)
        {
            _impl.Dispose();
        }

        if (!_attached)
        {
            _observers.Destroyed(this);
        }
    }

    internal void SetHitTestVisible(bool value) => AcceptsInput = value;

    internal void SetPositionFromToolkit(double x, double y)
    {
        PositionX = x;
        PositionY = y;
    }

    internal void NotifyFramePublished()
    {
        if (_attached)
        {
            _damagePending = true;
            _host?.RaiseSurfaceDamaged(this);
            return;
        }

        _observers.Damaged(this, _impl.WholeDamage);
    }

    /// <summary>
    /// Reports a frame that an attached surface finished to the observers. Call it on the compositor thread after <see cref="AvaloniaUIHost.SurfaceDamaged"/>.
    /// </summary>
    /// <returns><see langword="true"/> when a frame was pending and the observers were told.</returns>
    public bool PublishDamage()
    {
        if (_disposed || !_damagePending)
        {
            return false;
        }

        _damagePending = false;
        _observers.Damaged(this, _impl.WholeDamage);
        return true;
    }

    internal void RequestMoveDrag() => MoveDragRequested?.Invoke();

    internal void RequestResizeDrag(WindowEdge edge) => ResizeDragRequested?.Invoke(edge);

    private void RaiseTouch(uint timeMs, int id, double x, double y, RawPointerEventType type)
    {
        AssertThread();
        OnUiThread(() =>
        {
            if (!Ready)
            {
                return;
            }

            var slot = TouchSlot(id, type == RawPointerEventType.TouchBegin);
            if (slot >= 0 && !double.IsNaN(x))
            {
                _touchPoints[slot] = new global::Avalonia.Point(x, y);
            }

            var point = slot >= 0 ? _touchPoints[slot]
                : double.IsNaN(x) ? default : new global::Avalonia.Point(x, y);
            if (slot >= 0 && type == RawPointerEventType.TouchEnd)
            {
                _touchLive[slot] = false;
            }

            if (type == RawPointerEventType.TouchBegin && _root is { } root)
            {
                _impl.Context.Host?.TextFields.Classify(root, point);
            }

            Raise(new RawTouchEventArgs(_touch, timeMs, InputRootOf(), type, point, _modifiers, id));
        });
    }

    private int TouchSlot(int id, bool begin)
    {
        var free = -1;
        for (var slot = 0; slot < TouchCapacity; slot++)
        {
            if (_touchLive[slot] && _touchIds[slot] == id)
            {
                return slot;
            }

            if (free < 0 && !_touchLive[slot])
            {
                free = slot;
            }
        }

        if (!begin || free < 0)
        {
            return -1;
        }

        _touchIds[free] = id;
        _touchLive[free] = true;
        return free;
    }

    private bool Ready => !_disposed && _impl.InputRoot is not null && _impl.Input is not null;

    private IInputRoot InputRootOf() => _impl.InputRoot!;

    private void Raise(RawInputEventArgs args) => _impl.Input?.Invoke(args);
}
