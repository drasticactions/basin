using System.Text;
using Xkb;

namespace Basin.SoftKeyboard;

/// <summary>
/// Delivers typed soft keys by one of three routes: the active backend, the text input of the focused client, or synthesized keys.
/// It also turns a fast second Space into a period and a space.
/// </summary>
public sealed class KeyboardRouter
{
    /// <summary>
    /// The evdev code of Backspace.
    /// </summary>
    public const uint BackspaceCode = 14;
    /// <summary>
    /// The evdev code of Tab.
    /// </summary>
    public const uint TabCode = 15;
    /// <summary>
    /// The evdev code of Return.
    /// </summary>
    public const uint ReturnCode = 28;
    /// <summary>
    /// The evdev code of the Left arrow.
    /// </summary>
    public const uint LeftCode = 105;
    /// <summary>
    /// The evdev code of the Right arrow.
    /// </summary>
    public const uint RightCode = 106;
    /// <summary>
    /// The evdev code of Escape.
    /// </summary>
    public const uint EscapeCode = 1;
    /// <summary>
    /// The evdev code of the Up arrow.
    /// </summary>
    public const uint UpCode = 103;
    /// <summary>
    /// The evdev code of the Down arrow.
    /// </summary>
    public const uint DownCode = 108;
    /// <summary>
    /// The evdev code of Home.
    /// </summary>
    public const uint HomeCode = 102;
    /// <summary>
    /// The evdev code of End.
    /// </summary>
    public const uint EndCode = 107;
    /// <summary>
    /// The evdev code of Page Up.
    /// </summary>
    public const uint PageUpCode = 104;
    /// <summary>
    /// The evdev code of Page Down.
    /// </summary>
    public const uint PageDownCode = 109;
    /// <summary>
    /// The evdev code of Delete.
    /// </summary>
    public const uint DeleteCode = 111;

    private const uint XkbOffset = 8;
    private const uint ShiftMask = 1;
    private const uint ControlMask = 4;
    private const uint AltMask = 8;
    private const uint SuperMask = 64;
    private const string NoPeriodAfter = ".,;:!?";

    private readonly SoftKeyboardTextInput _textInput;
    private readonly ISoftKeyOutput _output;
    private readonly KeyTiming _timing;
    private bool _spaceArmed;
    private long _lastSpace;
    private char _lastKey = 'a';

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyboardRouter"/> class. A field change resets the double-space state.
    /// </summary>
    /// <param name="textInput">The text-input fallback that gives the field and takes text commits.</param>
    /// <param name="output">The key output for the keys route.</param>
    /// <param name="timing">The timing constants, or <see langword="null"/> for <see cref="KeyTiming.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="textInput"/> or <paramref name="output"/> is <see langword="null"/>.</exception>
    public KeyboardRouter(SoftKeyboardTextInput textInput, ISoftKeyOutput output, KeyTiming? timing = null)
    {
        ArgumentNullException.ThrowIfNull(textInput);
        ArgumentNullException.ThrowIfNull(output);
        _textInput = textInput;
        _output = output;
        _timing = timing ?? KeyTiming.Default;
        textInput.FieldChanged += _ => ResetShortcuts();
    }

    /// <summary>
    /// Gets or sets the backend that takes every key while it is active, or <see langword="null"/> for none.
    /// </summary>
    public IKeyboardBackend? Backend { get; set; }

    /// <summary>
    /// Gets or sets the direct commit that <see cref="CommitWord"/> tries first, or <see langword="null"/> for none.
    /// </summary>
    public Basin.Capabilities.ITextInputCommit? Commit { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a second Space within <see cref="KeyTiming.DoubleSpaceMs"/> types a period. It applies only in a text field or with no field.
    /// </summary>
    public bool SpacesToPeriod { get; set; } = true;

    /// <summary>
    /// Gets the route that a key takes now: the backend when it is active, then the text input when a field is active, then keys.
    /// </summary>
    public KeyRoute Route => Backend is { IsActive: true }
        ? KeyRoute.Backend
        : _textInput.IsActive ? KeyRoute.TextInput : KeyRoute.Keys;

    /// <summary>
    /// Occurs after <see cref="Send"/> delivers a key, with the key and the route it took.
    /// </summary>
    public event Action<TypedKey, KeyRoute>? Sent;

    /// <summary>
    /// Connects an engine: each typed key goes out through <see cref="Send"/>, and each field change sets auto-cap and the number lock and clears the symbol lock.
    /// </summary>
    /// <param name="engine">The engine to follow.</param>
    /// <param name="clock">The clock in milliseconds for the sent keys, or <see langword="null"/> for <see cref="Environment.TickCount64"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="engine"/> is <see langword="null"/>.</exception>
    public void Attach(SoftKeyboardEngine engine, Func<long>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var now = clock ?? (static () => Environment.TickCount64);
        engine.Typed += key => Send(key, now());
        _textInput.FieldChanged += field =>
        {
            engine.State.SetAutoCap(field.Active && field.AutoCap);
            engine.State.NumLockField = field.NumLock;
            engine.State.ClearSymbolLock();
        };
    }

    /// <summary>
    /// Delivers one typed key by the current route. A key with modifiers goes as a chord, never as text.
    /// </summary>
    /// <param name="key">The typed key.</param>
    /// <param name="now">The time in milliseconds.</param>
    public void Send(in TypedKey key, long now)
    {
        var route = Route;
        if (key.Modifiers != KeyModifiers.None && (key.Cap.IsText || key.Cap.IsKeystroke))
        {
            Chord(route, key, (uint)now);
            Sent?.Invoke(key, route);
            return;
        }

        if (key.Cap.IsText && !key.Cap.IsShortcut && DoubleSpace(key, now))
        {
            Emit(route, new TypedKey(KeyCap.Backspace, false, false, false, false), (uint)now);
            Text(route, ". ", (uint)now);
            _lastKey = '\0';
            _spaceArmed = false;
            Sent?.Invoke(key, route);
            return;
        }

        Emit(route, key, (uint)now);
        Sent?.Invoke(key, route);
    }

    /// <summary>
    /// Commits a whole word, for example from the trace decoder. It tries <see cref="Commit"/> first, then the text input, then keys. It never uses the backend.
    /// </summary>
    /// <param name="word">The word to commit.</param>
    /// <param name="now">The time in milliseconds.</param>
    /// <returns>The route that took the word.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="word"/> is <see langword="null"/>.</exception>
    public KeyRoute CommitWord(string word, long now)
    {
        ArgumentNullException.ThrowIfNull(word);
        ResetShortcuts();
        if (Commit?.TryCommitString(word) == true)
        {
            return KeyRoute.TextInput;
        }

        var route = _textInput.IsActive ? KeyRoute.TextInput : KeyRoute.Keys;
        Text(route, word, (uint)now);
        return route;
    }

    /// <summary>
    /// Takes back a word that <see cref="CommitWord"/> committed: a surrounding-text delete of its UTF-8 length on the text input, otherwise one Backspace for each character.
    /// </summary>
    /// <param name="committed">The committed word. An empty string does nothing.</param>
    /// <param name="now">The time in milliseconds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="committed"/> is <see langword="null"/>.</exception>
    public void DeleteCommitted(string committed, long now)
    {
        ArgumentNullException.ThrowIfNull(committed);
        if (committed.Length == 0)
        {
            return;
        }

        if (_textInput.IsActive)
        {
            _textInput.Delete((uint)Encoding.UTF8.GetByteCount(committed), 0);
            return;
        }

        var backspace = new TypedKey(KeyCap.Backspace, false, false, false, false);
        var route = Route;
        for (var i = 0; i < committed.EnumerateRunes().Count(); i++)
        {
            Emit(route, backspace, (uint)now);
        }
    }

    private void Emit(KeyRoute route, in TypedKey key, uint time)
    {
        var cap = key.Cap;
        if (cap.IsText)
        {
            Text(route, key.Text, time);
            return;
        }

        var (code, keysym) = Keystroke(cap);
        if (code == 0)
        {
            return;
        }

        if (cap.Function == KeyFunction.Backspace)
        {
            _lastKey = 'a';
            _spaceArmed = false;
        }
        else
        {
            _lastKey = '\0';
            _spaceArmed = false;
        }

        switch (route)
        {
            case KeyRoute.Backend:
                var state = key.WordDelete ? ControlMask : key.ShiftHeld ? ShiftMask : 0;
                Backend!.ProcessKey(keysym, code + XkbOffset, state, release: false, time);
                Backend.ProcessKey(keysym, code + XkbOffset, state, release: true, time);
                break;
            case KeyRoute.TextInput when key.WordDelete && _textInput.Surrounding is not null:
                DeleteWord();
                break;
            default:
                if (key.WordDelete)
                {
                    _output.ControlKey(code, time);
                }
                else
                {
                    _output.Key(code, time);
                }

                break;
        }
    }

    /// <summary>
    /// Gives the evdev code and the keysym of a cap that goes out as a key.
    /// </summary>
    /// <param name="cap">The cap.</param>
    /// <returns>The code and the keysym, or two zeros for a cap that has no key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cap"/> is <see langword="null"/>.</exception>
    public static (uint Code, uint Keysym) Keystroke(KeyCap cap)
    {
        ArgumentNullException.ThrowIfNull(cap);
        return cap.Function switch
        {
            KeyFunction.Backspace => (BackspaceCode, 0xff08u),
            KeyFunction.Return => (ReturnCode, 0xff0du),
            KeyFunction.Tab => (TabCode, 0xff09u),
            KeyFunction.Left => (LeftCode, 0xff51u),
            KeyFunction.Right => (RightCode, 0xff53u),
            KeyFunction.Escape => (EscapeCode, 0xff1bu),
            KeyFunction.Up => (UpCode, 0xff52u),
            KeyFunction.Down => (DownCode, 0xff54u),
            KeyFunction.Home => (HomeCode, 0xff50u),
            KeyFunction.End => (EndCode, 0xff57u),
            KeyFunction.PageUp => (PageUpCode, 0xff55u),
            KeyFunction.PageDown => (PageDownCode, 0xff56u),
            KeyFunction.Delete => (DeleteCode, 0xffffu),
            KeyFunction.FunctionKey when cap.Argument is >= 1 and <= 10 => (58u + (uint)cap.Argument, 0xffbdu + (uint)cap.Argument),
            KeyFunction.FunctionKey when cap.Argument is 11 or 12 => (76u + (uint)cap.Argument, 0xffbdu + (uint)cap.Argument),
            _ => (0u, 0u),
        };
    }

    private void Chord(KeyRoute route, in TypedKey key, uint time)
    {
        _lastKey = '\0';
        _spaceArmed = false;
        var modifiers = key.Modifiers;
        uint code;
        uint keysym;
        uint resolved = 0;
        if (key.Cap.IsText)
        {
            Rune.DecodeFromUtf16(key.Text, out var rune, out _);
            keysym = XkbKeysym.FromUtf32((uint)rune.Value).Value;
            if (!_output.TryResolve(keysym, out code, out resolved))
            {
                return;
            }
        }
        else
        {
            (code, keysym) = Keystroke(key.Cap);
            if (code == 0)
            {
                return;
            }
        }

        if (route == KeyRoute.Backend)
        {
            var state = resolved;
            state |= (modifiers & KeyModifiers.Shift) != 0 ? ShiftMask : 0;
            state |= (modifiers & KeyModifiers.Control) != 0 ? ControlMask : 0;
            state |= (modifiers & KeyModifiers.Alt) != 0 ? AltMask : 0;
            state |= (modifiers & KeyModifiers.Super) != 0 ? SuperMask : 0;
            Backend!.ProcessKey(keysym, code + XkbOffset, state, release: false, time);
            Backend.ProcessKey(keysym, code + XkbOffset, state, release: true, time);
            return;
        }

        _output.ModifiedKey(code, resolved, modifiers, time);
    }

    private void Text(KeyRoute route, string text, uint time)
    {
        switch (route)
        {
            case KeyRoute.Backend:
                foreach (var rune in text.EnumerateRunes())
                {
                    var keysym = XkbKeysym.FromUtf32((uint)rune.Value).Value;
                    var keycode = _output.TryResolve(keysym, out var evdev, out var modifiers) ? evdev + XkbOffset : 0;
                    Backend!.ProcessKey(keysym, keycode, modifiers, release: false, time);
                    Backend.ProcessKey(keysym, keycode, modifiers, release: true, time);
                }

                break;
            case KeyRoute.TextInput:
                _textInput.Send(text);
                break;
            default:
                _output.Type(text, time);
                break;
        }
    }

    private void DeleteWord()
    {
        var before = FieldState.BeforeCursor(_textInput.Surrounding!, _textInput.Cursor);
        var end = before.Length;
        while (end > 0 && char.IsWhiteSpace(before[end - 1]))
        {
            end--;
        }

        while (end > 0 && !char.IsWhiteSpace(before[end - 1]))
        {
            end--;
        }

        var bytes = Encoding.UTF8.GetByteCount(before[end..]);
        if (bytes > 0)
        {
            _textInput.Delete((uint)bytes, 0);
        }
    }

    private bool DoubleSpace(in TypedKey key, long now)
    {
        if (!SpacesToPeriod || (_textInput.Field.Active && _textInput.Field.Kind != FieldKind.Text))
        {
            return false;
        }

        var space = key.Cap.IsSpace;
        if (space && _spaceArmed)
        {
            var fire = _lastSpace + _timing.DoubleSpaceMs > now;
            _spaceArmed = false;
            _lastKey = '\0';
            return fire;
        }

        if (space && _lastKey != '\0' && NoPeriodAfter.IndexOf(_lastKey) < 0)
        {
            _spaceArmed = true;
            _lastSpace = now;
        }
        else
        {
            _spaceArmed = false;
            _lastKey = space || key.Text.Length != 1 ? '\0' : key.Text[0];
        }

        return false;
    }

    private void ResetShortcuts()
    {
        _spaceArmed = false;
        _lastKey = 'a';
    }
}
