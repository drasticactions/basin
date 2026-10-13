using System.Text;
using Xkb;

namespace Basin.SoftKeyboard;

public sealed class KeyboardRouter
{
    public const uint BackspaceCode = 14;
    public const uint TabCode = 15;
    public const uint ReturnCode = 28;
    public const uint LeftCode = 105;
    public const uint RightCode = 106;
    public const uint EscapeCode = 1;
    public const uint UpCode = 103;
    public const uint DownCode = 108;
    public const uint HomeCode = 102;
    public const uint EndCode = 107;
    public const uint PageUpCode = 104;
    public const uint PageDownCode = 109;
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

    public KeyboardRouter(SoftKeyboardTextInput textInput, ISoftKeyOutput output, KeyTiming? timing = null)
    {
        ArgumentNullException.ThrowIfNull(textInput);
        ArgumentNullException.ThrowIfNull(output);
        _textInput = textInput;
        _output = output;
        _timing = timing ?? KeyTiming.Default;
        textInput.FieldChanged += _ => ResetShortcuts();
    }

    public IKeyboardBackend? Backend { get; set; }

    public Basin.Capabilities.ITextInputCommit? Commit { get; set; }

    public bool SpacesToPeriod { get; set; } = true;

    public KeyRoute Route => Backend is { IsActive: true }
        ? KeyRoute.Backend
        : _textInput.IsActive ? KeyRoute.TextInput : KeyRoute.Keys;

    public event Action<TypedKey, KeyRoute>? Sent;

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
