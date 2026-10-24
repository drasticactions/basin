using System.Runtime.InteropServices;
using System.Text;
using Basin.Capabilities;
using SDL3;

namespace Basin.Sdl;

/// <summary>
/// An <see cref="ITextInputMethod"/> over the host IME through SDL text input.
/// It commits text only after a composition, because a plain key already reaches the client through the keyboard.
/// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="SdlTextInput"/> class and adds it to the loop as an event sink.
    /// </summary>
    /// <param name="loop">The SDL loop that delivers the text events.</param>
    public SdlTextInput(SdlLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        _loop = loop;
        loop.AddSink(this);
    }

    /// <summary>
    /// Gets or sets the function that gives the SDL window handle for text input. When it is <see langword="null"/>, the window with keyboard focus is used.
    /// </summary>
    public Func<nint>? WindowFor { get; set; }

    /// <summary>
    /// Gets or sets the function that converts a cursor rectangle in surface coordinates to window points.
    /// It returns <see langword="null"/> when the surface is not on the window. When the function is <see langword="null"/>, the IME gets no cursor area.
    /// </summary>
    public Func<Surface, Box, (int X, int Y, int Width, int Height)?>? AreaFor { get; set; }

    /// <summary>
    /// Gets a value indicating whether an input method can serve text input now. It is always <see langword="true"/>.
    /// </summary>
    public bool IsAvailable => true;

    /// <summary>
    /// Gets a value indicating whether the input method grabs the keyboard. It is always <see langword="false"/>.
    /// </summary>
    public bool HasKeyboardGrab => false;

    /// <summary>
    /// Gets a value indicating whether SDL text input is on for a window.
    /// </summary>
    public bool IsActive => _window != 0;

    /// <summary>
    /// Gets the number of composed texts that the bridge committed.
    /// </summary>
    public long Committed { get; private set; }

    /// <summary>
    /// Gets the number of SDL text events that the bridge dropped because no composition came before them.
    /// </summary>
    public long Dropped { get; private set; }

    /// <inheritdoc/>
    public event Action<PreeditString>? Preedit;

    /// <inheritdoc/>
    public event Action<string>? CommitString;

    /// <summary>
    /// Never occurs. SDL gives no request to delete surrounding text.
    /// </summary>
    public event Action<uint, uint>? DeleteSurroundingText
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public event Action? Done;

    /// <summary>
    /// Never occurs, because <see cref="IsAvailable"/> is always <see langword="true"/>.
    /// </summary>
    public event Action? AvailabilityChanged
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Makes a surface the active text input and starts SDL text input with the current content type.
    /// </summary>
    /// <param name="surface">The surface that owns the text input.</param>
    public void Activate(Surface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _active = surface;
        _composing = false;
        Start();
    }

    /// <summary>
    /// Stops SDL text input when the surface is the active one. Another surface is ignored.
    /// </summary>
    /// <param name="surface">The surface that owns the text input.</param>
    public void Deactivate(Surface surface)
    {
        if (!ReferenceEquals(_active, surface))
        {
            return;
        }

        _active = null;
        Stop();
    }

    /// <summary>
    /// Ignores the surrounding text, because SDL has no way to pass it to the IME.
    /// </summary>
    /// <param name="text">The surrounding text. It is not used.</param>
    /// <param name="cursor">The byte offset of the cursor. It is not used.</param>
    /// <param name="anchor">The byte offset of the selection anchor. It is not used.</param>
    public void SurroundingText(string text, uint cursor, uint anchor)
    {
    }

    /// <summary>
    /// Stores the content type for the next start of SDL text input. It maps to the SDL input type, capitalization, autocorrect and multiline.
    /// </summary>
    /// <param name="hint">The <c>zwp_text_input_v3.content_hint</c> bits.</param>
    /// <param name="purpose">The <c>zwp_text_input_v3.content_purpose</c> value.</param>
    public void ContentType(uint hint, uint purpose)
    {
        _hint = hint;
        _purpose = purpose;
    }

    /// <inheritdoc/>
    public void CursorRectangle(in Box rect) => _cursor = rect;

    /// <summary>
    /// Starts SDL text input when it is not on yet, and passes the cursor area through <see cref="AreaFor"/> to SDL.
    /// </summary>
    /// <param name="serial">The number of commits that the text input made. It is not used.</param>
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

    /// <inheritdoc/>
    public void ForwardKey(uint timeMs, uint keycode, bool pressed)
    {
    }

    /// <inheritdoc/>
    public void ForwardModifiers(uint depressed, uint latched, uint locked, uint group)
    {
    }

    /// <summary>
    /// Turns SDL text editing into preedit text, and SDL text input after a composition into committed text. It ignores both with no active surface.
    /// </summary>
    /// <param name="e">The SDL event.</param>
    /// <returns><see langword="true"/> when the bridge sent preedit or committed text.</returns>
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

    /// <summary>
    /// Maps a <c>zwp_text_input_v3</c> content purpose to an SDL text input type.
    /// </summary>
    /// <param name="purpose">The content purpose value.</param>
    /// <returns>The SDL text input type, or 0 (plain text) for a purpose with no match.</returns>
    public static long InputType(uint purpose) => purpose switch
    {
        6 => 2,
        7 => 1,
        8 => 4,
        2 or 3 or 4 => 6,
        9 => 7,
        _ => 0,
    };

    /// <summary>
    /// Maps <c>zwp_text_input_v3</c> content hint bits to an SDL capitalization mode. Uppercase wins over titlecase, and titlecase wins over auto capitalization.
    /// </summary>
    /// <param name="hint">The content hint bits.</param>
    /// <returns>The SDL capitalization mode, or 0 for none.</returns>
    public static long Capitalization(uint hint) =>
        (hint & (HintLowercase | HintUppercase | HintTitlecase | HintAutoCapitalization)) switch
        {
            var h when (h & HintUppercase) != 0 => 3,
            var h when (h & HintTitlecase) != 0 => 2,
            var h when (h & HintAutoCapitalization) != 0 => 1,
            _ => 0,
        };

    /// <summary>
    /// Leaves the loop and stops SDL text input. A second call does nothing.
    /// </summary>
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
