using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.UI.Quill;
using Pixman;
using Prowl.PaperUI;
using Prowl.Vector;

namespace Basin.UI.Paper;

/// <summary>
/// A chrome surface that runs a Prowl.Paper user interface on a Quill surface. Compositor input becomes Paper input.
/// Use it on the thread that made it.
/// </summary>
public sealed class PaperSurface : IUISurface, IUISurfaceObserver
{
    private const uint BtnLeft = 0x110;
    private const uint BtnRight = 0x111;
    private const uint BtnMiddle = 0x112;
    private const uint BtnSide = 0x113;
    private const uint BtnExtra = 0x114;
    private const double AxisPerNotch = 15.0;
    private const float Outside = -1e6f;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly IQuillUISurface _inner;
    private readonly PaperRendererTap _tap;
    private readonly Prowl.PaperUI.Paper _paper;
    private readonly PaperUIHost? _host;
    private readonly Func<long> _clock;
    private readonly UISurfaceObservers _observers = new();
    private readonly PixmanRegion32 _wholeDamage = new();
    private readonly bool[] _keysDown = new bool[256];
    private int _keysHeld;
    private int _buttonsHeld;
    private float _pointerX = Outside;
    private float _pointerY = Outside;
    private int? _touchId;
    private long _lastFrame;
    private int _lastFingerprint;
    private int _followUp;
    private bool _dirty = true;
    private bool _moving;
    private bool _transitionPending;
    private bool _drawing;
    private bool _disposeAfterDraw;
    private bool _disposed;
    private PaperCursor _cursor = PaperCursor.Default;

    /// <summary>
    /// Initializes a new instance of the <see cref="PaperSurface"/> class with no host. The caller must call <see cref="Draw"/> when <see cref="IsDirty"/> is set.
    /// </summary>
    /// <param name="inner">The Quill surface to draw on. This surface owns and disposes it.</param>
    /// <param name="clockNanos">The monotonic clock in nanoseconds, or <see langword="null"/> for <see cref="MonotonicClock.Nanos"/>.</param>
    public PaperSurface(IQuillUISurface inner, Func<long>? clockNanos = null)
        : this(inner, clockNanos, null)
    {
    }

    internal PaperSurface(IQuillUISurface inner, Func<long>? clockNanos, PaperUIHost? host)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        _host = host;
        _clock = clockNanos ?? (() => MonotonicClock.Nanos);
        _tap = new PaperRendererTap(inner.Renderer);
        var size = inner.Size;
        _paper = new Prowl.PaperUI.Paper(_tap, Math.Max(1, size.Width), Math.Max(1, size.Height), inner.Atlas);
        _paper.OnCursorChange += OnCursorChange;
        _inner.AddObserver(this);
    }

    /// <summary>
    /// Gets the Paper instance that the surface draws with.
    /// </summary>
    public Prowl.PaperUI.Paper Paper => _paper;

    /// <summary>
    /// Gets the Quill surface that the surface draws on.
    /// </summary>
    public IQuillUISurface Inner => _inner;

    /// <summary>
    /// Gets or sets the function that builds the user interface for each frame. An exception from it raises <see cref="Faulted"/>.
    /// </summary>
    public Action<Prowl.PaperUI.Paper>? Build { get; set; }

    /// <summary>
    /// Gets or sets the source of the text that key presses type. Without it, only committed input method text reaches text fields.
    /// </summary>
    public IKeyText? KeyText { get; set; }

    /// <summary>
    /// Gets the cursor that Paper asked for last.
    /// </summary>
    public PaperCursor Cursor => _cursor;

    /// <summary>
    /// Gets a value indicating whether the surface needs a new frame.
    /// </summary>
    public bool IsDirty => _dirty;

    /// <summary>
    /// Gets a value indicating whether the surface must keep drawing, because the last frame changed, input just arrived or a key is held.
    /// </summary>
    public bool IsMoving => _moving || _followUp > 0 || _keysHeld > 0;

    /// <summary>
    /// Gets the number of frames that Paper has drawn into the surface.
    /// </summary>
    public int Frames => _tap.Frames;

    /// <inheritdoc/>
    public UISurfaceSize Size => _inner.Size;

    /// <inheritdoc/>
    public double PositionX => _inner.PositionX;

    /// <inheritdoc/>
    public double PositionY => _inner.PositionY;

    /// <inheritdoc/>
    public bool AcceptsInput => _inner.AcceptsInput;

    /// <summary>
    /// Gets a value indicating whether a Paper element captures the keyboard, for example a focused text field.
    /// </summary>
    public bool WantsTextInput => _paper.WantsCaptureKeyboard;

    /// <summary>
    /// Occurs during a frame when Paper changes the cursor.
    /// </summary>
    public event Action<PaperCursor>? CursorChanged;

    /// <summary>
    /// Occurs when <see cref="Build"/> or Paper throws during a frame. The frame still ends.
    /// </summary>
    public event Action<Exception>? Faulted;

    /// <summary>
    /// Passes the layout position to the Quill surface.
    /// </summary>
    /// <param name="x">The left edge in layout coordinates.</param>
    /// <param name="y">The top edge in layout coordinates.</param>
    public void SetPosition(double x, double y) => _inner.SetPosition(x, y);

    /// <inheritdoc/>
    public void AddObserver(IUISurfaceObserver observer) => _observers.Add(observer);

    /// <inheritdoc/>
    public void RemoveObserver(IUISurfaceObserver observer) => _observers.Remove(observer);

    /// <summary>
    /// Configures the Quill surface, sets the Paper resolution to match and marks the surface dirty.
    /// </summary>
    /// <param name="logicalWidth">The width in logical pixels. It must be positive.</param>
    /// <param name="logicalHeight">The height in logical pixels. It must be positive.</param>
    /// <param name="scale">The scale from logical to buffer pixels. It must be positive.</param>
    /// <returns><see langword="false"/> when the surface is disposed or the Quill surface refuses the values.</returns>
    public bool Configure(int logicalWidth, int logicalHeight, double scale)
    {
        _thread.Assert();
        if (_disposed || !_inner.Configure(logicalWidth, logicalHeight, scale))
        {
            return false;
        }

        var size = _inner.Size;
        _paper.SetResolution(size.Width, size.Height);
        _paper.DisplayFramebufferScale = new Float2((float)size.Scale, (float)size.Scale);
        Invalidate();
        return true;
    }

    /// <summary>
    /// Marks the surface dirty and asks the host for a pump. It does nothing when the surface is already dirty or disposed.
    /// </summary>
    public void Invalidate()
    {
        if (_disposed || _dirty)
        {
            return;
        }

        _dirty = true;
        _host?.Wake();
    }

    /// <summary>
    /// Draws one Paper frame into the Quill surface and tells the observers. A dispose during the frame takes effect after it.
    /// </summary>
    /// <returns><see langword="false"/> when the surface is disposed, is drawing already or has no valid size.</returns>
    public bool Draw()
    {
        _thread.Assert();
        if (_disposed || _drawing)
        {
            return false;
        }

        var size = _inner.Size;
        if (size.Width <= 0 || size.Height <= 0 || !_inner.Configure(size.Width, size.Height, size.Scale))
        {
            return false;
        }

        var now = _clock();
        var delta = _lastFrame == 0 ? 0f : (float)((now - _lastFrame) / 1_000_000_000.0);
        _lastFrame = now;
        _dirty = false;
        _transitionPending = false;
        _drawing = true;
        _inner.BeginTarget();
        try
        {
            _paper.BeginFrame(Math.Max(0f, delta), (float)size.Scale);
            Build?.Invoke(_paper);
            _paper.EndFrame();
        }
        catch (Exception error)
        {
            Faulted?.Invoke(error);
        }
        finally
        {
            _inner.EndTarget();
            _drawing = false;
        }

        if (_disposeAfterDraw)
        {
            Dispose();
            return true;
        }

        _followUp = Math.Max(0, _followUp - 1);
        _moving = _tap.Fingerprint != _lastFingerprint;
        _lastFingerprint = _tap.Fingerprint;
        _observers.Damaged(this, _wholeDamage);
        return true;
    }

    /// <inheritdoc/>
    public bool TryAcquire(out UIFrame frame) => _inner.TryAcquire(out frame);

    /// <inheritdoc/>
    public bool AcceptsInputAt(double x, double y) => _inner.AcceptsInputAt(x, y);

    /// <summary>
    /// Gets the xcursor name of the cursor that Paper asked for last. The point is not used.
    /// </summary>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    /// <returns>The xcursor name.</returns>
    public string? CursorAt(double x, double y) => PaperCursors.NameOf(_cursor);

    /// <inheritdoc/>
    public void NotifyPointerEnter(double x, double y) => PointerAt(x, y);

    /// <inheritdoc/>
    public void NotifyPointerMotion(uint timeMs, double x, double y) => PointerAt(x, y);

    /// <summary>
    /// Tells Paper that a pointer button changed. Buttons other than left, right, middle, side and extra are ignored.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="button">The evdev button code.</param>
    /// <param name="pressed"><see langword="true"/> when the button went down.</param>
    public void NotifyPointerButton(uint timeMs, uint button, bool pressed)
    {
        var btn = ButtonOf(button);
        if (btn == PaperMouseBtn.Unknown || _disposed)
        {
            return;
        }

        Transition();
        _buttonsHeld = Math.Max(0, _buttonsHeld + (pressed ? 1 : -1));
        _paper.SetPointerState(btn, _pointerX, _pointerY, pressed, false);
        Input();
    }

    /// <summary>
    /// Tells Paper that the pointer scrolled. Paper gets one wheel step for each 15 units of vertical scroll. Horizontal scroll is ignored.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="dx">The horizontal scroll distance.</param>
    /// <param name="dy">The vertical scroll distance, in wl_pointer axis units.</param>
    public void NotifyPointerAxis(uint timeMs, double dx, double dy)
    {
        if (_disposed || dy == 0)
        {
            return;
        }

        _paper.SetPointerWheel((float)(-dy / AxisPerNotch));
        Input();
    }

    /// <summary>
    /// Moves the Paper pointer outside the surface. It does nothing while a button is held, so a drag continues.
    /// </summary>
    public void NotifyPointerLeave()
    {
        if (_disposed || _buttonsHeld > 0)
        {
            return;
        }

        PointerAt(Outside, Outside);
    }

    /// <summary>
    /// Asks for a new frame. The keys that are down at entry are not sent to Paper.
    /// </summary>
    /// <param name="pressed">The evdev codes of the keys that are down at entry.</param>
    public void NotifyKeyboardEnter(ReadOnlySpan<uint> pressed)
    {
        Input();
    }

    /// <summary>
    /// Sends a key change to Paper. On a press it also types the text that <see cref="KeyText"/> gives for the key.
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

        var paperKey = EvdevPaperKeys.KeyOf(key);
        if (paperKey != PaperKey.Unknown)
        {
            var index = (int)paperKey;
            if (_keysDown[index] != pressed)
            {
                Transition();
                _keysDown[index] = pressed;
                _keysHeld += pressed ? 1 : -1;
                _paper.SetKeyState(paperKey, pressed);
            }
        }

        if (pressed && KeyText is { } keyText)
        {
            Span<char> text = stackalloc char[16];
            var length = keyText.TextFor(key, text);
            Push(text[..length]);
        }

        Input();
    }

    /// <summary>
    /// Releases every key that Paper sees as held.
    /// </summary>
    public void NotifyKeyboardLeave()
    {
        if (_disposed)
        {
            return;
        }

        for (var i = 0; i < _keysDown.Length; i++)
        {
            if (_keysDown[i])
            {
                Transition();
                _keysDown[i] = false;
                _paper.SetKeyState((PaperKey)i, false);
            }
        }

        _keysHeld = 0;
        Input();
    }

    /// <summary>
    /// Types committed input method text into Paper. A carriage return becomes a line feed.
    /// </summary>
    /// <param name="text">The committed text.</param>
    public void NotifyTextCommit(ReadOnlySpan<char> text)
    {
        if (_disposed)
        {
            return;
        }

        Push(text);
        Input();
    }

    /// <summary>
    /// Presses the left button at the touch point. Only the first touch point acts, and other points are ignored while it is down.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The touch point id.</param>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    public void NotifyTouchDown(uint timeMs, int id, double x, double y)
    {
        if (_touchId is not null)
        {
            return;
        }

        _touchId = id;
        PointerAt(x, y);
        NotifyPointerButton(timeMs, BtnLeft, true);
    }

    /// <summary>
    /// Moves the Paper pointer when the point is the touch point that acts.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The touch point id.</param>
    /// <param name="x">The surface-local x coordinate in logical pixels.</param>
    /// <param name="y">The surface-local y coordinate in logical pixels.</param>
    public void NotifyTouchMotion(uint timeMs, int id, double x, double y)
    {
        if (_touchId == id)
        {
            PointerAt(x, y);
        }
    }

    /// <summary>
    /// Releases the left button and moves the pointer outside when the point is the touch point that acts.
    /// </summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="id">The touch point id.</param>
    public void NotifyTouchUp(uint timeMs, int id)
    {
        if (_touchId != id)
        {
            return;
        }

        _touchId = null;
        NotifyPointerButton(timeMs, BtnLeft, false);
        PointerAt(Outside, Outside);
    }

    /// <summary>
    /// Releases the left button of the touch point that acts and moves the pointer outside.
    /// </summary>
    public void NotifyTouchCancel()
    {
        if (_touchId is null)
        {
            return;
        }

        _touchId = null;
        NotifyPointerButton(0, BtnLeft, false);
        PointerAt(Outside, Outside);
    }

    /// <summary>
    /// Returns no popup. Paper draws its popups inside the surface.
    /// </summary>
    /// <param name="anchor">The anchor box in surface-local logical coordinates.</param>
    /// <param name="gravity">The direction in which the popup extends from the anchor.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public IUISurface? CreatePopup(in Box anchor, UIPopupGravity gravity) => null;

    /// <summary>
    /// Ignores damage from the Quill surface. The surface reports its own damage after each <see cref="Draw"/>.
    /// </summary>
    /// <param name="surface">The Quill surface.</param>
    /// <param name="damage">The changed region.</param>
    public void OnSurfaceDamaged(IUISurface surface, PixmanRegion32 damage)
    {
    }

    /// <summary>
    /// Disposes this surface when the Quill surface is disposed first.
    /// </summary>
    /// <param name="surface">The disposed surface.</param>
    public void OnSurfaceDestroyed(IUISurface surface)
    {
        if (ReferenceEquals(surface, _inner) && !_disposed)
        {
            Dispose();
        }
    }

    /// <summary>
    /// Disposes the Paper canvas and the Quill surface and tells the observers. During a <see cref="Draw"/> the dispose waits until the frame ends.
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
        _paper.OnCursorChange -= OnCursorChange;
        _inner.RemoveObserver(this);
        _host?.Forget(this);
        _paper.Canvas.Dispose();
        _inner.Dispose();
        _wholeDamage.Dispose();
        _observers.Destroyed(this);
    }

    private void Input()
    {
        _followUp = 2;
        Invalidate();
    }

    private void PointerAt(double x, double y)
    {
        if (_disposed)
        {
            return;
        }

        _pointerX = (float)x;
        _pointerY = (float)y;
        _paper.SetPointerPosition(_pointerX, _pointerY);
        Input();
    }

    private void Transition()
    {
        if (_transitionPending && !_drawing)
        {
            Draw();
        }

        _transitionPending = true;
    }

    private void Push(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            _paper.PushInputText(c == '\r' ? '\n' : c);
        }
    }

    private void OnCursorChange(PaperCursor cursor)
    {
        _cursor = cursor;
        CursorChanged?.Invoke(cursor);
    }

    private static PaperMouseBtn ButtonOf(uint button) => button switch
    {
        BtnLeft => PaperMouseBtn.Left,
        BtnRight => PaperMouseBtn.Right,
        BtnMiddle => PaperMouseBtn.Middle,
        BtnSide => PaperMouseBtn.Button4,
        BtnExtra => PaperMouseBtn.Button5,
        _ => PaperMouseBtn.Unknown,
    };
}
