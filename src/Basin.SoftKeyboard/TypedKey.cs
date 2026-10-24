namespace Basin.SoftKeyboard;

/// <summary>
/// A key that <see cref="SoftKeyboardEngine"/> typed, as the <see cref="SoftKeyboardEngine.Typed"/> event carries it.
/// </summary>
/// <param name="Cap">The cap that the key showed when the engine sent it.</param>
/// <param name="Capital">A value indicating whether a text cap is typed as its upper-case form.</param>
/// <param name="ShiftHeld">A value indicating whether a Shift touch was held while the key was typed.</param>
/// <param name="Repeat">A value indicating whether the key came from the repeat timer and not from a press or a release.</param>
/// <param name="WordDelete">A value indicating whether a repeating Backspace deletes a word and not a character.</param>
/// <param name="Modifiers">The sticky or held modifiers that apply to the key. A key with Control, Alt or Super is a chord.</param>
public readonly record struct TypedKey(KeyCap Cap, bool Capital, bool ShiftHeld, bool Repeat, bool WordDelete, KeyModifiers Modifiers = KeyModifiers.None)
{
    /// <summary>
    /// Gets the text that the key types: the cap's upper-case text when <see cref="Capital"/> is set, otherwise its plain text.
    /// </summary>
    public string Text => Capital ? Cap.UpperText : Cap.Text;
}
