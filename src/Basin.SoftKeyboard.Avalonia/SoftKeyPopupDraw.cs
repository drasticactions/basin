namespace Basin.SoftKeyboard.Avalonia;

/// <summary>
/// Holds what a skin needs to draw the open long-press list.
/// </summary>
public readonly struct SoftKeyPopupDraw
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SoftKeyPopupDraw"/> struct.
    /// </summary>
    /// <param name="engine">The engine that owns the list.</param>
    /// <param name="keymapTop">The canvas y where the keymap starts.</param>
    /// <param name="geometry">The list frame in keymap coordinates.</param>
    /// <param name="entries">The entries of the list.</param>
    /// <param name="highlighted">The index of the highlighted entry.</param>
    /// <param name="cell">The grid cell whose long press opened the list.</param>
    public SoftKeyPopupDraw(SoftKeyboardEngine engine, double keymapTop, LongPressFrame geometry, IReadOnlyList<KeyCap> entries, int highlighted, int cell)
    {
        Engine = engine;
        KeymapTop = keymapTop;
        Geometry = geometry;
        Entries = entries;
        Highlighted = highlighted;
        Cell = cell;
    }

    /// <summary>
    /// Gets the engine that owns the list.
    /// </summary>
    public SoftKeyboardEngine Engine { get; }

    /// <summary>
    /// Gets the canvas y where the keymap starts. Add it to <see cref="Geometry"/> to get canvas coordinates.
    /// </summary>
    public double KeymapTop { get; }

    /// <summary>
    /// Gets the list frame in keymap coordinates, with its line count and line length.
    /// </summary>
    public LongPressFrame Geometry { get; }

    /// <summary>
    /// Gets the entries of the list in display order.
    /// </summary>
    public IReadOnlyList<KeyCap> Entries { get; }

    /// <summary>
    /// Gets the index in <see cref="Entries"/> of the highlighted entry, as the engine reports it.
    /// </summary>
    public int Highlighted { get; }

    /// <summary>
    /// Gets the grid cell whose long press opened the list.
    /// </summary>
    public int Cell { get; }
}
