using System.Runtime.InteropServices;
using System.Text;
using Basin.Capabilities;
using SDL3;

namespace Basin.Sdl;

public sealed class SdlTextInput : ITextInputMethod, ISdlEventSink, IDisposable
{
    private const uint HintAutoCapitalization = 0x4;
    private const uint HintLowercase = 0x8;
    private const uint HintUppercase = 0x10;
    private const uint HintTitlecase = 0x20;
    private const uint HintMultiline = 0x200;
    private const uint HintCompletion = 0x1;
    private const uint HintSpellcheck = 0x2;

    private readonly SdlLoop _loop;
    private Surface? _active;
    private nint _window;
    private Box _cursor;
    private uint _hint;
    private uint _purpose;
    private bool _composing;
    private bool _disposed;

    public SdlTextInput(SdlLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        _loop = loop;
        loop.AddSink(this);
    }

    public Func<nint>? WindowFor { get; set; }

    public Func<Surface, Box, (int X, int Y, int Width, int Height)?>? AreaFor { get; set; }

    public bool IsAvailable => true;

    public bool HasKeyboardGrab => false;

    public bool IsActive => _window != 0;

    public long Committed { get; private set; }

    public long Dropped { get; private set; }

    public event Action<PreeditString>? Preedit;

    public event Action<string>? CommitString;

    public event Action<uint, uint>? DeleteSurroundingText
    {
        add { }
        remove { }
    }

    public event Action? Done;

    public event Action? AvailabilityChanged
    {
        add { }
        remove { }
    }

    public void Activate(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _active = surface;
        _composing = false;
        Start();
    }

    public void Deactivate(Surface surface)
    {
        if (!ReferenceEquals(_active, surface))
        {
            return;
        }

        _active = null;
        Stop();
    }

    public void SurroundingText(string text, uint cursor, uint anchor)
    {
    }

    public void ContentType(uint hint, uint purpose)
    {
        _hint = hint;
        _purpose = purpose;
    }

    public void CursorRectangle(in Box rect) => _cursor = rect;

    public void Commit(uint serial)
    {
        if (_active is null)
        {
            return;
        }

        if (_window == 0)
        {
            Start();
        }

        if (_window != 0 && AreaFor?.Invoke(_active, _cursor) is { } area)
        {
            var rect = new SDL.Rect { X = area.X, Y = area.Y, W = Math.Max(1, area.Width), H = Math.Max(1, area.Height) };
            _ = SDL.SetTextInputArea(_window, in rect, 0);
        }
    }

    public void ForwardKey(uint timeMs, uint keycode, bool pressed)
    {
    }

    public void ForwardModifiers(uint depressed, uint latched, uint locked, uint group)
    {
    }

    public bool Handle(in SDL.Event e)
    {
        switch ((SDL.EventType)e.Type)
        {
            case SDL.EventType.TextEditing:
                if (_active is null)
                {
                    return false;
                }

                var preedit = Marshal.PtrToStringUTF8(e.Edit.Text) ?? string.Empty;
                if (preedit.Length > 0)
                {
                    _composing = true;
                }

                var caret = Math.Clamp(e.Edit.Start, 0, preedit.Length);
                var bytes = Encoding.UTF8.GetByteCount(preedit.AsSpan(0, caret));
                Preedit?.Invoke(new PreeditString(preedit, bytes, bytes));
                Done?.Invoke();
                return true;
            case SDL.EventType.TextInput:
                if (_active is null)
                {
                    return false;
                }

                if (!_composing)
                {
                    Dropped++;
                    return false;
                }

                _composing = false;
                var text = Marshal.PtrToStringUTF8(e.Text.Text) ?? string.Empty;
                Preedit?.Invoke(new PreeditString(string.Empty, 0, 0));
                CommitString?.Invoke(text);
                Done?.Invoke();
                Committed++;
                return true;
            default:
                return false;
        }
    }

    private void Start()
    {
        var window = WindowFor?.Invoke() ?? SDL.GetKeyboardFocus();
        if (window == 0)
        {
            return;
        }

        if (_window != 0 && _window != window)
        {
            _ = SDL.StopTextInput(_window);
        }

        var props = SDL.CreateProperties();
        try
        {
            _ = SDL.SetNumberProperty(props, SDL.Props.TextInputTypeNumber, InputType(_purpose));
            _ = SDL.SetNumberProperty(props, SDL.Props.TextInputCapitalizationNumber, Capitalization(_hint));
            _ = SDL.SetBooleanProperty(props, SDL.Props.TextInputAutoCorrectBoolean, (_hint & (HintCompletion | HintSpellcheck)) != 0);
            _ = SDL.SetBooleanProperty(props, SDL.Props.TextInputMultilineBoolean, (_hint & HintMultiline) != 0);
            if (SDL.StartTextInputWithProperties(window, props))
            {
                _window = window;
            }
        }
        finally
        {
            SDL.DestroyProperties(props);
        }
    }

    private void Stop()
    {
        _composing = false;
        if (_window != 0)
        {
            _ = SDL.StopTextInput(_window);
            _window = 0;
        }
    }

    public static long InputType(uint purpose) => purpose switch
    {
        6 => 2,
        7 => 1,
        8 => 4,
        2 or 3 or 4 => 6,
        9 => 7,
        _ => 0,
    };

    public static long Capitalization(uint hint) =>
        (hint & (HintLowercase | HintUppercase | HintTitlecase | HintAutoCapitalization)) switch
        {
            var h when (h & HintUppercase) != 0 => 3,
            var h when (h & HintTitlecase) != 0 => 2,
            var h when (h & HintAutoCapitalization) != 0 => 1,
            _ => 0,
        };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loop.RemoveSink(this);
        Stop();
    }
}
