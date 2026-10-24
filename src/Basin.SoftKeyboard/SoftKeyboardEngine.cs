using System.Numerics;

namespace Basin.SoftKeyboard;

/// <summary>
/// Turns touches in keyboard coordinates into keys: presses, slides, rollover, repeats, long-press lists and traces. It draws nothing.
/// Every method that depends on time takes the time as an argument. A press, a move, a release and a repeat tick allocate nothing.
/// </summary>
public sealed class SoftKeyboardEngine
{
    /// <summary>
    /// The largest number of touches that the engine tracks at the same time. A further touch is ignored.
    /// </summary>
    public const int MaxTouches = 10;
    /// <summary>
    /// The cell index that means no cell.
    /// </summary>
    public const int NoCell = -1;
    /// <summary>
    /// The largest number of points in a trace path. Later points are dropped.
    /// </summary>
    public const int TraceCapacity = 1024;

    private readonly TouchSlot[] _touches = new TouchSlot[MaxTouches];
    private readonly KeyHitTester _hit;
    private int _repeatCell = NoCell;
    private long _repeatStart;
    private long _repeatDeadline = long.MaxValue;
    private int _repeatInterval;
    private long _lastTouchBegin = long.MinValue / 2;
    private int _longPressCell = NoCell;
    private IReadOnlyList<KeyCap>? _longPress;
    private LongPressFrame _longPressFrame;
    private int _longPressHighlight = LongPressFrame.Nothing;
    private float _padding;
    private readonly Vector2[] _trace = new Vector2[TraceCapacity];
    private int _traceCount;
    private int _traceSlot = -1;
    private KeyTiming _timing;
    private KeyPageSet? _pages;
    private string _page = KeyPageSet.MainPage;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftKeyboardEngine"/> class. Call <see cref="Layout"/> before the first touch.
    /// </summary>
    /// <param name="grid">The grid to show.</param>
    /// <param name="timing">The timing constants, or <see langword="null"/> for <see cref="KeyTiming.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is <see langword="null"/>.</exception>
    public SoftKeyboardEngine(KeyGrid grid, KeyTiming? timing = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        _timing = timing ?? KeyTiming.Default;
        State = new KeyboardState(_timing);
        _hit = new KeyHitTester(grid);
    }

    /// <summary>
    /// Gets or sets the timing constants of the engine and its state. A change takes effect on the next press and the next Shift tap.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public KeyTiming Timing
    {
        get => _timing;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _timing = value;
            State.Timing = value;
        }
    }

    /// <summary>
    /// Gets the shift and symbol state machine.
    /// </summary>
    public KeyboardState State { get; }

    /// <summary>
    /// Gets the hit tester of the current grid and layout.
    /// </summary>
    public KeyHitTester HitTester => _hit;

    /// <summary>
    /// Gets or sets the grid that shows now. Setting it clears <see cref="Pages"/>, closes a long-press list and stops a repeat.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public KeyGrid Grid
    {
        get => _hit.Grid;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _pages = null;
            _page = KeyPageSet.MainPage;
            SwapGrid(value);
        }
    }

    /// <summary>
    /// Gets or sets the page set, or <see langword="null"/> when the grid has no pages. Setting it shows the main page and raises <see cref="PageChanged"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public KeyPageSet? Pages
    {
        get => _pages;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _pages = value;
            _page = KeyPageSet.MainPage;
            SwapGrid(value.Main);
            PageChanged?.Invoke(_page);
        }
    }

    /// <summary>
    /// Gets the name of the page that shows now. It is <see cref="KeyPageSet.MainPage"/> without pages.
    /// </summary>
    public string Page => _page;

    /// <summary>
    /// Gets or sets a value indicating whether the hit test applies the row-edge correction between rows.
    /// </summary>
    public bool Diamonds { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether every long press waits <see cref="KeyTiming.SlowLongPressMs"/>. A consumer turns it on with trace typing.
    /// </summary>
    public bool SlowLongPress { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a lone touch that starts on a letter and travels past <see cref="KeyTiming.TraceThreshold"/> becomes a trace.
    /// </summary>
    public bool TraceEnabled { get; set; }

    /// <summary>
    /// Gets a value indicating whether a trace is in progress.
    /// </summary>
    public bool IsTracing => _traceSlot >= 0;

    /// <summary>
    /// Gets the points of the current or the last trace, in keyboard coordinates. Points closer than 2 pixels to the previous one are skipped.
    /// </summary>
    public ReadOnlySpan<Vector2> TracePath => _trace.AsSpan(0, _traceCount);

    /// <summary>
    /// Gets the keyboard width of the last layout, in logical pixels.
    /// </summary>
    public float Width => _hit.Width;

    /// <summary>
    /// Gets the height of the key rows of the last layout, in logical pixels.
    /// </summary>
    public float KeymapHeight => _hit.Height;

    /// <summary>
    /// Gets the padding above the key rows of the last layout. A touch in it has a negative y.
    /// </summary>
    public float Padding => _padding;

    /// <summary>
    /// Gets the rect of each cell of the current grid, by cell index.
    /// </summary>
    public ReadOnlySpan<KeyRect> Rects => _hit.Rects;

    /// <summary>
    /// Gets a value indicating whether a long-press list is open.
    /// </summary>
    public bool IsLongPressOpen => _longPress is not null;

    /// <summary>
    /// Gets the cell whose long-press list is open, or <see cref="NoCell"/>.
    /// </summary>
    public int LongPressCell => _longPressCell;

    /// <summary>
    /// Gets the entries of the open long-press list, or <see langword="null"/>.
    /// </summary>
    public IReadOnlyList<KeyCap>? LongPressEntries => _longPress;

    /// <summary>
    /// Gets the geometry of the open long-press list. The value is stale when no list is open.
    /// </summary>
    public LongPressFrame LongPressGeometry => _longPressFrame;

    /// <summary>
    /// Gets the highlighted entry of the open list, or <see cref="LongPressFrame.Nothing"/>.
    /// </summary>
    public int LongPressHighlightIndex => _longPressHighlight;

    /// <summary>
    /// Gets the time in milliseconds when <see cref="Tick"/> has work, or <see cref="long.MaxValue"/> for none.
    /// </summary>
    public long NextDeadline => _repeatDeadline;

    /// <summary>
    /// Gets a value indicating whether any touch is down.
    /// </summary>
    public bool HasTouches
    {
        get
        {
            foreach (ref readonly var touch in _touches.AsSpan())
            {
                if (touch.Active)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Occurs when a key shows pressed, with its cell index.
    /// </summary>
    public event Action<int>? Pressed;

    /// <summary>
    /// Occurs when a key stops showing pressed, with its cell index.
    /// </summary>
    public event Action<int>? Released;

    /// <summary>
    /// Occurs for each key that produces input: on release, on rollover, on a repeat and on a long-press choice.
    /// </summary>
    public event Action<TypedKey>? Typed;

    /// <summary>
    /// Occurs on each press of a key or a list entry, and on each repeat, so a consumer can play the key sound.
    /// </summary>
    public event Action<KeyCap>? Clicked;

    /// <summary>
    /// Occurs when a long-press list opens, with the cell and the entries.
    /// </summary>
    public event Action<int, IReadOnlyList<KeyCap>>? LongPressOpened;

    /// <summary>
    /// Occurs when the highlighted entry of the open list changes, with the entry index or <see cref="LongPressFrame.Nothing"/>.
    /// </summary>
    public event Action<int>? LongPressHighlight;

    /// <summary>
    /// Occurs when an entry of the list is chosen, before the entry is handled as a key.
    /// </summary>
    public event Action<KeyCap>? LongPressChosen;

    /// <summary>
    /// Occurs when the open long-press list closes.
    /// </summary>
    public event Action? LongPressClosed;

    /// <summary>
    /// Occurs when the Hide key is typed. The consumer hides the keyboard.
    /// </summary>
    public event Action? HideRequested;

    /// <summary>
    /// Occurs when a size key is typed, with the size.
    /// </summary>
    public event Action<KeyboardSize>? SizeChosen;

    /// <summary>
    /// Occurs when the language key is typed.
    /// </summary>
    public event Action? LanguageNext;

    /// <summary>
    /// Occurs when a language is chosen from the language key's list, with the language index.
    /// </summary>
    public event Action<int>? LanguageChosen;

    /// <summary>
    /// Occurs when anything that a view draws can have changed.
    /// </summary>
    public event Action? Invalidated;

    /// <summary>
    /// Occurs when a touch becomes a trace.
    /// </summary>
    public event Action? TraceStarted;

    /// <summary>
    /// Occurs when the trace path gets new points.
    /// </summary>
    public event Action? TraceUpdated;

    /// <summary>
    /// Occurs when the tracing touch lifts. <see cref="TracePath"/> then holds the whole path.
    /// </summary>
    public event Action? TraceEnded;

    /// <summary>
    /// Occurs when <see cref="NextDeadline"/> can have changed.
    /// </summary>
    public event Action? DeadlineChanged;

    /// <summary>
    /// Occurs when a different page shows, with its name.
    /// </summary>
    public event Action<string>? PageChanged;

    /// <summary>
    /// Occurs when a shape key is typed, with the shape name, or with <see langword="null"/> for the next shape. The consumer swaps the grid.
    /// </summary>
    public event Action<string?>? ShapeRequested;

    /// <summary>
    /// Lays the keyboard out at a size. It closes a long-press list and raises <see cref="Invalidated"/>.
    /// </summary>
    /// <param name="width">The keyboard width in logical pixels.</param>
    /// <param name="keymapHeight">The height of the key rows in logical pixels.</param>
    /// <param name="padding">The height of the padding above the key rows in logical pixels.</param>
    public void Layout(float width, float keymapHeight, float padding)
    {
        _padding = padding;
        CloseLongPress();
        _hit.Layout(width, keymapHeight);
        Invalidated?.Invoke();
    }

    /// <summary>
    /// Gives the cap that a cell shows now, after Shift and Symbol.
    /// </summary>
    /// <param name="cell">The cell index.</param>
    /// <returns>The cap, or <see langword="null"/> for an index out of range or a cell with no key.</returns>
    public KeyCap? CapAt(int cell) => cell >= 0 && cell < _hit.Grid.CellCount ? State.Resolve(_hit.Grid[cell]) : null;

    /// <summary>
    /// Tests whether a cell shows pressed now.
    /// </summary>
    /// <param name="cell">The cell index.</param>
    /// <returns><see langword="true"/> when a visible, handled touch is on the cell.</returns>
    public bool IsPressed(int cell)
    {
        foreach (ref readonly var touch in _touches.AsSpan())
        {
            if (touch.Active && !touch.Pending && touch.Visible && touch.Cell == cell)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tests whether a touch at a point belongs to the keyboard. The grace band above the padding counts for a short time after a touch begins.
    /// </summary>
    /// <param name="x">The horizontal position in keyboard coordinates.</param>
    /// <param name="y">The vertical position, from the top of the key rows.</param>
    /// <param name="now">The time in milliseconds.</param>
    /// <returns><see langword="true"/> inside the keyboard, and everywhere while a long-press list is open.</returns>
    public bool Accepts(float x, float y, long now)
    {
        if (IsLongPressOpen)
        {
            return true;
        }

        if (Width <= 0 || x < 0 || x > Width || y > KeymapHeight)
        {
            return false;
        }

        var grace = now < _lastTouchBegin + Timing.GraceBandMs ? Timing.GraceBand : 0;
        return y >= -_padding - grace;
    }

    /// <summary>
    /// Records a press. <see cref="Frame"/> or a motion of the same touch handles it. An eleventh touch is ignored.
    /// </summary>
    /// <param name="id">The touch id.</param>
    /// <param name="x">The horizontal position in keyboard coordinates.</param>
    /// <param name="y">The vertical position, from the top of the key rows.</param>
    /// <param name="now">The time in milliseconds.</param>
    public void TouchDown(int id, float x, float y, long now)
    {
        var slot = FindSlot(id);
        if (slot < 0)
        {
            slot = FreeSlot();
            if (slot < 0)
            {
                return;
            }
        }

        ref var touch = ref _touches[slot];
        touch = new TouchSlot
        {
            Id = id,
            Active = true,
            Pending = true,
            Visible = true,
            Cell = NoCell,
            X = x,
            Y = y,
            FirstX = x,
            FirstY = y,
            DownTime = now,
        };
        if (!HasOtherActive(slot))
        {
            _lastTouchBegin = now;
        }
    }

    /// <summary>
    /// Moves a touch. A press moves to the key under the finger, and a trace collects the point. An unknown id is ignored.
    /// </summary>
    /// <param name="id">The touch id.</param>
    /// <param name="x">The horizontal position in keyboard coordinates.</param>
    /// <param name="y">The vertical position, from the top of the key rows.</param>
    /// <param name="now">The time in milliseconds.</param>
    public void TouchMotion(int id, float x, float y, long now)
    {
        var slot = FindSlot(id);
        if (slot < 0)
        {
            return;
        }

        ref var touch = ref _touches[slot];
        if (touch.Pending)
        {
            Update(slot, touch.X, touch.Y, now, newTouch: true);
        }

        if (slot == _traceSlot)
        {
            AppendTrace(x, y);
            touch.X = x;
            touch.Y = y;
            TraceUpdated?.Invoke();
            return;
        }

        if (touch.TraceCandidate && TraceEnabled && _longPress is null &&
            Vector2.Distance(new Vector2(touch.FirstX, touch.FirstY), new Vector2(x, y)) >= Timing.TraceThreshold)
        {
            BeginTrace(slot, x, y);
            return;
        }

        Update(slot, x, y, now, newTouch: false);
    }

    /// <summary>
    /// Releases a touch. A key that was not sent or consumed is typed now. An unknown id is ignored.
    /// </summary>
    /// <param name="id">The touch id.</param>
    /// <param name="now">The time in milliseconds.</param>
    public void TouchUp(int id, long now)
    {
        var slot = FindSlot(id);
        if (slot < 0)
        {
            return;
        }

        if (_touches[slot].Pending)
        {
            Update(slot, _touches[slot].X, _touches[slot].Y, now, newTouch: true);
        }

        if (slot == _traceSlot)
        {
            _traceSlot = -1;
            _touches[slot] = default;
            TraceEnded?.Invoke();
            if (!HasTouches)
            {
                EndAll();
            }

            return;
        }

        Release(slot, now);
        _touches[slot] = default;
        if (!HasTouches)
        {
            EndAll();
        }
    }

    /// <summary>
    /// Handles the recorded presses, after the releases and motions of the same input frame.
    /// </summary>
    /// <param name="now">The time in milliseconds.</param>
    public void Frame(long now)
    {
        for (var slot = 0; slot < _touches.Length; slot++)
        {
            if (_touches[slot] is { Active: true, Pending: true })
            {
                Update(slot, _touches[slot].X, _touches[slot].Y, now, newTouch: true);
            }
        }
    }

    /// <summary>
    /// Marks a touch consumed without sending its key, for example when an edge gesture claims it. A trace on it is dropped.
    /// </summary>
    /// <param name="id">The touch id.</param>
    public void Cancel(int id)
    {
        var slot = FindSlot(id);
        if (slot < 0)
        {
            return;
        }

        ref var touch = ref _touches[slot];
        var cell = touch.Cell;
        touch.Consumed = true;
        touch.Visible = false;
        touch.Pending = false;
        if (slot == _traceSlot)
        {
            _traceSlot = -1;
            _traceCount = 0;
            Invalidated?.Invoke();
        }
        if (cell == _repeatCell)
        {
            StopRepeat();
        }

        if (cell != NoCell)
        {
            Released?.Invoke(cell);
        }

        Invalidated?.Invoke();
    }

    /// <summary>
    /// Cancels every touch and clears the Shift and Symbol holds.
    /// </summary>
    public void CancelAll()
    {
        for (var slot = 0; slot < _touches.Length; slot++)
        {
            if (_touches[slot].Active)
            {
                Cancel(_touches[slot].Id);
            }
        }

        State.SetShiftHeld(false);
        State.SetSymbolHeld(false);
    }

    /// <summary>
    /// Drops every touch and trace, stops a repeat, closes a long-press list and clears the holds. The locks and the page stay.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_touches);
        _traceSlot = -1;
        _traceCount = 0;
        StopRepeat();
        CloseLongPress();
        State.SetShiftHeld(false);
        State.SetSymbolHeld(false);
        Invalidated?.Invoke();
    }

    /// <summary>
    /// Runs the deadline: a repeat of a repeating key, or the opening of a long-press list. Before <see cref="NextDeadline"/> it does nothing.
    /// </summary>
    /// <param name="now">The time in milliseconds.</param>
    public void Tick(long now)
    {
        if (now < _repeatDeadline || _repeatCell == NoCell)
        {
            return;
        }

        var cell = _hit.Grid[_repeatCell];
        var cap = State.Resolve(cell);
        if (cap is { Repeats: true })
        {
            Clicked?.Invoke(cap);
            var wordDelete = State.ShiftHeld || now - _repeatStart > Timing.WordDeleteAfterMs;
            Typed?.Invoke(new TypedKey(cap, State.Capital, State.ShiftHeld, Repeat: true, WordDelete: cap.Function == KeyFunction.Backspace && wordDelete, ModifiersFor(cap)));
            _repeatInterval = cap.Function == KeyFunction.Backspace && wordDelete ? Timing.WordDeleteIntervalMs : Timing.RepeatIntervalMs;
            _repeatDeadline += _repeatInterval;
            if (_repeatDeadline <= now)
            {
                _repeatDeadline = now + _repeatInterval;
            }

            DeadlineChanged?.Invoke();
            return;
        }

        var repeatCell = _repeatCell;
        StopRepeat();
        if (OpenLongPress(repeatCell, cancelIfSame: false))
        {
            for (var slot = 0; slot < _touches.Length; slot++)
            {
                if (_touches[slot].Active && _touches[slot].Cell == repeatCell)
                {
                    _touches[slot].Consumed = true;
                }
            }
        }
    }

    /// <summary>
    /// Presses and releases the center of a cell at one time, as one touch.
    /// </summary>
    /// <param name="id">The touch id to use.</param>
    /// <param name="cell">The cell index.</param>
    /// <param name="now">The time in milliseconds.</param>
    public void TapCell(int id, int cell, long now)
    {
        var rect = _hit.RectOf(cell);
        TouchDown(id, rect.CenterX, rect.CenterY, now);
        Frame(now);
        TouchUp(id, now);
    }

    /// <summary>
    /// Closes the open long-press list without typing. It does nothing when no list is open.
    /// </summary>
    public void CloseLongPress()
    {
        if (_longPress is null)
        {
            return;
        }

        _longPress = null;
        _longPressCell = NoCell;
        _longPressHighlight = LongPressFrame.Nothing;
        LongPressClosed?.Invoke();
        Invalidated?.Invoke();
    }

    /// <summary>
    /// Chooses an entry of the open list and handles it as a key. An index out of range, or no open list, does nothing.
    /// </summary>
    /// <param name="index">The entry index.</param>
    /// <param name="now">The time in milliseconds.</param>
    public void ChooseLongPress(int index, long now)
    {
        if (_longPress is not { } entries || index < 0 || index >= entries.Count)
        {
            return;
        }

        var entry = entries[index];
        CloseLongPress();
        LongPressChosen?.Invoke(entry);
        HandleKey(entry, now);
    }

    /// <summary>
    /// Shows a page by name. The name of the page that shows already shows the main page.
    /// </summary>
    /// <param name="name">The page name.</param>
    /// <returns><see langword="false"/> without pages or when the set has no such page.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool ShowPage(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_pages is not { } pages)
        {
            return false;
        }

        if (name == _page && name != KeyPageSet.MainPage)
        {
            name = KeyPageSet.MainPage;
        }

        if (!pages.TryGet(name, out var grid))
        {
            return false;
        }

        _page = name;
        SwapGrid(grid);
        PageChanged?.Invoke(name);
        return true;
    }

    /// <summary>
    /// Opens the long-press list of a cell. A letter on the symbol page has no list. A cell with no list closes the open list.
    /// </summary>
    /// <param name="cell">The cell index.</param>
    /// <param name="cancelIfSame"><see langword="true"/> to leave the list as it is when the same list is open already.</param>
    /// <returns><see langword="true"/> when the list opened.</returns>
    public bool OpenLongPress(int cell, bool cancelIfSame = false)
    {
        var entries = LongPressFor(cell);
        if (cancelIfSame && ReferenceEquals(entries, _longPress))
        {
            return false;
        }

        if (entries is null)
        {
            if (_longPress is not null)
            {
                _longPress = null;
                _longPressCell = NoCell;
                _longPressHighlight = LongPressFrame.Nothing;
                LongPressClosed?.Invoke();
            }

            return false;
        }

        _longPress = entries;
        _longPressCell = cell;
        _longPressHighlight = LongPressFrame.Nothing;
        _longPressFrame = LongPressFrame.For(_hit.RectOf(cell), entries.Count, Width);
        LongPressOpened?.Invoke(cell, entries);
        Invalidated?.Invoke();
        return true;
    }

    private IReadOnlyList<KeyCap>? LongPressFor(int cell)
    {
        if (cell < 0 || cell >= _hit.Grid.CellCount)
        {
            return null;
        }

        var key = _hit.Grid[cell];
        if (key.Plain is { IsLetter: true } && State.SymbolActive)
        {
            return null;
        }

        return key.LongPress;
    }

    private void Update(int slot, float x, float y, long now, bool newTouch)
    {
        ref var touch = ref _touches[slot];
        var entry = LongPressFrame.Outside;
        if (_longPress is not null)
        {
            entry = _longPressFrame.EntryAt(x, y);
        }

        var cell = entry == LongPressFrame.Outside && y >= -_padding ? _hit.Find(x, y, Diamonds) : NoCell;
        var cap = cell == NoCell ? null : State.Resolve(_hit.Grid[cell]);
        var previousCell = touch.Cell;
        if (newTouch)
        {
            touch.Pending = false;
            touch.FirstX = x;
            touch.FirstY = y;
            touch.TraceCandidate = TraceEnabled && _longPress is null && entry == LongPressFrame.Outside &&
                ActiveCount() == 1 && TraceDecoder.TracesLetter(cap);
        }

        if (entry != LongPressFrame.Outside)
        {
            if (newTouch && entry >= 0 && _longPress is { } list)
            {
                Clicked?.Invoke(list[entry]);
            }

            if (entry != _longPressHighlight)
            {
                _longPressHighlight = entry;
                LongPressHighlight?.Invoke(entry);
                Invalidated?.Invoke();
            }
        }
        else if (newTouch || previousCell != cell)
        {
            if (touch.Visible && !touch.Consumed && cell != _repeatCell)
            {
                if (newTouch && cap is not null && (cap.Repeats || LongPressFor(cell) is not null || (cap.Function == KeyFunction.Hide && ActiveCount() == 1)))
                {
                    var delay = cap.Function == KeyFunction.Hide || SlowLongPress ? Timing.SlowLongPressMs : cap.Repeats ? Timing.RepeatDelayMs : Timing.LongPressMs;
                    _repeatCell = cell;
                    _repeatStart = now;
                    _repeatDeadline = now + delay;
                    DeadlineChanged?.Invoke();
                }
                else
                {
                    StopRepeat();
                }
            }

            if (newTouch && cap is not null)
            {
                Clicked?.Invoke(cap);
                for (var other = 0; other < _touches.Length; other++)
                {
                    if (other == slot || !_touches[other].Active || _touches[other].Pending)
                    {
                        continue;
                    }

                    ref var held = ref _touches[other];
                    if (!held.Visible)
                    {
                        continue;
                    }

                    var heldCap = CapAt(held.Cell);
                    if (heldCap is not null && !held.Consumed &&
                        heldCap.Function is not (KeyFunction.Symbol or KeyFunction.Shift or KeyFunction.Hide))
                    {
                        HandleKey(heldCap, now);
                        held.Visible = false;
                        Released?.Invoke(held.Cell);
                    }

                    held.Consumed = true;
                }
            }

            if (touch.Visible && cap is not null)
            {
                var held = false;
                if (cap.Function == KeyFunction.Symbol && _longPress is null && !State.SymbolHeld)
                {
                    State.SetSymbolHeld(true);
                    held = true;
                }
                else if (cap.Function == KeyFunction.Shift && !State.ShiftHeld)
                {
                    State.SetShiftHeld(true);
                    held = true;
                }

                if (held && _longPress is not null)
                {
                    touch.Consumed = true;
                }
            }

            if (previousCell != NoCell && previousCell != cell)
            {
                Released?.Invoke(previousCell);
            }

            if (cell != NoCell && touch.Visible)
            {
                Pressed?.Invoke(cell);
            }

            Invalidated?.Invoke();
        }

        touch.Cell = cell;
        if (_longPress is not null && touch.Visible != (entry == LongPressFrame.Outside))
        {
            touch.Visible = !touch.Visible;
            Invalidated?.Invoke();
        }

        touch.X = x;
        touch.Y = y;
    }

    private void Release(int slot, long now)
    {
        ref var touch = ref _touches[slot];
        if (_longPress is { } list)
        {
            var entry = _longPressFrame.EntryAt(touch.X, touch.Y);
            if (entry == LongPressFrame.Outside)
            {
                var cap = CapAt(touch.Cell);
                if (cap is { Function: KeyFunction.Shift or KeyFunction.Symbol })
                {
                    if (cap.Function == KeyFunction.Shift)
                    {
                        State.SetShiftHeld(false);
                    }
                    else
                    {
                        State.SetSymbolHeld(false);
                    }

                    HandleKey(cap, now);
                }
                else if (!OpenLongPress(touch.Cell, cancelIfSame: true) && !touch.Consumed)
                {
                    CloseLongPress();
                }
                else
                {
                    Invalidated?.Invoke();
                }
            }
            else
            {
                CloseLongPress();
                if (entry >= 0)
                {
                    LongPressChosen?.Invoke(list[entry]);
                    HandleKey(list[entry], now);
                }
            }

            if (touch.Cell != NoCell)
            {
                Released?.Invoke(touch.Cell);
            }

            return;
        }

        if (touch.Cell == NoCell)
        {
            return;
        }

        var send = touch.Visible && !touch.Consumed;
        var key = CapAt(touch.Cell);
        if (key is null)
        {
            return;
        }

        if (key.Function == KeyFunction.Symbol)
        {
            State.SetSymbolHeld(false);
        }
        else if (key.Function == KeyFunction.Shift)
        {
            State.SetShiftHeld(false);
        }
        else
        {
            touch.Visible = false;
            touch.Consumed = true;
            if (State.ShiftHeld || State.SymbolHeld)
            {
                for (var other = 0; other < _touches.Length; other++)
                {
                    if (other != slot && _touches[other].Active &&
                        CapAt(_touches[other].Cell) is { Function: KeyFunction.Symbol or KeyFunction.Shift })
                    {
                        _touches[other].Consumed = true;
                    }
                }
            }
        }

        var cell = touch.Cell;
        if (send)
        {
            HandleKey(key, now);
        }

        if (cell == _repeatCell)
        {
            StopRepeat();
        }

        Released?.Invoke(cell);
        Invalidated?.Invoke();
    }

    private void HandleKey(KeyCap cap, long now)
    {
        switch (cap.Function)
        {
            case KeyFunction.Text:
            case KeyFunction.Backspace:
            case KeyFunction.Return:
            case KeyFunction.Tab:
            case KeyFunction.Left:
            case KeyFunction.Right:
            case KeyFunction.Escape:
            case KeyFunction.Up:
            case KeyFunction.Down:
            case KeyFunction.Home:
            case KeyFunction.End:
            case KeyFunction.PageUp:
            case KeyFunction.PageDown:
            case KeyFunction.Delete:
            case KeyFunction.FunctionKey:
                Typed?.Invoke(new TypedKey(cap, (State.Modifiers == KeyModifiers.None ? State.Capital : State.CapActive) && cap.IsText, State.ShiftHeld, Repeat: false, WordDelete: false, ModifiersFor(cap)));
                State.Consume();
                if (cap.IsSpace || cap.Function == KeyFunction.Return)
                {
                    State.ClearSymbolLock();
                }

                break;
            case KeyFunction.Symbol:
                if (_longPress is not null)
                {
                    CloseLongPress();
                }
                else
                {
                    State.TapSymbol();
                }

                break;
            case KeyFunction.Shift when cap.IsCapsLock:
                State.SetShift(State.Shift == ShiftMode.Lock ? ShiftMode.Off : ShiftMode.Lock);
                break;
            case KeyFunction.Shift:
                State.TapShift(now);
                break;
            case KeyFunction.Control:
            case KeyFunction.Alt:
            case KeyFunction.Super:
                State.TapModifier(cap.Function, now);
                break;
            case KeyFunction.Page:
                ShowPage(cap.Target);
                break;
            case KeyFunction.Emoji:
                ShowPage(EmojiPage);
                break;
            case KeyFunction.LayoutSwitch:
                ShapeRequested?.Invoke(null);
                break;
            case KeyFunction.ShapeChoice:
                ShapeRequested?.Invoke(cap.Target);
                break;
            case KeyFunction.Hide:
                HideRequested?.Invoke();
                break;
            case KeyFunction.Size:
                SizeChosen?.Invoke(cap.Size);
                break;
            case KeyFunction.Language:
                LanguageNext?.Invoke();
                break;
            case KeyFunction.LanguageChoice:
                LanguageChosen?.Invoke(cap.Argument);
                break;
        }
    }

    /// <summary>
    /// The name of the first emoji page, which the emoji key shows.
    /// </summary>
    public const string EmojiPage = "emoji";

    private KeyModifiers ModifiersFor(KeyCap cap)
    {
        var modifiers = State.Modifiers;
        if (cap.IsText || modifiers == KeyModifiers.None && cap.Function is KeyFunction.Backspace or KeyFunction.Return
            or KeyFunction.Tab or KeyFunction.Left or KeyFunction.Right)
        {
            return modifiers;
        }

        return State.ShiftActive ? modifiers | KeyModifiers.Shift : modifiers;
    }

    private void SwapGrid(KeyGrid grid)
    {
        CloseLongPress();
        StopRepeat();
        _hit.Grid = grid;
        Invalidated?.Invoke();
    }

    private void BeginTrace(int slot, float x, float y)
    {
        ref var touch = ref _touches[slot];
        var cell = touch.Cell;
        touch.Consumed = true;
        touch.Visible = false;
        if (cell == _repeatCell)
        {
            StopRepeat();
        }

        _traceSlot = slot;
        _traceCount = 0;
        AppendTrace(touch.FirstX, touch.FirstY);
        AppendTrace(x, y);
        touch.X = x;
        touch.Y = y;
        if (cell != NoCell)
        {
            Released?.Invoke(cell);
        }

        TraceStarted?.Invoke();
        TraceUpdated?.Invoke();
        Invalidated?.Invoke();
    }

    private void AppendTrace(float x, float y)
    {
        var point = new Vector2(x, y);
        if (_traceCount > 0 && Vector2.DistanceSquared(_trace[_traceCount - 1], point) < 4)
        {
            return;
        }

        if (_traceCount == TraceCapacity)
        {
            _trace[TraceCapacity - 1] = point;
            return;
        }

        _trace[_traceCount++] = point;
    }

    private void EndAll()
    {
        StopRepeat();
        State.SetShiftHeld(false);
        State.SetSymbolHeld(false);
    }

    private void StopRepeat()
    {
        var had = _repeatDeadline != long.MaxValue;
        _repeatCell = NoCell;
        _repeatStart = 0;
        _repeatDeadline = long.MaxValue;
        if (had)
        {
            DeadlineChanged?.Invoke();
        }
    }

    private int FindSlot(int id)
    {
        for (var slot = 0; slot < _touches.Length; slot++)
        {
            if (_touches[slot].Active && _touches[slot].Id == id)
            {
                return slot;
            }
        }

        return -1;
    }

    private int FreeSlot()
    {
        for (var slot = 0; slot < _touches.Length; slot++)
        {
            if (!_touches[slot].Active)
            {
                return slot;
            }
        }

        return -1;
    }

    private bool HasOtherActive(int slot)
    {
        for (var other = 0; other < _touches.Length; other++)
        {
            if (other != slot && _touches[other].Active)
            {
                return true;
            }
        }

        return false;
    }

    private int ActiveCount()
    {
        var count = 0;
        foreach (ref readonly var touch in _touches.AsSpan())
        {
            if (touch.Active)
            {
                count++;
            }
        }

        return count;
    }

    private struct TouchSlot
    {
        public int Id;
        public bool Active;
        public bool Pending;
        public bool Visible;
        public bool Consumed;
        public int Cell;
        public float X;
        public float Y;
        public float FirstX;
        public float FirstY;
        public long DownTime;
        public bool TraceCandidate;
    }
}
