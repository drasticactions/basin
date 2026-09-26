using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.UI.Quill;
using Pixman;
using Prowl.PaperUI;
using Prowl.Vector;

namespace Basin.UI.Paper;

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

    public Prowl.PaperUI.Paper Paper => _paper;

    public IQuillUISurface Inner => _inner;

    public Action<Prowl.PaperUI.Paper>? Build { get; set; }

    public IKeyText? KeyText { get; set; }

    public PaperCursor Cursor => _cursor;

    public bool IsDirty => _dirty;

    public bool IsMoving => _moving || _followUp > 0 || _keysHeld > 0;

    public int Frames => _tap.Frames;

    public UISurfaceSize Size => _inner.Size;

    public double PositionX => _inner.PositionX;

    public double PositionY => _inner.PositionY;

    public bool AcceptsInput => _inner.AcceptsInput;

    public bool WantsTextInput => _paper.WantsCaptureKeyboard;

    public event Action<PaperCursor>? CursorChanged;

    public event Action<Exception>? Faulted;

    public void SetPosition(double x, double y) => _inner.SetPosition(x, y);

    public void AddObserver(IUISurfaceObserver observer) => _observers.Add(observer);

    public void RemoveObserver(IUISurfaceObserver observer) => _observers.Remove(observer);

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

    public void Invalidate()
    {
        if (_disposed || _dirty)
        {
            return;
        }

        _dirty = true;
        _host?.Wake();
    }

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

    public bool TryAcquire(out UIFrame frame) => _inner.TryAcquire(out frame);

    public bool AcceptsInputAt(double x, double y) => _inner.AcceptsInputAt(x, y);

    public string? CursorAt(double x, double y) => PaperCursors.NameOf(_cursor);

    public void NotifyPointerEnter(double x, double y) => PointerAt(x, y);

    public void NotifyPointerMotion(uint timeMs, double x, double y) => PointerAt(x, y);

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

    public void NotifyPointerAxis(uint timeMs, double dx, double dy)
    {
        if (_disposed || dy == 0)
        {
            return;
        }

        _paper.SetPointerWheel((float)(-dy / AxisPerNotch));
        Input();
    }

    public void NotifyPointerLeave()
    {
        if (_disposed || _buttonsHeld > 0)
        {
            return;
        }

        PointerAt(Outside, Outside);
    }

    public void NotifyKeyboardEnter(ReadOnlySpan<uint> pressed)
    {
        Input();
    }

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

    public void NotifyTextCommit(ReadOnlySpan<char> text)
    {
        if (_disposed)
        {
            return;
        }

        Push(text);
        Input();
    }

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

    public void NotifyTouchMotion(uint timeMs, int id, double x, double y)
    {
        if (_touchId == id)
        {
            PointerAt(x, y);
        }
    }

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

    public IUISurface? CreatePopup(in Box anchor, UIPopupGravity gravity) => null;

    public void OnSurfaceDamaged(IUISurface surface, PixmanRegion32 damage)
    {
    }

    public void OnSurfaceDestroyed(IUISurface surface)
    {
        if (ReferenceEquals(surface, _inner) && !_disposed)
        {
            Dispose();
        }
    }

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
