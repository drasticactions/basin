namespace Basin.SoftKeyboard.Avalonia;

public static class SoftKeyLabels
{
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
