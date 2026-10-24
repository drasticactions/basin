using Basin.Capabilities;
using Pixman;

namespace Basin.Scene;

/// <summary>
/// Routes pointer, touch, keyboard and text input from scene coordinates to the UI surfaces in a <see cref="UISurfaceIndex"/>.
/// It tracks the hovered surface, the keyboard focus, an optional implicit pointer grab and up to ten touch points.
/// </summary>
public sealed class UISurfaceRouter : IUISurfaceObserver
{
    private const int TouchCapacity = 10;

    private readonly Scene _scene;
    private readonly UISurfaceIndex _index;
    private readonly IUISurface?[] _touchSurfaces = new IUISurface?[TouchCapacity];
    private readonly int[] _touchIds = new int[TouchCapacity];
    private IUISurface? _hovered;
    private IUISurface? _focus;
    private IUISurface? _grab;
    private int _grabButtons;

    /// <summary>
    /// Initializes a new instance of the <see cref="UISurfaceRouter"/> class.
    /// </summary>
    /// <param name="scene">The scene that hit tests run against.</param>
    /// <param name="index">The index that maps scene nodes to UI surfaces.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> or <paramref name="index"/> is <see langword="null"/>.</exception>
    public UISurfaceRouter(Scene scene, UISurfaceIndex index)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(index);

        _scene = scene;
        _index = index;
    }

    /// <summary>
    /// Gets the surface under the pointer, or <see langword="null"/> when the pointer is over no UI surface.
    /// </summary>
    public IUISurface? Hovered => _hovered;

    /// <summary>
    /// Gets the surface that receives key and text input, or <see langword="null"/> for none.
    /// </summary>
    public IUISurface? KeyboardFocus => _focus;

    /// <summary>
    /// Gets the surface that holds the implicit pointer grab, or <see langword="null"/> when no button is held.
    /// </summary>
    public IUISurface? PointerGrab => _grab;

    /// <summary>
    /// Gets a value indicating whether a button press grabs the pointer for the pressed surface until the last button is released.
    /// </summary>
    public bool ImplicitGrab { get; init; }

    /// <summary>
    /// Gets a value indicating whether the focused surface asks for text input.
    /// </summary>
    public bool WantsTextInput => _focus?.WantsTextInput ?? false;

    /// <summary>
    /// Occurs when the hovered surface changes. The argument is the new surface, or <see langword="null"/> when the pointer left all UI surfaces.
    /// </summary>
    public event Action<IUISurface?>? HoverChanged;

    /// <summary>
    /// Occurs when the keyboard focus changes. The argument is the new surface, or <see langword="null"/> when no surface has focus.
    /// </summary>
    public event Action<IUISurface?>? FocusChanged;

    /// <summary>
    /// Finds the UI surface at the top of the scene at a point.
    /// </summary>
    /// <param name="x">The horizontal position in scene coordinates.</param>
    /// <param name="y">The vertical position in scene coordinates.</param>
    /// <returns>The hit with surface coordinates, or <see langword="null"/> when the top node is not a UI surface.</returns>
    public UISurfaceHit? SurfaceAt(double x, double y)
    {
        if (_scene.NodeAt(x, y) is not { } hit ||
            hit.Node is not SceneBuffer node ||
            _index.SurfaceOf(node) is not { } surface)
        {
            return null;
        }

        var scale = _index.NodeOf(node)?.InputScale ?? 1.0;
        return new UISurfaceHit(surface, node, hit.X * scale, hit.Y * scale);
    }

    /// <summary>
    /// Gets the cursor name that the UI surface at a point asks for.
    /// </summary>
    /// <param name="x">The horizontal position in scene coordinates.</param>
    /// <param name="y">The vertical position in scene coordinates.</param>
    /// <returns>The cursor name, or <see langword="null"/> when no UI surface is there or it has no preference.</returns>
    public string? CursorAt(double x, double y) =>
        SurfaceAt(x, y) is { } hit ? hit.Surface.CursorAt(hit.X, hit.Y) : null;

    /// <summary>
    /// Converts a scene point to the coordinates of a surface, also when the point is outside the surface.
    /// </summary>
    /// <param name="surface">The surface to convert into.</param>
    /// <param name="x">The horizontal position in scene coordinates.</param>
    /// <param name="y">The vertical position in scene coordinates.</param>
    /// <param name="localX">The horizontal position in surface coordinates. On failure it holds <paramref name="x"/> or a partial result.</param>
    /// <param name="localY">The vertical position in surface coordinates. On failure it holds <paramref name="y"/> or a partial result.</param>
    /// <returns><see langword="false"/> when the surface is not in the index or its node cannot map the point.</returns>
    public bool TryLocal(IUISurface surface, double x, double y, out double localX, out double localY)
    {
        localX = x;
        localY = y;
        if (surface is null || _index.OwnerOf(surface) is not { } owner)
        {
            return false;
        }

        if (!owner.Node.TryMapSceneToLocal(x, y, out localX, out localY))
        {
            return false;
        }

        localX *= owner.InputScale;
        localY *= owner.InputScale;
        return true;
    }

    /// <summary>
    /// Delivers pointer motion. A grab gets the motion wherever the pointer is. Otherwise the surface under the pointer gets an enter or a motion, and the previous surface gets a leave.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="x">The horizontal position in scene coordinates.</param>
    /// <param name="y">The vertical position in scene coordinates.</param>
    /// <returns>The surface that took the motion, or the default value when none did. A grab that can no longer map the point ends.</returns>
    public UIPointerRoute PointerMotion(uint timeMs, double x, double y)
    {
        if (_grab is { } grabbed)
        {
            if (!TryLocal(grabbed, x, y, out var localX, out var localY))
            {
                EndGrab();
                return default;
            }

            grabbed.NotifyPointerMotion(timeMs, localX, localY);
            return new UIPointerRoute(grabbed, false, grabbed.CursorAt(localX, localY));
        }

        if (SurfaceAt(x, y) is not { } hit)
        {
            PointerLeave();
            return default;
        }

        var entered = !ReferenceEquals(hit.Surface, _hovered);
        if (entered)
        {
            LeaveHovered();
            _hovered = hit.Surface;
            _hovered.AddObserver(this);
            _hovered.NotifyPointerEnter(hit.X, hit.Y);
            HoverChanged?.Invoke(_hovered);
        }
        else
        {
            hit.Surface.NotifyPointerMotion(timeMs, hit.X, hit.Y);
        }

        return new UIPointerRoute(hit.Surface, entered, hit.Surface.CursorAt(hit.X, hit.Y));
    }

    /// <summary>
    /// Sends a leave to the hovered surface and raises <see cref="HoverChanged"/> with <see langword="null"/>. It does nothing when no surface is hovered.
    /// </summary>
    public void PointerLeave()
    {
        if (_hovered is null)
        {
            return;
        }

        LeaveHovered();
        HoverChanged?.Invoke(null);
    }

    /// <summary>
    /// Delivers a button event to <paramref name="target"/>, or else to the grab, or else to the hovered surface.
    /// When <see cref="ImplicitGrab"/> is set and no target is given, presses start a grab and the last release ends it.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="button">The Linux input button code.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    /// <param name="target">The surface to send to, or <see langword="null"/> to route by grab and hover.</param>
    /// <returns><see langword="true"/> when a surface received the event.</returns>
    public bool PointerButton(uint timeMs, uint button, bool pressed, IUISurface? target = null)
    {
        var surface = target ?? _grab ?? _hovered;
        if (surface is null)
        {
            return false;
        }

        if (target is null && ImplicitGrab)
        {
            if (pressed)
            {
                if (_grabButtons++ == 0)
                {
                    _grab = surface;
                    _grab.AddObserver(this);
                }
            }
            else if (_grabButtons > 0 && --_grabButtons == 0)
            {
                surface.NotifyPointerButton(timeMs, button, pressed);
                EndGrab();
                return true;
            }
        }

        surface.NotifyPointerButton(timeMs, button, pressed);
        return true;
    }

    /// <summary>
    /// Delivers a scroll to <paramref name="target"/>, or else to the grab, or else to the hovered surface.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="dx">The horizontal scroll distance.</param>
    /// <param name="dy">The vertical scroll distance.</param>
    /// <param name="target">The surface to send to, or <see langword="null"/> to route by grab and hover.</param>
    /// <returns><see langword="true"/> when a surface received the scroll.</returns>
    public bool PointerAxis(uint timeMs, double dx, double dy, IUISurface? target = null)
    {
        var surface = target ?? _grab ?? _hovered;
        if (surface is null)
        {
            return false;
        }

        surface.NotifyPointerAxis(timeMs, dx, dy);
        return true;
    }

    /// <summary>
    /// Delivers one seat axis event as a scroll on the matching axis.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="axis">The axis and its value.</param>
    /// <param name="target">The surface to send to, or <see langword="null"/> to route by grab and hover.</param>
    /// <returns><see langword="true"/> when a surface received the scroll.</returns>
    public bool PointerAxis(uint timeMs, in PointerAxis axis, IUISurface? target = null)
    {
        var horizontal = axis.Axis == Wayland.WlPointer.Axis.HorizontalScroll;
        return PointerAxis(timeMs, horizontal ? axis.Value : 0, horizontal ? 0 : axis.Value, target);
    }

    /// <summary>
    /// Latches a touch point to the UI surface under it and sends the down event. Later motion and up events for the same id go to that surface.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The touch point id.</param>
    /// <param name="x">The horizontal position in scene coordinates.</param>
    /// <param name="y">The vertical position in scene coordinates.</param>
    /// <returns><see langword="false"/> when no UI surface is under the point or ten touch points are already latched.</returns>
    public bool TouchDown(uint timeMs, int id, double x, double y)
    {
        if (SurfaceAt(x, y) is not { } hit)
        {
            return false;
        }

        var slot = TouchSlotOf(id);
        if (slot < 0)
        {
            slot = FreeTouchSlot();
            if (slot < 0)
            {
                return false;
            }
        }

        _touchIds[slot] = id;
        _touchSurfaces[slot] = hit.Surface;
        hit.Surface.NotifyTouchDown(timeMs, id, hit.X, hit.Y);
        return true;
    }

    /// <summary>
    /// Sends touch motion to the surface that the touch point is latched to. A point that the surface can no longer map is unlatched with no event.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The touch point id.</param>
    /// <param name="x">The horizontal position in scene coordinates.</param>
    /// <param name="y">The vertical position in scene coordinates.</param>
    /// <returns><see langword="true"/> when the id was latched to a surface.</returns>
    public bool TouchMotion(uint timeMs, int id, double x, double y)
    {
        var slot = TouchSlotOf(id);
        if (slot < 0 || _touchSurfaces[slot] is not { } surface)
        {
            return false;
        }

        if (!TryLocal(surface, x, y, out var localX, out var localY))
        {
            _touchSurfaces[slot] = null;
            return true;
        }

        surface.NotifyTouchMotion(timeMs, id, localX, localY);
        return true;
    }

    /// <summary>
    /// Unlatches a touch point and sends the up event when its surface is still in the index.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The touch point id.</param>
    /// <returns><see langword="true"/> when the id was latched to a surface.</returns>
    public bool TouchUp(uint timeMs, int id)
    {
        var slot = TouchSlotOf(id);
        if (slot < 0 || _touchSurfaces[slot] is not { } surface)
        {
            return false;
        }

        _touchSurfaces[slot] = null;
        if (_index.OwnerOf(surface) is not null)
        {
            surface.NotifyTouchUp(timeMs, id);
        }

        return true;
    }

    /// <summary>
    /// Unlatches every touch point and sends one cancel to each surface that is still in the index.
    /// </summary>
    /// <returns><see langword="true"/> when at least one touch point was latched.</returns>
    public bool TouchCancel()
    {
        var any = false;
        for (var i = 0; i < TouchCapacity; i++)
        {
            if (_touchSurfaces[i] is not { } surface)
            {
                continue;
            }

            any = true;
            _touchSurfaces[i] = null;
            if (!TouchLatched(surface) && _index.OwnerOf(surface) is not null)
            {
                surface.NotifyTouchCancel();
            }
        }

        return any;
    }

    /// <summary>
    /// Moves the keyboard focus. The old surface gets a leave, the new surface gets an enter, and <see cref="FocusChanged"/> occurs. It does nothing when the focus does not change.
    /// </summary>
    /// <param name="surface">The surface to focus, or <see langword="null"/> to clear the focus.</param>
    /// <param name="pressed">The Linux key codes that are held at the time of the enter.</param>
    public void SetKeyboardFocus(IUISurface? surface, ReadOnlySpan<uint> pressed = default)
    {
        if (ReferenceEquals(surface, _focus))
        {
            return;
        }

        if (_focus is { } previous)
        {
            previous.NotifyKeyboardLeave();
            Release(previous);
        }

        _focus = surface;
        if (_focus is { } next)
        {
            next.AddObserver(this);
            next.NotifyKeyboardEnter(pressed);
        }

        FocusChanged?.Invoke(_focus);
    }

    /// <summary>
    /// Sends a key event to the focused surface.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="key">The Linux key code.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    /// <returns><see langword="true"/> when a surface has focus.</returns>
    public bool Key(uint timeMs, uint key, bool pressed)
    {
        if (_focus is not { } surface)
        {
            return false;
        }

        surface.NotifyKey(timeMs, key, pressed);
        return true;
    }

    /// <summary>
    /// Sends the xkb modifier state to the focused surface.
    /// </summary>
    /// <param name="depressed">The depressed modifier mask.</param>
    /// <param name="latched">The latched modifier mask.</param>
    /// <param name="locked">The locked modifier mask.</param>
    /// <param name="group">The effective layout group.</param>
    /// <returns><see langword="true"/> when a surface has focus.</returns>
    public bool Modifiers(uint depressed, uint latched, uint locked, uint group)
    {
        if (_focus is not { } surface)
        {
            return false;
        }

        surface.NotifyModifiers(depressed, latched, locked, group);
        return true;
    }

    /// <summary>
    /// Sends committed text from an input method to the focused surface.
    /// </summary>
    /// <param name="text">The text to insert. It is valid only during the call.</param>
    /// <returns><see langword="true"/> when a surface has focus.</returns>
    public bool TextCommit(ReadOnlySpan<char> text)
    {
        if (_focus is not { } surface)
        {
            return false;
        }

        surface.NotifyTextCommit(text);
        return true;
    }

    /// <summary>
    /// Sends the preedit string of an input method to the focused surface.
    /// </summary>
    /// <param name="text">The preedit text. It is valid only during the call.</param>
    /// <param name="cursorBegin">The start of the cursor range in the preedit text.</param>
    /// <param name="cursorEnd">The end of the cursor range in the preedit text.</param>
    /// <returns><see langword="true"/> when a surface has focus.</returns>
    public bool Preedit(ReadOnlySpan<char> text, int cursorBegin, int cursorEnd)
    {
        if (_focus is not { } surface)
        {
            return false;
        }

        surface.NotifyPreedit(text, cursorBegin, cursorEnd);
        return true;
    }

    /// <summary>
    /// Drops every reference to a surface with no event to it. Its touch points, grab, hover and focus are cleared, and the matching change events occur.
    /// </summary>
    /// <param name="surface">The surface that goes away.</param>
    public void Forget(IUISurface surface)
    {
        for (var i = 0; i < TouchCapacity; i++)
        {
            if (ReferenceEquals(_touchSurfaces[i], surface))
            {
                _touchSurfaces[i] = null;
            }
        }

        if (ReferenceEquals(surface, _grab))
        {
            _grab = null;
            _grabButtons = 0;
            Release(surface);
        }

        if (ReferenceEquals(surface, _hovered))
        {
            _hovered = null;
            Release(surface);
            HoverChanged?.Invoke(null);
        }

        if (ReferenceEquals(surface, _focus))
        {
            _focus = null;
            Release(surface);
            FocusChanged?.Invoke(null);
        }
    }

    /// <summary>
    /// Does nothing. The router observes surfaces only to learn when they are destroyed.
    /// </summary>
    /// <param name="surface">The surface that changed.</param>
    /// <param name="damage">The changed region.</param>
    public void OnSurfaceDamaged(IUISurface surface, PixmanRegion32 damage)
    {
    }

    /// <summary>
    /// Calls <see cref="Forget(IUISurface)"/> for the destroyed surface.
    /// </summary>
    /// <param name="surface">The disposed surface.</param>
    public void OnSurfaceDestroyed(IUISurface surface) => Forget(surface);

    private void LeaveHovered()
    {
        if (_hovered is not { } surface)
        {
            return;
        }

        _hovered = null;
        surface.NotifyPointerLeave();
        Release(surface);
    }

    private void EndGrab()
    {
        if (_grab is not { } grabbed)
        {
            return;
        }

        _grab = null;
        _grabButtons = 0;
        Release(grabbed);
    }

    private void Release(IUISurface surface)
    {
        if (!ReferenceEquals(surface, _hovered) && !ReferenceEquals(surface, _focus) && !ReferenceEquals(surface, _grab))
        {
            surface.RemoveObserver(this);
        }
    }

    private bool TouchLatched(IUISurface surface)
    {
        for (var i = 0; i < TouchCapacity; i++)
        {
            if (ReferenceEquals(_touchSurfaces[i], surface))
            {
                return true;
            }
        }

        return false;
    }

    private int TouchSlotOf(int id)
    {
        for (var i = 0; i < TouchCapacity; i++)
        {
            if (_touchSurfaces[i] is not null && _touchIds[i] == id)
            {
                return i;
            }
        }

        return -1;
    }

    private int FreeTouchSlot()
    {
        for (var i = 0; i < TouchCapacity; i++)
        {
            if (_touchSurfaces[i] is null)
            {
                return i;
            }
        }

        return -1;
    }
}
