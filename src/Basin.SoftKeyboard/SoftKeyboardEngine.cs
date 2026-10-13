using System.Numerics;

namespace Basin.SoftKeyboard;

public sealed class SoftKeyboardEngine
{
    public const int MaxTouches = 10;
    public const int NoCell = -1;
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

    public SoftKeyboardEngine(KeyGrid grid, KeyTiming? timing = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        _timing = timing ?? KeyTiming.Default;
        State = new KeyboardState(_timing);
        _hit = new KeyHitTester(grid);
    }

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

    public KeyboardState State { get; }

    public KeyHitTester HitTester => _hit;

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

    public string Page => _page;

    public bool Diamonds { get; set; } = true;

    public bool SlowLongPress { get; set; }

    public bool TraceEnabled { get; set; }

    public bool IsTracing => _traceSlot >= 0;

    public ReadOnlySpan<Vector2> TracePath => _trace.AsSpan(0, _traceCount);

    public float Width => _hit.Width;

    public float KeymapHeight => _hit.Height;

    public float Padding => _padding;

    public ReadOnlySpan<KeyRect> Rects => _hit.Rects;

    public bool IsLongPressOpen => _longPress is not null;

    public int LongPressCell => _longPressCell;

    public IReadOnlyList<KeyCap>? LongPressEntries => _longPress;

    public LongPressFrame LongPressGeometry => _longPressFrame;

    public int LongPressHighlightIndex => _longPressHighlight;

    public long NextDeadline => _repeatDeadline;

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

    public event Action<int>? Pressed;

    public event Action<int>? Released;

    public event Action<TypedKey>? Typed;

    public event Action<KeyCap>? Clicked;

    public event Action<int, IReadOnlyList<KeyCap>>? LongPressOpened;

    public event Action<int>? LongPressHighlight;

    public event Action<KeyCap>? LongPressChosen;

    public event Action? LongPressClosed;

    public event Action? HideRequested;

    public event Action<KeyboardSize>? SizeChosen;

    public event Action? LanguageNext;

    public event Action<int>? LanguageChosen;

    public event Action? Invalidated;

    public event Action? TraceStarted;

    public event Action? TraceUpdated;

    public event Action? TraceEnded;

    public event Action? DeadlineChanged;

    public event Action<string>? PageChanged;

    public event Action<string?>? ShapeRequested;

    public void Layout(float width, float keymapHeight, float padding)
    {
        _padding = padding;
        CloseLongPress();
        _hit.Layout(width, keymapHeight);
        Invalidated?.Invoke();
    }

    public KeyCap? CapAt(int cell) => cell >= 0 && cell < _hit.Grid.CellCount ? State.Resolve(_hit.Grid[cell]) : null;

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

    public void TapCell(int id, int cell, long now)
    {
        var rect = _hit.RectOf(cell);
        TouchDown(id, rect.CenterX, rect.CenterY, now);
        Frame(now);
        TouchUp(id, now);
    }

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
