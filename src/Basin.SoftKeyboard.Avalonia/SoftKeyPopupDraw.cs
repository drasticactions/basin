namespace Basin.SoftKeyboard.Avalonia;

public readonly struct SoftKeyPopupDraw
{
    public SoftKeyPopupDraw(SoftKeyboardEngine engine, double keymapTop, LongPressFrame geometry, IReadOnlyList<KeyCap> entries, int highlighted, int cell)
    {
        Engine = engine;
        KeymapTop = keymapTop;
        Geometry = geometry;
        Entries = entries;
        Highlighted = highlighted;
        Cell = cell;
    }

    public SoftKeyboardEngine Engine { get; }

    public double KeymapTop { get; }

    public LongPressFrame Geometry { get; }

    public IReadOnlyList<KeyCap> Entries { get; }

    public int Highlighted { get; }

    public int Cell { get; }
}
