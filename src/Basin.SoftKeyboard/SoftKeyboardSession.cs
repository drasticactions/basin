using System.Text.Json;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Seat;

namespace Basin.SoftKeyboard;

/// <summary>
/// The part of a compositor's soft keyboard that is the same in every shell: the engine, the router, the typer, the timer, the layouts, the saved state and the show policy.
/// The session draws nothing and places nothing. Every member runs on the compositor loop.
/// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftKeyboardSession"/> class. It reads the state file and loads the layouts. An xkb layout loads only after <see cref="Attach"/>.
    /// </summary>
    /// <param name="loop">The compositor loop that owns the key timer.</param>
    /// <param name="options">The configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="loop"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Gets the engine that turns touches into keys. The consumer feeds it the touches on the keyboard.
    /// </summary>
    public SoftKeyboardEngine Engine { get; }

    /// <summary>
    /// Gets the typer that taps the engine's keys for a string. Its clock is the session's clock.
    /// </summary>
    public SoftKeyboardTyper Typer { get; }

    /// <summary>
    /// Gets the text-input fallback that the consumer installs as the input-method module's fallback before the services freeze.
    /// </summary>
    public SoftKeyboardTextInput TextInput { get; } = new();

    /// <summary>
    /// Gets the router that delivers typed keys.
    /// </summary>
    /// <exception cref="InvalidOperationException">The session is not attached.</exception>
    public KeyboardRouter Router => _router ?? throw new InvalidOperationException("the session is not attached to a keyboard");

    /// <summary>
    /// Gets a value indicating whether <see cref="Attach"/> has run.
    /// </summary>
    public bool IsAttached => _router is not null;

    /// <summary>
    /// Gets the loaded layouts, in the order of the options. A layout that cannot load is left out. The list is never empty.
    /// </summary>
    public IReadOnlyList<KeyboardLayout> Layouts => _layouts;

    /// <summary>
    /// Gets the index of the selected layout in <see cref="Layouts"/>.
    /// </summary>
    public int LayoutIndex => _layoutIndex;

    /// <summary>
    /// Gets the selected layout.
    /// </summary>
    public KeyboardLayout CurrentLayout => _layouts[Math.Clamp(_layoutIndex, 0, _layouts.Count - 1)];

    /// <summary>
    /// Gets the keyboard size: the saved size, the chosen size, or the size of the options.
    /// </summary>
    public KeyboardSize Size { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the user chose the size, now or in the state file.
    /// </summary>
    public bool SizeChosen { get; private set; }

    /// <summary>
    /// Gets the state of the focused text field.
    /// </summary>
    public FieldState Field => TextInput.Field;

    /// <summary>
    /// Gets the kind of the most recent input. It starts as <see cref="LastInput.Pointer"/>.
    /// </summary>
    public LastInput LastInput { get; private set; } = LastInput.Pointer;

    /// <summary>
    /// Gets a value indicating whether the user raised the keyboard by hand.
    /// </summary>
    public bool Manual => _manual;

    /// <summary>
    /// Gets a value indicating whether a focused field raised the keyboard under the policy.
    /// </summary>
    public bool Automatic => _auto;

    /// <summary>
    /// Gets a value indicating whether the keyboard must show: <see cref="Manual"/> or <see cref="Automatic"/>.
    /// </summary>
    public bool Wanted => _manual || _auto;

    /// <summary>
    /// Gets the show policy of the current options.
    /// </summary>
    public SoftKeyboardPolicy Policy => _options.Policy;

    /// <summary>
    /// Gets the session's clock in milliseconds. It is the typer's clock, which never runs behind a typed string.
    /// </summary>
    public long Now => Typer.Clock;

    /// <summary>
    /// Gets or sets the test that two surfaces belong to one window. The default compares the references.
    /// </summary>
    public Func<Surface, Surface, bool> SameWindow { get; set; } = static (a, b) => ReferenceEquals(a, b);

    /// <summary>
    /// Occurs when the policy runs: on a touch, a hand raise or hide, a hardware key, a field activation or deactivation, or a keyboard focus change.
    /// The arguments are <see cref="Wanted"/> and a short reason. The same value can come twice, so the handler must be idempotent.
    /// </summary>
    public event Action<bool, string>? WantedChanged;

    /// <summary>
    /// Occurs after each commit of the text-input state, once the grid has a bottom row for the field.
    /// </summary>
    public event Action? FieldChanged;

    /// <summary>
    /// Occurs before the policy runs on a field activation or deactivation, and when an input-method client comes or goes.
    /// </summary>
    public event Action? TextInputChanged;

    /// <summary>
    /// Occurs when an input-method client comes or goes, after <see cref="TextInputChanged"/>.
    /// </summary>
    public event Action? InputMethodChanged;

    /// <summary>
    /// Occurs when the seat keyboard's focus moves, with the new surface or <see langword="null"/>.
    /// </summary>
    public event Action<Surface?>? FocusChanged;

    /// <summary>
    /// Occurs after each rebuild of the engine's grid.
    /// </summary>
    public event Action? LayoutChanged;

    /// <summary>
    /// Occurs after the user or the consumer selects a layout, with that layout.
    /// </summary>
    public event Action<KeyboardLayout>? LayoutSelected;

    /// <summary>
    /// Occurs after a size is chosen, with that size.
    /// </summary>
    public event Action<KeyboardSize>? SizeChanged;

    /// <summary>
    /// Connects the session to the seat. It creates the router, builds the xkb layouts, and follows the keyboard focus and the keymap.
    /// </summary>
    /// <param name="keyboard">The seat keyboard.</param>
    /// <param name="textInputs">The text-input focus, or <see langword="null"/>. A word commit goes to it first.</param>
    /// <param name="inputMethods">The input-method clients, or <see langword="null"/> when there are none.</param>
    /// <param name="output">The key output, or <see langword="null"/> for a <see cref="SeatKeyOutput"/> over <paramref name="keyboard"/>.</param>
    /// <exception cref="InvalidOperationException">The session is already attached.</exception>
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

    /// <summary>
    /// Records the kind of the most recent input. It does not run the policy.
    /// </summary>
    /// <param name="input">The kind of input.</param>
    public void NoteInput(LastInput input) => LastInput = input;

    /// <summary>
    /// Records a pointer input. It does not run the policy.
    /// </summary>
    public void NotePointer() => LastInput = LastInput.Pointer;

    /// <summary>
    /// Tests whether a keyboard tag belongs to an input-method client.
    /// </summary>
    /// <param name="tag">The tag of the keyboard.</param>
    /// <returns><see langword="false"/> when the tag is not an input-method client or the session has no input-method clients.</returns>
    public bool IsInputMethodKeyboard(object? tag) => _clients?.IsInputMethodClient(tag) ?? false;

    /// <summary>
    /// Records a hardware key. A press clears both intents and raises <see cref="WantedChanged"/> when the keyboard was wanted.
    /// </summary>
    /// <param name="pressed"><see langword="true"/> for a press. A release does nothing.</param>
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

    /// <summary>
    /// Records a touch. A touch on the window of the active field sets the automatic intent and raises <see cref="WantedChanged"/>.
    /// </summary>
    /// <param name="surface">The surface under the touch, or <see langword="null"/> for none.</param>
    public void NoteTouch(Surface? surface)
    {
        LastInput = LastInput.Touch;
        if (surface is not null && TextInput.ActiveSurface is { } active && SameWindow(active, surface) && !_auto)
        {
            _auto = true;
            WantedChanged?.Invoke(Wanted, "touch");
        }
    }

    /// <summary>
    /// Sets the manual intent and raises <see cref="WantedChanged"/>.
    /// </summary>
    public void Raise()
    {
        _manual = true;
        WantedChanged?.Invoke(true, "manual");
    }

    /// <summary>
    /// Clears both intents and raises <see cref="WantedChanged"/> with <see langword="false"/>.
    /// </summary>
    /// <param name="why">The reason that the event carries.</param>
    public void Hide(string why)
    {
        ArgumentNullException.ThrowIfNull(why);
        _manual = false;
        _auto = false;
        WantedChanged?.Invoke(false, why);
    }

    /// <summary>
    /// Clears both intents without an event. The consumer calls it when a focus change makes the intent stale.
    /// </summary>
    public void ClearIntent()
    {
        _manual = false;
        _auto = false;
    }

    /// <summary>
    /// Tells the session that the consumer took the keyboard down. It clears the manual intent, resets the engine and clears the symbol lock.
    /// </summary>
    public void NoteHidden()
    {
        _manual = false;
        Engine.Reset();
        Engine.State.ClearSymbolLock();
    }

    /// <summary>
    /// Selects a layout, saves the choice to the state file and rebuilds the grid. An index out of range does nothing.
    /// </summary>
    /// <param name="index">The index in <see cref="Layouts"/>.</param>
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

    /// <summary>
    /// Selects the first layout with an id.
    /// </summary>
    /// <param name="id">The layout id.</param>
    /// <returns><see langword="false"/> when no loaded layout has the id.</returns>
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

    /// <summary>
    /// Sets the size as the user's choice, saves it to the state file and raises <see cref="SizeChanged"/>.
    /// </summary>
    /// <param name="size">The chosen size.</param>
    public void ChooseSize(KeyboardSize size)
    {
        Size = size;
        SizeChosen = true;
        SaveState();
        SizeChanged?.Invoke(size);
    }

    /// <summary>
    /// Reads the shape name from a layout id: <c>win8-thumb-en-us</c> gives <c>thumb</c>, and <c>xkb-full</c> gives <c>full</c>. Any other id is returned unchanged.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <returns>The shape name.</returns>
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

    /// <summary>
    /// Applies new options at run time. The layouts reload only when the layout list changes, and the size changes only when the user chose none.
    /// The state path and the locale of the new options take effect on the next layout reload.
    /// </summary>
    /// <param name="options">The new options.</param>
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

    /// <summary>
    /// Rebuilds the engine's grid or pages from the current layout and field, and raises <see cref="LayoutChanged"/>.
    /// </summary>
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

    /// <summary>
    /// Removes the key timer and stops following the seat keyboard and the input-method clients.
    /// </summary>
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
