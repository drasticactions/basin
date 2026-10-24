using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;

namespace Basin.SoftKeyboard.Avalonia;

/// <summary>
/// Draws a <see cref="SoftKeyboardEngine"/> through an <see cref="ISoftKeyboardSkin"/> and, in <see cref="SoftKeyboardInput.Pointer"/> mode, drives the engine from pointer events.
/// </summary>
public sealed class SoftKeyboardCanvas : Control, ICustomHitTest, Basin.Capabilities.IInputPaneView
{
    private SoftKeyboardEngine? _engine;
    private ISoftKeyboardSkin? _skin;
    private SoftKeyboardInput _input;
    private readonly HashSet<int> _down = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SoftKeyboardCanvas"/> class. The canvas does not take focus and clips to its bounds.
    /// </summary>
    public SoftKeyboardCanvas()
    {
        Focusable = false;
        ClipToBounds = true;
    }

    /// <summary>
    /// Gets or sets the engine to draw and drive. The canvas redraws on the engine's Invalidated event. A new engine forgets the held pointers.
    /// </summary>
    public SoftKeyboardEngine? Engine
    {
        get => _engine;
        set
        {
            if (ReferenceEquals(_engine, value))
            {
                return;
            }

            if (_engine is not null)
            {
                _engine.Invalidated -= InvalidateVisual;
            }

            _engine = value;
            _down.Clear();
            if (value is not null)
            {
                value.Invalidated += InvalidateVisual;
            }

            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets or sets the skin that draws the keyboard. With no skin, the canvas draws nothing.
    /// </summary>
    public ISoftKeyboardSkin? Skin
    {
        get => _skin;
        set
        {
            _skin = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets or sets the clock in milliseconds that the canvas gives to the engine. The default is <see cref="Environment.TickCount64"/>.
    /// </summary>
    public Func<long> Clock { get; set; } = static () => Environment.TickCount64;

    /// <summary>
    /// Gets or sets where the canvas gets its touches. <see cref="SoftKeyboardInput.External"/> also makes the canvas invisible to hit tests.
    /// </summary>
    public SoftKeyboardInput Input
    {
        get => _input;
        set
        {
            _input = value;
            IsHitTestVisible = value == SoftKeyboardInput.Pointer;
        }
    }

    /// <summary>
    /// Gets or sets the canvas y where the engine's first row starts. When it is <see langword="null"/>, the engine's padding is used.
    /// </summary>
    public double? KeymapTop { get; set; }

    /// <summary>
    /// Occurs in <see cref="SoftKeyboardInput.Pointer"/> mode when the last captured pointer loses its capture.
    /// </summary>
    public event Action? TouchesEnded;

    /// <summary>
    /// Tells whether a point takes input. In pointer mode, a point takes input from the padding above the keys down, and anywhere while a long-press list is open.
    /// </summary>
    /// <param name="point">The point in canvas coordinates.</param>
    /// <returns><see langword="false"/> in <see cref="SoftKeyboardInput.External"/> mode or outside the bounds.</returns>
    public bool HitTest(global::Avalonia.Point point)
    {
        if (_input != SoftKeyboardInput.Pointer || !new Rect(Bounds.Size).Contains(point))
        {
            return false;
        }

        return _engine is not { } engine || engine.IsLongPressOpen || point.Y >= Top(engine) - engine.Padding;
    }

    private double Top(SoftKeyboardEngine engine) => KeymapTop ?? engine.Padding;

    /// <summary>
    /// Calls the skin to draw the background, each visible key, the layer over the keys and the open long-press list, in that order.
    /// </summary>
    /// <param name="context">The context to draw into.</param>
    public override void Render(DrawingContext context)
    {
        if (_skin is not { } skin)
        {
            return;
        }

        var bounds = new Rect(Bounds.Size);
        skin.DrawBackground(context, bounds);
        if (_engine is not { } engine || engine.Width <= 0)
        {
            return;
        }

        var top = (float)Top(engine);
        var state = engine.State;
        var shared = SoftKeyState.None;
        if (state.Shift == ShiftMode.Lock)
        {
            shared |= SoftKeyState.ShiftLock;
        }
        else if (state.ShiftActive || state.AutoCap)
        {
            shared |= SoftKeyState.ShiftOnce;
        }

        if (state.Capital)
        {
            shared |= SoftKeyState.Capital;
        }

        if (state.SymbolActive)
        {
            shared |= SoftKeyState.Symbol;
        }

        var rects = engine.Rects;
        var grid = engine.Grid;
        for (var cell = 0; cell < grid.CellCount && cell < rects.Length; cell++)
        {
            var key = grid[cell];
            if (!key.IsVisible || key.Plain is not { } plain)
            {
                continue;
            }

            var shown = engine.CapAt(cell) ?? plain;
            var flags = shared;
            if (engine.IsPressed(cell))
            {
                flags |= SoftKeyState.Pressed;
            }

            if (!shown.IsText)
            {
                flags |= SoftKeyState.Function;
            }

            if (shown.Function is KeyFunction.Control or KeyFunction.Alt or KeyFunction.Super)
            {
                flags |= state.ModifierMode(shown.Function) switch
                {
                    ShiftMode.Once => SoftKeyState.Latched,
                    ShiftMode.Lock => SoftKeyState.Locked,
                    _ => SoftKeyState.None,
                };
            }

            var r = rects[cell];
            skin.DrawKey(context, new SoftKeyDraw(engine, cell, new Rect(r.X, r.Y + top, r.Width, r.Height), key, shown, SoftKeyLabels.For(engine, shown), flags));
        }

        skin.DrawOverKeys(context, bounds);
        if (engine.IsLongPressOpen && engine.LongPressEntries is { } entries)
        {
            skin.DrawPopup(context, new SoftKeyPopupDraw(engine, top, engine.LongPressGeometry, entries, engine.LongPressHighlightIndex, engine.LongPressCell));
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_input != SoftKeyboardInput.Pointer)
        {
            return;
        }

        if (_engine is { } engine)
        {
            var point = e.GetPosition(this);
            _down.Add(e.Pointer.Id);
            e.Pointer.Capture(this);
            engine.TouchDown(e.Pointer.Id, (float)point.X, (float)(point.Y - Top(engine)), Clock());
            engine.Frame(Clock());
        }

        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_input == SoftKeyboardInput.Pointer && _engine is { } engine && e.Pointer.Captured == this)
        {
            var point = e.GetPosition(this);
            engine.TouchMotion(e.Pointer.Id, (float)point.X, (float)(point.Y - Top(engine)), Clock());
            engine.Frame(Clock());
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_input != SoftKeyboardInput.Pointer)
        {
            return;
        }

        if (_engine is { } engine)
        {
            engine.TouchUp(e.Pointer.Id, Clock());
            engine.Frame(Clock());
        }

        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_input != SoftKeyboardInput.Pointer)
        {
            return;
        }

        _engine?.Cancel(e.Pointer.Id);
        if (_down.Remove(e.Pointer.Id) && _down.Count == 0)
        {
            TouchesEnded?.Invoke();
        }
    }
}
