using System.Text.Json;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Seat;

namespace Basin.SoftKeyboard;

public sealed class SoftKeyboardSession : IDisposable
{
    private readonly List<KeyboardLayout> _layouts = [];
    private readonly BasinLogger _log;
    private SoftKeyboardSessionOptions _options;
    private SoftKeyboardTimer? _timer;
    private KeyboardRouter? _router;
    private SeatKeyboard? _keyboard;
    private ITextInputFocus? _textInputs;
    private IInputMethodClients? _clients;
    private int _layoutIndex;
    private string? _savedLayout;
    private BottomRow _bottom;
    private bool _manual;
    private bool _auto;

    public SoftKeyboardSession(ICompositorEventLoop loop, SoftKeyboardSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _log = options.Log ?? SoftKeyboardLog.Log;
        LoadState();
        LoadLayouts();
        Engine = new SoftKeyboardEngine(CurrentGrid(out var pages), options.Timing) { Diamonds = options.Diamonds };
        if (pages is not null)
        {
            Engine.Pages = pages;
        }

        Engine.State.LayoutNumLock = CurrentLayout.NumLock;
        Typer = new SoftKeyboardTyper(Engine, options.TypistTouchId);
        Typer.Pressing += () => LastInput = LastInput.Touch;
        _timer = new SoftKeyboardTimer(loop, Engine, () => Now);
        Engine.SizeChosen += ChooseSize;
        Engine.LanguageNext += () => SelectLayout((_layoutIndex + 1) % Math.Max(1, _layouts.Count));
        Engine.LanguageChosen += SelectLayout;
        Engine.ShapeRequested += ChooseShape;
    }

    public SoftKeyboardEngine Engine { get; }

    public SoftKeyboardTyper Typer { get; }

    public SoftKeyboardTextInput TextInput { get; } = new();

    public KeyboardRouter Router => _router ?? throw new InvalidOperationException("the session is not attached to a keyboard");

    public bool IsAttached => _router is not null;

    public IReadOnlyList<KeyboardLayout> Layouts => _layouts;

    public int LayoutIndex => _layoutIndex;

    public KeyboardLayout CurrentLayout => _layouts[Math.Clamp(_layoutIndex, 0, _layouts.Count - 1)];

    public KeyboardSize Size { get; private set; }

    public bool SizeChosen { get; private set; }

    public FieldState Field => TextInput.Field;

    public LastInput LastInput { get; private set; } = LastInput.Pointer;

    public bool Manual => _manual;

    public bool Automatic => _auto;

    public bool Wanted => _manual || _auto;

    public SoftKeyboardPolicy Policy => _options.Policy;

    public long Now => Typer.Clock;

    public Func<Surface, Surface, bool> SameWindow { get; set; } = static (a, b) => ReferenceEquals(a, b);

    public event Action<bool, string>? WantedChanged;

    public event Action? FieldChanged;

    public event Action? TextInputChanged;

    public event Action? InputMethodChanged;

    public event Action<Surface?>? FocusChanged;

    public event Action? LayoutChanged;

    public event Action<KeyboardLayout>? LayoutSelected;

    public event Action<KeyboardSize>? SizeChanged;

    public void Attach(SeatKeyboard keyboard, ITextInputFocus? textInputs, IInputMethodClients? inputMethods, ISoftKeyOutput? output = null)
    {
        ArgumentNullException.ThrowIfNull(keyboard);
        if (_router is not null)
        {
            throw new InvalidOperationException("the session is already attached");
        }

        _keyboard = keyboard;
        _textInputs = textInputs;
        _clients = inputMethods;
        keyboard.FocusChanged += OnKeyboardFocus;
        keyboard.KeymapChanged += OnKeymap;
        if (inputMethods is not null)
        {
            inputMethods.AvailabilityChanged += OnInputMethodAvailability;
        }

        if (_options.Layouts.Any(IsXkb))
        {
            LoadLayouts();
            Rebuild();
        }

        _router = new KeyboardRouter(TextInput, output ?? new SeatKeyOutput(keyboard))
        {
            SpacesToPeriod = _options.SpacesToPeriod,
            Commit = textInputs,
        };
        _router.Attach(Engine, () => Now);
        _router.Sent += (_, _) => LastInput = LastInput.Touch;
        TextInput.Activated += OnActivated;
        TextInput.Deactivated += OnDeactivated;
        TextInput.FieldChanged += OnFieldChanged;
    }

    public void NoteInput(LastInput input) => LastInput = input;

    public void NotePointer() => LastInput = LastInput.Pointer;

    public bool IsInputMethodKeyboard(object? tag) => _clients?.IsInputMethodClient(tag) ?? false;

    public void NoteHardwareKey(bool pressed)
    {
        if (!pressed)
        {
            return;
        }

        LastInput = LastInput.Keyboard;
        if (_manual || _auto)
        {
            _manual = false;
            _auto = false;
            WantedChanged?.Invoke(false, "key");
        }
    }

    public void NoteTouch(Surface? surface)
    {
        LastInput = LastInput.Touch;
        if (surface is not null && TextInput.ActiveSurface is { } active && SameWindow(active, surface) && !_auto)
        {
            _auto = true;
            WantedChanged?.Invoke(Wanted, "touch");
        }
    }

    public void Raise()
    {
        _manual = true;
        WantedChanged?.Invoke(true, "manual");
    }

    public void Hide(string why)
    {
        ArgumentNullException.ThrowIfNull(why);
        _manual = false;
        _auto = false;
        WantedChanged?.Invoke(false, why);
    }

    public void ClearIntent()
    {
        _manual = false;
        _auto = false;
    }

    public void NoteHidden()
    {
        _manual = false;
        Engine.Reset();
        Engine.State.ClearSymbolLock();
    }

    public void SelectLayout(int index)
    {
        if (index < 0 || index >= _layouts.Count)
        {
            return;
        }

        _layoutIndex = index;
        _savedLayout = CurrentLayout.Id;
        SaveState();
        Rebuild();
        LayoutSelected?.Invoke(CurrentLayout);
    }

    public bool SelectLayout(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var index = _layouts.FindIndex(layout => layout.Id == id);
        if (index < 0)
        {
            return false;
        }

        SelectLayout(index);
        return true;
    }

    public void ChooseSize(KeyboardSize size)
    {
        Size = size;
        SizeChosen = true;
        SaveState();
        SizeChanged?.Invoke(size);
    }

    public static string ShapeOf(KeyboardLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var id = layout.Id;
        if (id.StartsWith("win8-", StringComparison.Ordinal))
        {
            var end = id.IndexOf('-', 5);
            return end > 5 ? id[5..end] : id[5..];
        }

        return id.StartsWith("xkb-", StringComparison.Ordinal) ? id[4..] : id;
    }

    public void Reload(SoftKeyboardSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var previous = _options;
        _options = options;
        Engine.Timing = options.Timing;
        Engine.Diamonds = options.Diamonds;
        if (_router is not null)
        {
            _router.SpacesToPeriod = options.SpacesToPeriod;
        }

        if (!options.Layouts.SequenceEqual(previous.Layouts))
        {
            LoadLayouts();
            Rebuild();
        }

        if (!SizeChosen)
        {
            Size = options.Size;
        }
    }

    public void Rebuild()
    {
        var grid = CurrentGrid(out var pages);
        if (pages is not null)
        {
            Engine.Pages = pages;
        }
        else
        {
            Engine.Grid = grid;
        }

        Engine.State.LayoutNumLock = CurrentLayout.NumLock;
        LayoutChanged?.Invoke();
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        if (_keyboard is { } keyboard)
        {
            keyboard.FocusChanged -= OnKeyboardFocus;
            keyboard.KeymapChanged -= OnKeymap;
        }

        if (_clients is { } clients)
        {
            clients.AvailabilityChanged -= OnInputMethodAvailability;
        }

        _keyboard = null;
        _clients = null;
    }

    private static bool IsXkb(string id) =>
        id is KeyboardLayouts.Xkb or SoftKeyboardSessionOptions.XkbStandard or SoftKeyboardSessionOptions.XkbFull;

    private void ChooseShape(string? shape)
    {
        if (_layouts.Count < 2)
        {
            return;
        }

        if (shape is null)
        {
            SelectLayout((_layoutIndex + 1) % _layouts.Count);
            return;
        }

        var index = _layouts.FindIndex(layout => ShapeOf(layout) == shape);
        if (index >= 0 && index != _layoutIndex)
        {
            SelectLayout(index);
        }
    }

    private void OnKeyboardFocus(Surface? surface)
    {
        _textInputs?.NotifyFocus(surface);
        FocusChanged?.Invoke(surface);
        WantedChanged?.Invoke(Wanted, "focus");
    }

    private void OnInputMethodAvailability()
    {
        TextInput.ExternalInputMethod = _clients?.HasInputMethodClient ?? false;
        TextInputChanged?.Invoke();
        InputMethodChanged?.Invoke();
    }

    private void OnActivated(Surface surface)
    {
        TextInputChanged?.Invoke();
        if (_options.Policy == SoftKeyboardPolicy.OnAnyFocus || LastInput == LastInput.Touch)
        {
            _auto = true;
        }

        WantedChanged?.Invoke(Wanted, "activate");
    }

    private void OnDeactivated()
    {
        TextInputChanged?.Invoke();
        _auto = false;
        WantedChanged?.Invoke(Wanted, "deactivate");
    }

    private void OnFieldChanged(FieldState field)
    {
        if (field.BottomRow != _bottom)
        {
            Rebuild();
        }

        FieldChanged?.Invoke();
    }

    private void OnKeymap()
    {
        if (_options.Layouts.Count(IsXkb) > _layouts.Count(static layout => IsXkb(layout.Id)))
        {
            LoadLayouts();
            Rebuild();
            return;
        }

        var changed = false;
        for (var i = 0; i < _layouts.Count; i++)
        {
            if (!IsXkb(_layouts[i].Id) || BuildXkb(_layouts[i].Id) is not { } rebuilt)
            {
                continue;
            }

            _layouts[i] = rebuilt;
            changed |= i == _layoutIndex;
        }

        if (changed)
        {
            Rebuild();
        }
    }

    private KeyGrid CurrentGrid(out KeyPageSet? pages)
    {
        IReadOnlyList<KeyCap>? languages = null;
        if (_layouts.Count >= 2 && !CurrentLayout.IsShaped)
        {
            var caps = new KeyCap[_layouts.Count];
            for (var i = 0; i < caps.Length; i++)
            {
                caps[i] = KeyCap.LanguageChoice(i, _layouts[i].LanguageLabel);
            }

            languages = caps;
        }

        _bottom = TextInput.Field.BottomRow;
        var layout = CurrentLayout;
        if (layout.PageNames.Count > 0)
        {
            pages = layout.PageSet(_bottom, languages, layout.LanguageLabel);
            return pages.Main;
        }

        pages = null;
        return layout.Grid(_bottom, languages, layout.LanguageLabel);
    }

    private void LoadLayouts()
    {
        _layouts.Clear();
        var ids = _options.Layouts.Count > 0 ? _options.Layouts : [KeyboardLayouts.DefaultFor(_options.Locale)];
        foreach (var id in ids)
        {
            if (IsXkb(id) && _keyboard is null)
            {
                continue;
            }

            var layout = IsXkb(id) ? BuildXkb(id) : KeyboardLayouts.Load(id, _options.UserLayoutDirectory, _log);
            if (layout is null)
            {
                _log.Warn($"keyboard: layout '{id}' cannot be loaded");
                continue;
            }

            _layouts.Add(layout);
        }

        if (_layouts.Count == 0)
        {
            _layouts.Add(KeyboardLayouts.Load("us-qwerty")!);
        }

        _layoutIndex = 0;
        if (_savedLayout is { } saved)
        {
            var index = _layouts.FindIndex(layout => layout.Id == saved);
            if (index >= 0)
            {
                _layoutIndex = index;
            }
        }
    }

    private KeyboardLayout? BuildXkb(string id)
    {
        if (_keyboard?.Keymap is not { } keymap)
        {
            return null;
        }

        var layout = _keyboard.State?.SerializeLayout(Xkb.XkbStateComponent.LayoutEffective) ?? 0;
        return id switch
        {
            SoftKeyboardSessionOptions.XkbStandard => XkbGrid.Standard(keymap, layout),
            SoftKeyboardSessionOptions.XkbFull => XkbGrid.Full(keymap, layout),
            _ => KeyboardLayouts.Load("us-qwerty") is { } symbols ? XkbGrid.Build(keymap, layout, symbols) : null,
        };
    }

    private void LoadState()
    {
        Size = _options.Size;
        SizeChosen = false;
        _savedLayout = null;
        if (_options.StatePath is not { } path)
        {
            return;
        }

        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            var file = JsonSerializer.Deserialize(File.ReadAllText(path), SoftKeyboardJsonContext.Default.SoftKeyboardStateFile);
            if (file?.Size is { } size)
            {
                Size = KeyboardSizes.Parse(size, Size);
                SizeChosen = true;
            }

            _savedLayout = file?.Layout;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            _log.Warn($"keyboard: cannot read the state file: {error.Message}");
        }
    }

    private void SaveState()
    {
        if (_options.StatePath is not { } path)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var file = new SoftKeyboardStateFile
            {
                Size = SizeChosen ? KeyboardSizes.Name(Size) : null,
                Layout = _savedLayout,
            };
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(file, SoftKeyboardJsonContext.Default.SoftKeyboardStateFile));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"keyboard: cannot write the state file: {error.Message}");
        }
    }
}
