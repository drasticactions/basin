namespace Basin.SoftKeyboard;

public sealed class SoftKeyboardTyper
{
    private const int Attempts = 5;
    private const int ChooseStepMilliseconds = 100;

    private readonly SoftKeyboardEngine _engine;
    private long _floor;

    public SoftKeyboardTyper(SoftKeyboardEngine engine, int touchId = 900)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        TouchId = touchId;
    }

    public int TouchId { get; }

    public int StepMilliseconds { get; set; } = 600;

    public long Clock => Math.Max(Environment.TickCount64, _floor);

    public event Action? Pressing;

    public long Step(int milliseconds)
    {
        _floor = Clock + milliseconds;
        return _floor;
    }

    public void AdvanceTo(long time) => _floor = Math.Max(_floor, time);

    public bool Type(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (!TypeCharacter(c))
            {
                return false;
            }
        }

        return true;
    }

    public bool TypeCharacter(char c)
    {
        var text = c.ToString();
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var cell = Producing(text);
            if (cell >= 0)
            {
                return TapCell(cell);
            }

            if (LongPressFor(text, out var holder, out var index))
            {
                OpenLongPress(holder);
                _engine.ChooseLongPress(index, Step(ChooseStepMilliseconds));
                return true;
            }

            if (!Produces(_engine.Grid, text) && !Produces(_engine.Grid, char.ToLowerInvariant(c).ToString()) && PageKeyToward(text, out var pageKey))
            {
                TapCell(pageKey);
                continue;
            }

            var state = _engine.State;
            if (char.IsLetter(c) && !CellFor(char.ToLowerInvariant(c).ToString(), alternate: true, out _))
            {
                Tap("shift");
                continue;
            }

            if (CellFor(text, alternate: true, out var alternateCell))
            {
                Tap(_engine.Grid[alternateCell].Plain!.IsLetter ? "symbol" : "shift");
                continue;
            }

            if (CellFor(text, alternate: false, out _))
            {
                Tap(state.SymbolActive ? "symbol" : "shift");
                continue;
            }

            return false;
        }

        return false;
    }

    public bool Tap(string name) => TapCell(Find(name), StepMilliseconds);

    public bool Tap(string name, int stepMilliseconds) => TapCell(Find(name), stepMilliseconds);

    public bool TapCell(int cell) => TapCell(cell, StepMilliseconds);

    public bool TapCell(int cell, int stepMilliseconds)
    {
        if (cell < 0 || cell >= _engine.Grid.CellCount)
        {
            return false;
        }

        Pressing?.Invoke();
        _engine.TapCell(TouchId, cell, Step(stepMilliseconds));
        return true;
    }

    public bool Hold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!LongPressFor(text, out var cell, out var index))
        {
            return false;
        }

        OpenLongPress(cell);
        _engine.ChooseLongPress(index, Step(ChooseStepMilliseconds));
        return true;
    }

    public bool HoldCell(int cell, int milliseconds)
    {
        if (cell < 0 || cell >= _engine.Grid.CellCount)
        {
            return false;
        }

        var rect = _engine.Rects[cell];
        var start = Step(StepMilliseconds);
        Pressing?.Invoke();
        _engine.TouchDown(TouchId, rect.CenterX, rect.CenterY, start);
        _engine.Frame(start);
        var end = start + Math.Max(0, milliseconds);
        while (_engine.NextDeadline <= end)
        {
            _engine.Tick(_engine.NextDeadline);
        }

        AdvanceTo(end);
        _engine.TouchUp(TouchId, end);
        return true;
    }

    public int Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var grid = _engine.Grid;
        var wanted = name switch
        {
            "space" => " ",
            "sym" => "symbol",
            "lang" => "language",
            "enter" => "return",
            _ => name,
        };
        for (var cell = 0; cell < _engine.Grid.CellCount; cell++)
        {
            if (grid[cell] is { IsVisible: true, Plain: { } plain } &&
                (plain.IsText ? plain.Text == wanted || plain.Text == wanted.ToLowerInvariant() : plain.Name == wanted))
            {
                return cell;
            }
        }

        return -1;
    }

    private void OpenLongPress(int cell)
    {
        var rect = _engine.Rects[cell];
        var start = Step(StepMilliseconds);
        Pressing?.Invoke();
        _engine.TouchDown(TouchId, rect.CenterX, rect.CenterY, start);
        _engine.Frame(start);
        var deadline = _engine.NextDeadline;
        if (deadline != long.MaxValue)
        {
            AdvanceTo(deadline);
            _engine.Tick(deadline);
        }

        _engine.TouchUp(TouchId, Clock);
    }

    private int Producing(string text)
    {
        var state = _engine.State;
        for (var cell = 0; cell < _engine.Grid.CellCount; cell++)
        {
            if (_engine.Grid[cell] is not { IsVisible: true } key || state.Resolve(key) is not { IsText: true } cap)
            {
                continue;
            }

            var produced = cap.Text.Length == 1 && state.Capital ? cap.UpperText : cap.Text;
            if (produced == text)
            {
                return cell;
            }
        }

        return -1;
    }

    private bool CellFor(string text, bool alternate, out int found)
    {
        for (var cell = 0; cell < _engine.Grid.CellCount; cell++)
        {
            if (_engine.Grid[cell] is not { IsVisible: true } key)
            {
                continue;
            }

            var cap = alternate ? key.Alternate : key.Plain;
            if (cap is { IsText: true } && (cap.Text == text || (cap.Text.Length == 1 && cap.UpperText == text && !alternate)))
            {
                found = cell;
                return true;
            }
        }

        found = -1;
        return false;
    }

    private bool PageKeyToward(string text, out int cell)
    {
        cell = -1;
        if (_engine.Pages is not { } pages)
        {
            return false;
        }

        var first = new Dictionary<string, int>(StringComparer.Ordinal) { [_engine.Page] = -1 };
        var queue = new Queue<string>();
        queue.Enqueue(_engine.Page);
        while (queue.Count > 0)
        {
            var page = queue.Dequeue();
            if (!pages.TryGet(page, out var grid))
            {
                continue;
            }

            if (page != _engine.Page && Produces(grid, text))
            {
                cell = first[page];
                return true;
            }

            for (var i = 0; i < grid.CellCount; i++)
            {
                if (grid[i] is not { IsVisible: true, Plain: { Function: KeyFunction.Page or KeyFunction.Emoji } key })
                {
                    continue;
                }

                var target = key.Function == KeyFunction.Emoji ? SoftKeyboardEngine.EmojiPage : key.Target;
                if (target == page)
                {
                    target = KeyPageSet.MainPage;
                }

                if (!first.ContainsKey(target))
                {
                    first[target] = page == _engine.Page ? i : first[page];
                    queue.Enqueue(target);
                }
            }
        }

        return false;
    }

    private static bool Produces(KeyGrid grid, string text)
    {
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid[i] is { IsVisible: true } key &&
                (key.Plain is { IsText: true } plain && (plain.Text == text || plain.UpperText == text) ||
                 key.Alternate is { IsText: true } alternate && alternate.Text == text))
            {
                return true;
            }
        }

        return false;
    }

    private bool LongPressFor(string text, out int cell, out int index)
    {
        for (cell = 0; cell < _engine.Grid.CellCount; cell++)
        {
            if (_engine.Grid[cell] is not { IsVisible: true, LongPress: { } entries })
            {
                continue;
            }

            for (index = 1; index < entries.Count; index++)
            {
                if (entries[index].IsText && entries[index].Text == text)
                {
                    return true;
                }
            }
        }

        cell = -1;
        index = -1;
        return false;
    }
}
