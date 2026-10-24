using Avalonia.Input;
using Basin.Config;

namespace Basin.Portal.Prompts.Avalonia;

/// <summary>
/// Translates Avalonia keys and modifiers into the names and masks that <see cref="TriggerSyntax"/> uses.
/// </summary>
public static class AvaloniaKeys
{
    /// <summary>
    /// Tells whether a key is a modifier or a lock key, which cannot be the main key of a chord.
    /// </summary>
    /// <param name="key">The Avalonia key.</param>
    /// <returns><see langword="true"/> for Shift, Ctrl, Alt, the Windows keys and the lock keys.</returns>
    public static bool IsModifier(Key key) => key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.CapsLock or Key.NumLock or Key.Scroll;

    /// <summary>
    /// Converts Avalonia modifier flags into a <see cref="Modifiers"/> mask. Meta becomes <see cref="Modifiers.Super"/>.
    /// </summary>
    /// <param name="modifiers">The Avalonia modifier flags.</param>
    /// <returns>The mask with Shift, Ctrl, Alt and Super.</returns>
    public static Modifiers ModifiersOf(KeyModifiers modifiers)
    {
        var result = Modifiers.None;
        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            result |= Modifiers.Shift;
        }

        if ((modifiers & KeyModifiers.Control) != 0)
        {
            result |= Modifiers.Ctrl;
        }

        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            result |= Modifiers.Alt;
        }

        if ((modifiers & KeyModifiers.Meta) != 0)
        {
            result |= Modifiers.Super;
        }

        return result;
    }

    /// <summary>
    /// Gets the XKB keysym name for a key on a US layout, for example <c>a</c>, <c>F5</c> or <c>Return</c>.
    /// </summary>
    /// <param name="key">The Avalonia key.</param>
    /// <returns>The keysym name. A key with no mapping gives the name of the <see cref="Key"/> value, which can be no keysym.</returns>
    public static string KeysymNameOf(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            return ((char)('a' + (key - Key.A))).ToString();
        }

        if (key >= Key.D0 && key <= Key.D9)
        {
            return ((char)('0' + (key - Key.D0))).ToString();
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return "KP_" + (key - Key.NumPad0);
        }

        if (key >= Key.F1 && key <= Key.F24)
        {
            return "F" + (1 + (key - Key.F1));
        }

        return key switch
        {
            Key.Space => "space",
            Key.Enter or Key.Return => "Return",
            Key.Tab => "Tab",
            Key.Back => "BackSpace",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "Prior",
            Key.PageDown => "Next",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Escape => "Escape",
            Key.PrintScreen => "Print",
            Key.Pause => "Pause",
            Key.OemMinus => "minus",
            Key.OemPlus => "equal",
            Key.OemComma => "comma",
            Key.OemPeriod => "period",
            Key.OemQuestion => "slash",
            Key.OemSemicolon => "semicolon",
            Key.OemQuotes => "apostrophe",
            Key.OemOpenBrackets => "bracketleft",
            Key.OemCloseBrackets => "bracketright",
            Key.OemPipe => "backslash",
            Key.OemTilde => "grave",
            _ => key.ToString(),
        };
    }
}
