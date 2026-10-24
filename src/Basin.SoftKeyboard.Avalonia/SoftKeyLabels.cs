namespace Basin.SoftKeyboard.Avalonia;

/// <summary>
/// Gives the default text label of a key cap.
/// </summary>
public static class SoftKeyLabels
{
    /// <summary>
    /// Gets the cap's own label when it has one. Otherwise it gets the text, in capitals when the engine shows capitals, or a symbol for a function.
    /// </summary>
    /// <param name="engine">The engine whose state picks capitals.</param>
    /// <param name="cap">The cap to label.</param>
    /// <returns>The label. It is empty for the space bar and for a function with no default label.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="engine"/> or <paramref name="cap"/> is <see langword="null"/>.</exception>
    public static string For(SoftKeyboardEngine engine, KeyCap cap)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(cap);
        if (cap.Label is { } label)
        {
            return label;
        }

        return cap.Function switch
        {
            KeyFunction.Text when cap.IsSpace => "",
            KeyFunction.Text => cap.Text.Length == 1 && engine.State.Capital ? cap.UpperText : cap.Text,
            KeyFunction.Backspace => "⌫",
            KeyFunction.Return => "⏎",
            KeyFunction.Shift => "⇧",
            KeyFunction.Symbol => "?123",
            KeyFunction.Hide => "▾",
            KeyFunction.Tab => "⇥",
            KeyFunction.Left => "←",
            KeyFunction.Right => "→",
            KeyFunction.Up => "↑",
            KeyFunction.Down => "↓",
            KeyFunction.Control => "Ctrl",
            KeyFunction.Alt => "Alt",
            KeyFunction.Super => "Super",
            KeyFunction.Escape => "Esc",
            KeyFunction.Home => "Home",
            KeyFunction.End => "End",
            KeyFunction.PageUp => "PgUp",
            KeyFunction.PageDown => "PgDn",
            KeyFunction.Delete => "Del",
            KeyFunction.Emoji => "☺",
            KeyFunction.LayoutSwitch => "⌨",
            KeyFunction.Page or KeyFunction.ShapeChoice => cap.Target,
            _ => "",
        };
    }
}
