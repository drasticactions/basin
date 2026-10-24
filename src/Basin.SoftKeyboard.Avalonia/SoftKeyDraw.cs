using Avalonia;

namespace Basin.SoftKeyboard.Avalonia;

/// <summary>
/// Holds what a skin needs to draw one key.
/// </summary>
public readonly struct SoftKeyDraw
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SoftKeyDraw"/> struct.
    /// </summary>
    /// <param name="engine">The engine that owns the key.</param>
    /// <param name="cell">The grid cell index.</param>
    /// <param name="bounds">The key rectangle in canvas coordinates.</param>
    /// <param name="key">The grid cell.</param>
    /// <param name="shown">The cap that the engine shows on the key now.</param>
    /// <param name="label">The text to draw on the key.</param>
    /// <param name="state">The key's state flags.</param>
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

    /// <summary>
    /// Gets the engine that owns the key.
    /// </summary>
    public SoftKeyboardEngine Engine { get; }

    /// <summary>
    /// Gets the grid cell index of the key.
    /// </summary>
    public int Cell { get; }

    /// <summary>
    /// Gets the key rectangle in canvas coordinates, with the keymap top already added.
    /// </summary>
    public Rect Bounds { get; }

    /// <summary>
    /// Gets the grid cell, with its weight, caps and class.
    /// </summary>
    public KeyCell Key { get; }

    /// <summary>
    /// Gets the cell's plain cap. The canvas draws only keys that have one.
    /// </summary>
    public KeyCap Plain => Key.Plain!;

    /// <summary>
    /// Gets the cap that the engine shows on the key now, for example the alternate cap on the symbol page.
    /// </summary>
    public KeyCap Shown { get; }

    /// <summary>
    /// Gets the text to draw on the key. It is empty for the space bar.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the key's state flags.
    /// </summary>
    public SoftKeyState State { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="State"/> has <see cref="SoftKeyState.Pressed"/>.
    /// </summary>
    public bool Pressed => (State & SoftKeyState.Pressed) != 0;
}
