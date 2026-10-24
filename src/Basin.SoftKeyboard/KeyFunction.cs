namespace Basin.SoftKeyboard;

/// <summary>
/// What a <see cref="KeyCap"/> does when it is typed.
/// </summary>
public enum KeyFunction
{
    /// <summary>
    /// No function. The default value.
    /// </summary>
    None,
    /// <summary>
    /// The cap types its text.
    /// </summary>
    Text,
    /// <summary>
    /// Deletes the character before the cursor. The key repeats.
    /// </summary>
    Backspace,
    /// <summary>
    /// The Return key.
    /// </summary>
    Return,
    /// <summary>
    /// The Shift key, which <see cref="KeyboardState"/> handles.
    /// </summary>
    Shift,
    /// <summary>
    /// The Symbol key, which shows the alternate caps.
    /// </summary>
    Symbol,
    /// <summary>
    /// Asks the consumer to hide the keyboard.
    /// </summary>
    Hide,
    /// <summary>
    /// The Tab key.
    /// </summary>
    Tab,
    /// <summary>
    /// Moves to the next language.
    /// </summary>
    Language,
    /// <summary>
    /// The Left arrow. The key repeats.
    /// </summary>
    Left,
    /// <summary>
    /// The Right arrow. The key repeats.
    /// </summary>
    Right,
    /// <summary>
    /// Chooses a keyboard size. The size is the cap's argument.
    /// </summary>
    Size,
    /// <summary>
    /// Chooses one language from the language key's long-press list.
    /// </summary>
    LanguageChoice,
    /// <summary>
    /// The sticky Control modifier.
    /// </summary>
    Control,
    /// <summary>
    /// The sticky Alt modifier.
    /// </summary>
    Alt,
    /// <summary>
    /// The sticky Super modifier.
    /// </summary>
    Super,
    /// <summary>
    /// The Escape key.
    /// </summary>
    Escape,
    /// <summary>
    /// The Up arrow. The key repeats.
    /// </summary>
    Up,
    /// <summary>
    /// The Down arrow. The key repeats.
    /// </summary>
    Down,
    /// <summary>
    /// The Home key.
    /// </summary>
    Home,
    /// <summary>
    /// The End key.
    /// </summary>
    End,
    /// <summary>
    /// The Page Up key. The key repeats.
    /// </summary>
    PageUp,
    /// <summary>
    /// The Page Down key. The key repeats.
    /// </summary>
    PageDown,
    /// <summary>
    /// The Delete key. The key repeats.
    /// </summary>
    Delete,
    /// <summary>
    /// A function key, F1 to F12. The number is the cap's argument.
    /// </summary>
    FunctionKey,
    /// <summary>
    /// Shows the page named <c>emoji</c>.
    /// </summary>
    Emoji,
    /// <summary>
    /// Asks the consumer for the next keyboard shape.
    /// </summary>
    LayoutSwitch,
    /// <summary>
    /// Asks the consumer for the keyboard shape that the cap names.
    /// </summary>
    ShapeChoice,
    /// <summary>
    /// Shows the page that the cap names, or the main page when that page shows already.
    /// </summary>
    Page,
}
