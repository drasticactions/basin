using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Diagnostics;
using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using Pixman;
using ImGuiApi = Hexa.NET.ImGui.ImGui;
using ImGuiInternal = Hexa.NET.ImGui.ImGuiP;

namespace Basin.UI.ImGui;

/// <summary>
/// A Dear ImGui surface. It draws <see cref="Content"/> in its own contexts, renders only when the draw data changes, and takes input only where ImGui shows a window.
/// </summary>
public sealed unsafe class ImGuiUISurface : IUISurface
{
    private const uint BtnLeft = 0x110;
    private const uint BtnRight = 0x111;
    private const uint BtnMiddle = 0x112;
    private const uint BtnSide = 0x113;
    private const uint BtnExtra = 0x114;
    private const double AxisPerNotch = 15.0;
    private const float MinimumDelta = 1e-6f;
    private const long InputLingerNanos = 600_000_000;
    private const int MaxChainedFrames = 3;
    private const int MaxRects = 64;
    private const int RectHistory = 3;
    private const int LogLimit = 64 * 1024;

    private static readonly byte* EmptyText = (byte*)NativeMemory.AllocZeroed(1);

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly ImGuiUIHost _host;
    private readonly IImGuiTarget _target;
    private readonly ImGuiContextSet _contexts;
    private readonly Func<long> _clock;
    private readonly UISurfaceObservers _observers = new();
    private readonly PixmanRegion32 _damage = new();
    private readonly Box[] _rects = new Box[MaxRects * RectHistory];
    private readonly Box[] _holes = new Box[MaxRects];
    private readonly int[] _rectCounts = new int[RectHistory];
    private readonly bool[] _keysDown = new bool[256];
    private GCHandle _self;
    private ImGuiClipboard? _clipboard;
    private int _rectFrame;
    private int _width;
    private int _height;
    private double _scale;
    private long _lastFrame;
    private long _inputUntil;
    private long _lastHash;
    private long _renderedHash;
    private int _keysHeld;
    private int _buttonsHeld;
    private int _refreshHz;
    private int _chained;
    private int? _touchId;
    private double _pointerX = double.NaN;
    private double _pointerY = double.NaN;
    private bool _dirty = true;
    private bool _moving;
    private bool _popupOpen;
    private bool _tooltipOpen;
    private bool _wantsText;
    private bool _queued;
    private bool _produced;
    private bool _drawing;
    private bool _disposeAfterDraw;
    private bool _disposed;
    private ImGuiMouseCursor _cursor = ImGuiMouseCursor.Arrow;
    private Vector2 _imePosition;
    private float _fontSize;
    private bool _imeVisible;

    internal ImGuiUISurface(ImGuiUIHost host, IImGuiTarget target, ImFontAtlasPtr atlas, float fontSize, Func<long> clock)
    {
        _host = host;
        _target = target;
        _clock = clock;
        _contexts = new ImGuiContextSet(atlas);
        _self = GCHandle.Alloc(this);
        using var scope = _contexts.Enter();
        var io = ImGuiApi.GetIO();
        io.BackendPlatformUserData = (void*)GCHandle.ToIntPtr(_self);
        if (host.HasVertexOffset)
        {
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        }

        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        var context = _contexts.Context.Handle;
        context->DebugLogFlags &= ~ImGuiDebugLogFlags.OutputToTty;
        context->DebugLogFlags |= ImGuiDebugLogFlags.EventError;
        var platform = ImGuiApi.GetPlatformIO();
        platform.PlatformGetClipboardTextFn = (void*)(delegate* unmanaged[Cdecl]<ImGuiContext*, byte*>)&GetClipboardText;
        platform.PlatformSetClipboardTextFn = (void*)(delegate* unmanaged[Cdecl]<ImGuiContext*, byte*, void>)&SetClipboardText;
        platform.PlatformSetImeDataFn = (void*)(delegate* unmanaged[Cdecl]<ImGuiContext*, ImGuiViewport*, ImGuiPlatformImeData*, void>)&SetImeData;
        ImGuiApi.GetStyle().FontSizeBase = fontSize;
        _fontSize = fontSize;
    }

    /// <summary>
    /// Gets or sets the base font size in logical pixels. A change takes effect on the next frame. A value of 0 or less is ignored.
    /// </summary>
    public float FontSize
    {
        get => _fontSize;
        set
        {
            if (_disposed || value <= 0 || value == _fontSize)
            {
                return;
            }

            _fontSize = value;
            using (_contexts.Enter())
            {
                ImGuiApi.GetStyle().FontSizeBase = value;
            }

            Invalidate();
        }
    }

    /// <summary>
    /// Gets or sets the panels that each frame draws. When it is <see langword="null"/>, the frames are empty.
    /// </summary>
    public IImGuiContent? Content { get; set; }

    /// <summary>
    /// Gets or sets the source of the text that a key press types. When it is <see langword="null"/>, keys type no text, and only <see cref="NotifyTextCommit"/> does.
    /// </summary>
    public IKeyText? KeyText { get; set; }

    /// <summary>
    /// Gets or sets the xkb masks that <see cref="NotifyModifiers"/> maps onto the ImGui modifiers. The default is <see cref="ImGuiModifierMasks.Standard"/>.
    /// </summary>
    public ImGuiModifierMasks Modifiers { get; set; } = ImGuiModifierMasks.Standard;

    /// <summary>
    /// Gets the contexts of the surface. A caller that uses ImGui outside <see cref="IImGuiContent.Build"/> must enter them first.
    /// </summary>
    public ImGuiContextSet Contexts => _contexts;

    /// <inheritdoc/>
    public UISurfaceSize Size => new(_width, _height, _scale);

    /// <summary>
    /// Gets a value indicating whether the surface must draw on the next pump, for example after input, <see cref="Configure"/> or <see cref="Invalidate"/>.
    /// </summary>
    public bool IsDirty => _dirty;

    /// <summary>
    /// Gets a value indicating whether the surface needs frames at the host interval. It is true while frames change, input is queued or recent, or a key or a button is held.
    /// It is also true while a text field is active or a popup or tooltip is open.
    /// </summary>
    public bool IsAnimating =>
        _moving || _queued || _keysHeld > 0 || _buttonsHeld > 0 || _wantsText || _popupOpen || _tooltipOpen
        || _clock() < _inputUntil;

    /// <summary>
    /// Gets the highest rate that the last frame asked for through <see cref="ImGuiFrame.RequestRefresh"/>. It is 0 when no panel asked.
    /// </summary>
    public int RefreshHz => _refreshHz;

    /// <summary>
    /// Gets the host clock time in nanoseconds of the last frame. It is 0 before the first frame.
    /// </summary>
    public long LastFrameNanos => _lastFrame;

    /// <summary>
    /// Gets the number of frames that the surface built.
    /// </summary>
    public int Frames { get; private set; }

    /// <summary>
    /// Gets the number of frames that the renderer drew. A frame with the same draw data as the last render does not count.
    /// </summary>
    public int Renders { get; private set; }

    /// <summary>
    /// Gets a value indicating whether an ImGui text field was active in the last frame.
    /// </summary>
    public bool WantsTextInput => _wantsText;

    /// <summary>
    /// Gets the position of the text cursor that ImGui reported for the input method, in surface-local logical pixels.
    /// </summary>
    public Vector2 TextInputPosition => _imePosition;

    /// <summary>
    /// Gets a value indicating whether ImGui wants the input method shown.
    /// </summary>
    public bool TextInputVisible => _imeVisible;

    /// <summary>
    /// Gets the xcursor name that the last frame asked for, or <see langword="null"/> when ImGui hides the cursor.
    /// </summary>
    public string? Cursor => ImGuiCursors.NameOf(_cursor);

    /// <summary>
    /// Occurs when <see cref="IImGuiContent.Build"/> throws. The surface catches the exception, raises the event, and renders the frame.
    /// </summary>
    public event Action<Exception>? Faulted;

    /// <summary>
    /// Connects the ImGui copy and paste to a clipboard. It replaces the clipboard that an earlier call set.
    /// </summary>
    /// <param name="store">The selection store, or <see langword="null"/> to disconnect the clipboard.</param>
    /// <param name="loop">The loop that reads the clipboard pipe.</param>
    public void SetClipboard(ISelectionStore? store, ICompositorEventLoop loop)
    {
        _clipboard?.Dispose();
        _clipboard = store is null ? null : new ImGuiClipboard(store, loop);
    }

    /// <inheritdoc/>
    public void AddObserver(IUISurfaceObserver observer) => _observers.Add(observer);

    /// <inheritdoc/>
    public void RemoveObserver(IUISurfaceObserver observer) => _observers.Remove(observer);

    /// <summary>
    /// Sets the logical size and the scale for the next frame. The surface snaps the scale and resizes its render target. It returns <see langword="false"/> during a draw.
    /// </summary>
    /// <param name="logicalWidth">The width in logical pixels. It must be positive.</param>
    /// <param name="logicalHeight">The height in logical pixels. It must be positive.</param>
    /// <param name="scale">The scale from logical to buffer pixels. It must be positive.</param>
    /// <returns><see langword="false"/> when a value is not valid, the surface is disposed or drawing, or the target rejects the size.</returns>
    public bool Configure(int logicalWidth, int logicalHeight, double scale)
    {
        _thread.Assert();
        if (_disposed || _drawing || logicalWidth <= 0 || logicalHeight <= 0 || scale <= 0)
        {
            return false;
        }

        scale = OutputScaling.Snap(scale);
        var physical = OutputScaling.ToPhysical(new Box(0, 0, logicalWidth, logicalHeight), scale);
        if (physical.IsEmpty || !_target.Configure(physical.Width, physical.Height))
        {
            return false;
        }

        if (logicalWidth != _width || logicalHeight != _height || scale != _scale)
        {
            _renderedHash = 0;
            Array.Clear(_rectCounts);
        }

        (_width, _height, _scale) = (logicalWidth, logicalHeight, scale);
        Invalidate();
        return true;
    }

    /// <summary>
    /// Makes the surface draw on the next pump and asks the host for a wakeup. The call does nothing when the surface is already dirty or disposed.
    /// </summary>
    public void Invalidate()
    {
        if (_disposed || _dirty)
        {
            return;
        }

        _dirty = true;
        _host.Wake();
    }

    internal void RequestRefresh(int hz)
    {
        if (hz > _refreshHz)
        {
            _refreshHz = hz;
        }
    }

    /// <summary>
    /// Builds one frame now and renders it when the draw data changed. The host calls it from <see cref="ImGuiUIHost.Pump"/>.
    /// </summary>
    /// <returns><see langword="false"/> when the surface is disposed, has no size, or is already drawing. Otherwise <see langword="true"/>, also when the frame did not render.</returns>
    public bool Draw()
    {
        _thread.Assert();
        if (_disposed || _drawing || _width <= 0 || _height <= 0)
        {
            return false;
        }

        var now = _clock();
        var delta = _lastFrame == 0 ? 1f / 60f : (float)((now - _lastFrame) / 1_000_000_000.0);
        var driven = _dirty || _queued || _keysHeld > 0 || _buttonsHeld > 0 || _wantsText || _popupOpen || _tooltipOpen
                     || now < _inputUntil;
        _chained = driven ? 0 : _moving ? _chained + 1 : MaxChainedFrames;
        _lastFrame = now;
        _dirty = false;
        _refreshHz = 0;
        _drawing = true;
        bool render;
        try
        {
            using var scope = _contexts.Enter();
            var io = ImGuiApi.GetIO();
            io.DisplaySize = new Vector2(_width, _height);
            io.DisplayFramebufferScale = new Vector2((float)_scale, (float)_scale);
            io.DeltaTime = Math.Max(MinimumDelta, delta);
            _host.Fonts.NextFrame();
            ImGuiApi.NewFrame();
            ImGuizmo.BeginFrame();
            try
            {
                Content?.Build(new ImGuiFrame(this, _width, _height, _scale, io.DeltaTime));
            }
            catch (Exception error)
            {
                Faulted?.Invoke(error);
            }

            ImGuiApi.Render();
            var data = ImGuiApi.GetDrawData().Handle;
            var textures = ImGuiTextures.Process(data, _host.Backend);
            var hash = ImGuiDrawHash.Of(data);
            _moving = hash != _lastHash && _chained < MaxChainedFrames;
            _lastHash = hash;
            Collect(io);
            render = textures || hash != _renderedHash || !_produced;
            if (render)
            {
                _target.Render(data);
                _renderedHash = hash;
                _produced = true;
                Renders++;
            }

            ForwardLog();
        }
        finally
        {
            _drawing = false;
        }

        Frames++;
        if (_disposeAfterDraw)
        {
            Dispose();
            return true;
        }

        if (render)
        {
            BuildDamage();
            _observers.Damaged(this, _damage);
        }

        return true;
    }

    /// <inheritdoc/>
    public bool TryAcquire(out UIFrame frame)
    {
        _thread.Assert();
        if (_disposed || !_produced)
        {
            frame = default;
            return false;
        }

        return _target.TryAcquire(_damage, out frame);
    }

    /// <summary>
    /// Finds whether a point is inside a window of the last frame. A window with no mouse input, a dock host and the hole of a dock space do not count. While a popup is open or a button is held, every point counts.
    /// </summary>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    /// <returns><see langword="true"/> when input at the point goes to the surface.</returns>
    public bool AcceptsInputAt(double x, double y)
    {
        _thread.Assert();
        if (_disposed || x < 0 || y < 0 || x >= _width || y >= _height)
        {
            return false;
        }

        if (_popupOpen || _buttonsHeld > 0)
        {
            return true;
        }

        var start = _rectFrame * MaxRects;
        var count = _rectCounts[_rectFrame];
        for (var i = 0; i < count; i++)
        {
            ref readonly var rect = ref _rects[start + i];
            if (Inside(rect, x, y) && !Inside(_holes[i], x, y))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the cursor that the last frame asked for. The point has no effect, because ImGui reports one cursor for each frame.
    /// </summary>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    /// <returns>An xcursor name, or <see langword="null"/> when ImGui hides the cursor.</returns>
    public string? CursorAt(double x, double y) => ImGuiCursors.NameOf(_cursor);

    /// <inheritdoc/>
    public void NotifyPointerEnter(double x, double y) => PointerAt(x, y);

    /// <inheritdoc/>
    public void NotifyPointerMotion(uint timeMs, double x, double y)
    {
        if (x == _pointerX && y == _pointerY)
        {
            return;
        }

        PointerAt(x, y);
    }

    /// <summary>
    /// Tells the surface that a pointer button changed. Only the left, right, middle, side and extra buttons reach ImGui.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="button">The evdev button code, for example BTN_LEFT.</param>
    /// <param name="pressed"><see langword="true"/> when the button went down.</param>
    public void NotifyPointerButton(uint timeMs, uint button, bool pressed)
    {
        var index = ButtonOf(button);
        if (index < 0 || _disposed)
        {
            return;
        }

        _buttonsHeld = Math.Max(0, _buttonsHeld + (pressed ? 1 : -1));
        using (_contexts.Enter())
        {
            var io = ImGuiApi.GetIO();
            io.AddMouseSourceEvent(ImGuiMouseSource.Mouse);
            io.AddMouseButtonEvent(index, pressed);
        }

        Input();
    }

    /// <summary>
    /// Tells the surface that the pointer scrolled. The surface turns 15 axis units into one wheel notch.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="dx">The horizontal scroll distance, in wl_pointer axis units.</param>
    /// <param name="dy">The vertical scroll distance, in wl_pointer axis units.</param>
    public void NotifyPointerAxis(uint timeMs, double dx, double dy)
    {
        if (_disposed || (dx == 0 && dy == 0))
        {
            return;
        }

        using (_contexts.Enter())
        {
            ImGuiApi.GetIO().AddMouseWheelEvent((float)(-dx / AxisPerNotch), (float)(-dy / AxisPerNotch));
        }

        Input();
    }

    /// <summary>
    /// Tells the surface that the pointer left it. While a button is held, the surface ignores the leave, so that a drag continues.
    /// </summary>
    public void NotifyPointerLeave()
    {
        if (_disposed || _buttonsHeld > 0)
        {
            return;
        }

        _pointerX = double.NaN;
        _pointerY = double.NaN;
        using (_contexts.Enter())
        {
            ImGuiApi.GetIO().AddMousePosEvent(-float.MaxValue, -float.MaxValue);
        }

        Input();
    }

    /// <summary>
    /// Gives keyboard focus to the surface. The keys that are down at entry do not reach ImGui.
    /// </summary>
    /// <param name="pressed">The evdev codes of the keys that are down at entry.</param>
    public void NotifyKeyboardEnter(ReadOnlySpan<uint> pressed)
    {
        if (_disposed)
        {
            return;
        }

        using (_contexts.Enter())
        {
            ImGuiApi.GetIO().AddFocusEvent(true);
        }

        Input();
    }

    /// <summary>
    /// Tells the surface that a key changed. A press also types the text that <see cref="KeyText"/> gives for the key.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="key">The evdev key code.</param>
    /// <param name="pressed"><see langword="true"/> when the key went down.</param>
    public void NotifyKey(uint timeMs, uint key, bool pressed)
    {
        if (_disposed)
        {
            return;
        }

        using (_contexts.Enter())
        {
            var io = ImGuiApi.GetIO();
            var imguiKey = EvdevImGuiKeys.KeyOf(key);
            if (imguiKey != ImGuiKey.None && key < (uint)_keysDown.Length && _keysDown[key] != pressed)
            {
                _keysDown[key] = pressed;
                _keysHeld = Math.Max(0, _keysHeld + (pressed ? 1 : -1));
                io.AddKeyEvent(imguiKey, pressed);
            }

            if (pressed && KeyText is { } keyText)
            {
                Span<char> text = stackalloc char[16];
                var length = keyText.TextFor(key, text);
                Push(io, text[..Math.Clamp(length, 0, text.Length)]);
            }
        }

        Input();
    }

    /// <summary>
    /// Sets the ImGui modifiers from the xkb state through <see cref="Modifiers"/>. A depressed, latched or locked modifier counts as down.
    /// </summary>
    /// <param name="depressed">The xkb mask of the depressed modifiers.</param>
    /// <param name="latched">The xkb mask of the latched modifiers.</param>
    /// <param name="locked">The xkb mask of the locked modifiers.</param>
    /// <param name="group">The xkb layout group. The surface ignores it.</param>
    public void NotifyModifiers(uint depressed, uint latched, uint locked, uint group)
    {
        if (_disposed)
        {
            return;
        }

        var active = depressed | latched | locked;
        var masks = Modifiers;
        using (_contexts.Enter())
        {
            var io = ImGuiApi.GetIO();
            io.AddKeyEvent(ImGuiKey.ModCtrl, masks.Control != 0 && (active & masks.Control) != 0);
            io.AddKeyEvent(ImGuiKey.ModShift, masks.Shift != 0 && (active & masks.Shift) != 0);
            io.AddKeyEvent(ImGuiKey.ModAlt, masks.Alt != 0 && (active & masks.Alt) != 0);
            io.AddKeyEvent(ImGuiKey.ModSuper, masks.Super != 0 && (active & masks.Super) != 0);
        }

        Input();
    }

    /// <summary>
    /// Takes keyboard focus away. The surface releases every key and modifier that it holds down in ImGui.
    /// </summary>
    public void NotifyKeyboardLeave()
    {
        if (_disposed)
        {
            return;
        }

        using (_contexts.Enter())
        {
            var io = ImGuiApi.GetIO();
            for (var i = 0; i < _keysDown.Length; i++)
            {
                if (_keysDown[i])
                {
                    _keysDown[i] = false;
                    io.AddKeyEvent(EvdevImGuiKeys.KeyOf((uint)i), false);
                }
            }

            io.AddKeyEvent(ImGuiKey.ModCtrl, false);
            io.AddKeyEvent(ImGuiKey.ModShift, false);
            io.AddKeyEvent(ImGuiKey.ModAlt, false);
            io.AddKeyEvent(ImGuiKey.ModSuper, false);
            io.AddFocusEvent(false);
        }

        _keysHeld = 0;
        Input();
    }

    /// <summary>
    /// Types committed text into the active field. Control characters are dropped.
    /// </summary>
    /// <param name="text">The committed text.</param>
    public void NotifyTextCommit(ReadOnlySpan<char> text)
    {
        if (_disposed)
        {
            return;
        }

        using (_contexts.Enter())
        {
            Push(ImGuiApi.GetIO(), text);
        }

        Input();
    }

    /// <summary>
    /// Ignores the preedit text. ImGui shows no preedit.
    /// </summary>
    /// <param name="text">The preedit text.</param>
    /// <param name="cursorBegin">The start of the cursor in the preedit.</param>
    /// <param name="cursorEnd">The end of the cursor in the preedit.</param>
    public void NotifyPreedit(ReadOnlySpan<char> text, int cursorBegin, int cursorEnd)
    {
    }

    /// <summary>
    /// Starts a touch press as an ImGui left button. The surface follows one touch point and ignores others while it is down.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The touch point id.</param>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    public void NotifyTouchDown(uint timeMs, int id, double x, double y)
    {
        if (_disposed || _touchId is not null)
        {
            return;
        }

        _touchId = id;
        _pointerX = double.NaN;
        _pointerY = double.NaN;
        using (_contexts.Enter())
        {
            var io = ImGuiApi.GetIO();
            io.AddMouseSourceEvent(ImGuiMouseSource.TouchScreen);
            io.AddMousePosEvent((float)x, (float)y);
            io.AddMouseSourceEvent(ImGuiMouseSource.TouchScreen);
            io.AddMouseButtonEvent(0, true);
        }

        _buttonsHeld++;
        Input();
    }

    /// <summary>
    /// Moves the followed touch point. Other points are ignored.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The touch point id.</param>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    public void NotifyTouchMotion(uint timeMs, int id, double x, double y)
    {
        if (_disposed || _touchId != id)
        {
            return;
        }

        using (_contexts.Enter())
        {
            var io = ImGuiApi.GetIO();
            io.AddMouseSourceEvent(ImGuiMouseSource.TouchScreen);
            io.AddMousePosEvent((float)x, (float)y);
        }

        Input();
    }

    /// <summary>
    /// Releases the ImGui left button when the followed touch point goes up. Other points are ignored.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The touch point id.</param>
    public void NotifyTouchUp(uint timeMs, int id)
    {
        if (_disposed || _touchId != id)
        {
            return;
        }

        ReleaseTouch();
    }

    /// <summary>
    /// Releases the followed touch point, if one is down.
    /// </summary>
    public void NotifyTouchCancel()
    {
        if (_disposed || _touchId is null)
        {
            return;
        }

        ReleaseTouch();
    }

    /// <summary>
    /// Makes no popup. ImGui draws its popups inside the surface.
    /// </summary>
    /// <param name="anchor">The anchor box. The surface ignores it.</param>
    /// <param name="gravity">The popup direction. The surface ignores it.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public IUISurface? CreatePopup(in Box anchor, UIPopupGravity gravity) => null;

    /// <summary>
    /// Destroys the contexts and the render target, and tells the observers. A call during <see cref="Draw"/> takes effect when the draw ends.
    /// </summary>
    public void Dispose()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        if (_drawing)
        {
            _disposeAfterDraw = true;
            return;
        }

        _disposed = true;
        _host.Forget(this);
        _clipboard?.Dispose();
        _clipboard = null;
        _contexts.Dispose();
        if (_self.IsAllocated)
        {
            _self.Free();
        }

        _target.Dispose();
        _damage.Dispose();
        _observers.Destroyed(this);
    }

    private void ReleaseTouch()
    {
        _touchId = null;
        _buttonsHeld = Math.Max(0, _buttonsHeld - 1);
        using (_contexts.Enter())
        {
            var io = ImGuiApi.GetIO();
            io.AddMouseSourceEvent(ImGuiMouseSource.TouchScreen);
            io.AddMouseButtonEvent(0, false);
            io.AddMouseSourceEvent(ImGuiMouseSource.TouchScreen);
            io.AddMousePosEvent(-float.MaxValue, -float.MaxValue);
        }

        Input();
    }

    private void PointerAt(double x, double y)
    {
        if (_disposed)
        {
            return;
        }

        _pointerX = x;
        _pointerY = y;
        using (_contexts.Enter())
        {
            var io = ImGuiApi.GetIO();
            io.AddMouseSourceEvent(ImGuiMouseSource.Mouse);
            io.AddMousePosEvent((float)x, (float)y);
        }

        Input();
    }

    private void Input()
    {
        _inputUntil = _clock() + InputLingerNanos;
        _queued = true;
        Invalidate();
    }

    private static void Push(ImGuiIOPtr io, ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (c >= 0x20 && c != 0x7f)
            {
                io.AddInputCharacterUTF16(c);
            }
        }
    }

    private void Collect(ImGuiIOPtr io)
    {
        var context = _contexts.Context.Handle;
        _queued = context->InputEventsQueue.Size > 0;
        _wantsText = io.WantTextInput;
        _cursor = ImGuiApi.GetMouseCursor();
        _popupOpen = context->OpenPopupStack.Size > 0;
        _tooltipOpen = false;

        _rectFrame = (_rectFrame + 1) % RectHistory;
        var start = _rectFrame * MaxRects;
        var count = 0;
        var windows = context->Windows;
        for (var i = 0; i < windows.Size; i++)
        {
            var window = windows.Data[i].Handle;
            if (window is null || window->Active == 0 || window->Hidden != 0 || window->IsFallbackWindow != 0)
            {
                continue;
            }

            var flags = window->Flags;
            if ((flags & ImGuiWindowFlags.Tooltip) != 0)
            {
                _tooltipOpen = true;
            }

            if ((flags & (ImGuiWindowFlags.ChildWindow | ImGuiWindowFlags.DockNodeHost)) != 0
                || (flags & ImGuiWindowFlags.NoMouseInputs) != 0 || count == MaxRects)
            {
                continue;
            }

            var x = (int)MathF.Floor(window->Pos.X);
            var y = (int)MathF.Floor(window->Pos.Y);
            var right = (int)MathF.Ceiling(window->Pos.X + window->Size.X);
            var bottom = (int)MathF.Ceiling(window->Pos.Y + window->Size.Y);
            if (right > x && bottom > y)
            {
                var hole = window->HitTestHoleSize;
                var offset = window->HitTestHoleOffset;
                _holes[count] = hole.X > 0 && hole.Y > 0 ? new Box(x + offset.X, y + offset.Y, hole.X, hole.Y) : default;
                _rects[start + count++] = new Box(x, y, right - x, bottom - y);
            }
        }

        _rectCounts[_rectFrame] = count;
    }

    private void BuildDamage()
    {
        _damage.Clear();
        for (var frame = 0; frame < RectHistory; frame++)
        {
            var start = frame * MaxRects;
            for (var i = 0; i < _rectCounts[frame]; i++)
            {
                var physical = OutputScaling.ToPhysical(_rects[start + i], _scale);
                if (!physical.IsEmpty)
                {
                    _damage.UnionRect(_damage, physical.X, physical.Y, (uint)physical.Width, (uint)physical.Height);
                }
            }
        }

        if (_popupOpen)
        {
            var whole = OutputScaling.ToPhysical(new Box(0, 0, _width, _height), _scale);
            _damage.UnionRect(_damage, 0, 0, (uint)whole.Width, (uint)whole.Height);
        }
    }

    private void ForwardLog()
    {
        var context = _contexts.Context.Handle;
        ref var buffer = ref context->DebugLogBuf.Buf;
        if (buffer.Size <= 1)
        {
            return;
        }

        var text = new ReadOnlySpan<byte>(buffer.Data, buffer.Size - 1);
        while (!text.IsEmpty)
        {
            var end = text.IndexOf((byte)'\n');
            var line = end < 0 ? text : text[..end];
            if (!line.IsEmpty)
            {
                ImGuiLog.Log.Warn($"{System.Text.Encoding.UTF8.GetString(line)}");
            }

            text = end < 0 ? default : text[(end + 1)..];
        }

        ImGuiApi.clear(&context->DebugLogBuf);
        ImGuiInternal.clear(&context->DebugLogIndex);
    }

    private static bool Inside(in Box box, double x, double y) =>
        box.Width > 0 && x >= box.X && y >= box.Y && x < box.X + box.Width && y < box.Y + box.Height;

    private static int ButtonOf(uint button) => button switch
    {
        BtnLeft => 0,
        BtnRight => 1,
        BtnMiddle => 2,
        BtnSide => 3,
        BtnExtra => 4,
        _ => -1,
    };

    private static ImGuiUISurface? SurfaceOf(ImGuiContext* context)
    {
        if (context is null)
        {
            return null;
        }

        var handle = (nint)context->IO.BackendPlatformUserData;
        return handle == 0 ? null : GCHandle.FromIntPtr(handle).Target as ImGuiUISurface;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte* GetClipboardText(ImGuiContext* context)
    {
        try
        {
            var text = SurfaceOf(context)?._clipboard is { } clipboard ? clipboard.NativeText : null;
            return text is null ? EmptyText : text;
        }
        catch (Exception error)
        {
            ImGuiLog.Log.Error(error, $"clipboard: read callback failed");
            return EmptyText;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SetClipboardText(ImGuiContext* context, byte* text)
    {
        try
        {
            if (SurfaceOf(context)?._clipboard is { } clipboard && text is not null)
            {
                clipboard.SetText(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(text));
            }
        }
        catch (Exception error)
        {
            ImGuiLog.Log.Error(error, $"clipboard: write callback failed");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SetImeData(ImGuiContext* context, ImGuiViewport* viewport, ImGuiPlatformImeData* data)
    {
        try
        {
            if (SurfaceOf(context) is { } surface && data is not null)
            {
                surface._imeVisible = data->WantVisible != 0;
                surface._imePosition = data->InputPos;
            }
        }
        catch (Exception error)
        {
            ImGuiLog.Log.Error(error, $"ime: text input placement callback failed");
        }
    }
}
