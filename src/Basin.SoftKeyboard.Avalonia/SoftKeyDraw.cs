using Avalonia;

namespace Basin.SoftKeyboard.Avalonia;

public readonly struct SoftKeyDraw
{
    public SoftKeyDraw(SoftKeyboardEngine engine, int cell, Rect bounds, KeyCell key, KeyCap shown, string label, SoftKeyState state)
    {
        Engine = engine;
        Cell = cell;
        Bounds = bounds;
        Key = key;
        Shown = shown;
        Label = label;
        State = state;
    }

    public SoftKeyboardEngine Engine { get; }

    public int Cell { get; }

    public Rect Bounds { get; }

    public KeyCell Key { get; }

    public KeyCap Plain => Key.Plain!;

    public KeyCap Shown { get; }

    public string Label { get; }

    public SoftKeyState State { get; }

    public bool Pressed => (State & SoftKeyState.Pressed) != 0;
}
