using System.Globalization;

namespace Basin.SoftKeyboard;

/// <summary>
/// Types a string by tapping the engine's own keys, as a person does. It uses Shift, Symbol, pages and long-press lists to reach each character.
/// </summary>
public sealed class SoftKeyboardTyper
{
    private const int Attempts = 5;
    private const int ChooseStepMilliseconds = 100;

    private readonly SoftKeyboardEngine _engine;
    private long _floor;

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftKeyboardTyper"/> class.
    /// </summary>
    /// <param name="engine">The engine whose keys the typer taps.</param>
    /// <param name="touchId">The touch id of the taps. It must not collide with a real touch id.</param>
    /// <exception cref="ArgumentNullException"><paramref name="engine"/> is <see langword="null"/>.</exception>
    public SoftKeyboardTyper(SoftKeyboardEngine engine, int touchId = 900)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        TouchId = touchId;
    }

    /// <summary>
    /// Gets the touch id of the taps.
    /// </summary>
    public int TouchId { get; }

    /// <summary>
    /// Gets or sets the time that each tap moves the clock forward. The default of 600 keeps two taps from being read as a double tap or a repeat.
    /// </summary>
    public int StepMilliseconds { get; set; } = 600;

    /// <summary>
    /// Gets the clock in milliseconds: the wall clock or the typer's floor, whichever is later. The owner uses it for its own engine calls.
    /// </summary>
    public long Clock => Math.Max(Environment.TickCount64, _floor);

    /// <summary>
    /// Occurs before each press that the typer makes.
    /// </summary>
    public event Action? Pressing;

    /// <summary>
    /// Moves the clock floor forward from <see cref="Clock"/>.
    /// </summary>
    /// <param name="milliseconds">The time to move forward.</param>
    /// <returns>The new floor.</returns>
    public long Step(int milliseconds)
    {
        _floor = Clock + milliseconds;
        return _floor;
    }

    /// <summary>
    /// Moves the clock floor forward to a time. A time behind the floor does nothing.
    /// </summary>
    /// <param name="time">The time in milliseconds.</param>
    public void AdvanceTo(long time) => _floor = Math.Max(_floor, time);

    /// <summary>
    /// Types each text element of a text in turn, so an emoji of several UTF-16 units is one key. It stops at the first element that no key makes.
    /// </summary>
    /// <param name="text">The text to type.</param>
    /// <returns><see langword="true"/> when every element was typed.</returns>
    public bool Type(ReadOnlySpan<char> text)
    {
        while (!text.IsEmpty)
        {
            var length = StringInfo.GetNextTextElementLength(text);
            if (!TypeText(text[..length].ToString()))
            {
                return false;
            }

            text = text[length..];
        }

        return true;
    }

    /// <summary>
    /// Types one character. It is the same as <see cref="TypeText"/> with a one-character string.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><see langword="false"/> when no key makes the character.</returns>
    public bool TypeCharacter(char c) => TypeText(c.ToString());

    /// <summary>
    /// Types one text element, for example a letter or an emoji. It taps a key that makes it, or chooses it from a long-press list, after up to five steps through pages, Shift and Symbol.
    /// An element that ends in the emoji variation selector U+FE0F is tried again without it.
    /// </summary>
    /// <param name="element">The text element.</param>
    /// <returns><see langword="false"/> when no key makes the element.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    public bool TypeText(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (TypeElement(element))
        {
            return true;
        }

        return element.Length > 1 && element[^1] == '\uFE0F' && TypeElement(element[..^1]);
    }

    private bool TypeElement(string text)
    {
        var single = text.Length == 1;
        var lower = single ? char.ToLowerInvariant(text[0]).ToString() : text;
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

            if (!Produces(_engine.Grid, text) && !Produces(_engine.Grid, lower) && PageKeyToward(text, out var pageKey))
            {
                TapCell(pageKey);
                continue;
            }

            var state = _engine.State;
            if (single && char.IsLetter(text[0]) && !CellFor(lower, alternate: true, out _))
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

    /// <summary>
    /// Taps the visible key that <see cref="Find"/> finds for a name.
    /// </summary>
    /// <param name="name">A cap's text, a function name, or an alias.</param>
    /// <returns><see langword="false"/> when no visible key has the name.</returns>
    public bool Tap(string name) => TapCell(Find(name), StepMilliseconds);

    /// <summary>
    /// Taps the visible key that <see cref="Find"/> finds for a name, with its own clock step.
    /// </summary>
    /// <param name="name">A cap's text, a function name, or an alias.</param>
    /// <param name="stepMilliseconds">The time that the tap moves the clock forward.</param>
    /// <returns><see langword="false"/> when no visible key has the name.</returns>
    public bool Tap(string name, int stepMilliseconds) => TapCell(Find(name), stepMilliseconds);

    /// <summary>
    /// Taps a cell.
    /// </summary>
    /// <param name="cell">The cell index.</param>
    /// <returns><see langword="false"/> when the index is out of range.</returns>
    public bool TapCell(int cell) => TapCell(cell, StepMilliseconds);

    /// <summary>
    /// Taps a cell with its own clock step.
    /// </summary>
    /// <param name="cell">The cell index.</param>
    /// <param name="stepMilliseconds">The time that the tap moves the clock forward.</param>
    /// <returns><see langword="false"/> when the index is out of range.</returns>
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

    /// <summary>
    /// Chooses a text from the long-press list of the key that holds it.
    /// </summary>
    /// <param name="text">The text of the list entry.</param>
    /// <returns><see langword="false"/> when no long-press list has the text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Presses a cell, ticks the engine through every deadline in the hold, and releases it. A hold past the long-press time opens the list.
    /// </summary>
    /// <param name="cell">The cell index.</param>
    /// <param name="milliseconds">The hold time.</param>
    /// <returns><see langword="false"/> when the index is out of range.</returns>
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

    /// <summary>
    /// Finds the first visible key by a cap's text or a function name. The aliases <c>space</c>, <c>sym</c>, <c>lang</c> and <c>enter</c> are also accepted.
    /// </summary>
    /// <param name="name">The name to find.</param>
    /// <returns>The cell index, or -1 when no visible key has the name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
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
