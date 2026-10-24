namespace Basin.Capabilities;

/// <summary>
/// The input method that serves the text inputs of clients, for example an input method client or an on-screen keyboard.
/// The text input side calls the methods, and the input method answers through the events.
/// </summary>
public interface ITextInputMethod
{
    /// <summary>Gets a value indicating whether an input method can serve text input now.</summary>
    bool IsAvailable { get; }

    /// <summary>Tells the input method that a text input on a surface is enabled.</summary>
    /// <param name="surface">The surface that owns the text input.</param>
    void Activate(Surface surface);

    /// <summary>Tells the input method that the text input on a surface is disabled. A surface that is not active is ignored.</summary>
    /// <param name="surface">The surface that owns the text input.</param>
    void Deactivate(Surface surface);

    /// <summary>Sends the text around the cursor.</summary>
    /// <param name="text">The surrounding text.</param>
    /// <param name="cursor">The byte offset of the cursor in <paramref name="text"/>.</param>
    /// <param name="anchor">The byte offset of the selection anchor in <paramref name="text"/>.</param>
    void SurroundingText(string text, uint cursor, uint anchor);

    /// <summary>Sends the kind of content that the text input expects.</summary>
    /// <param name="hint">The <c>zwp_text_input_v3.content_hint</c> bits.</param>
    /// <param name="purpose">The <c>zwp_text_input_v3.content_purpose</c> value.</param>
    void ContentType(uint hint, uint purpose);

    /// <summary>Sends the area of the text cursor, so that a popup can appear near it.</summary>
    /// <param name="rect">The cursor area in surface-local coordinates.</param>
    void CursorRectangle(in Box rect);

    /// <summary>Tells the input method that the client applied a new text input state.</summary>
    /// <param name="serial">The number of commits that the text input made.</param>
    void Commit(uint serial);

    /// <summary>Sends a key event to the input method keyboard grab. It does nothing without a grab.</summary>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="keycode">The evdev keycode.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    void ForwardKey(uint timeMs, uint keycode, bool pressed);

    /// <summary>Sends the modifier state to the input method keyboard grab. It does nothing without a grab.</summary>
    /// <param name="depressed">The mask of modifiers that are held down.</param>
    /// <param name="latched">The mask of latched modifiers.</param>
    /// <param name="locked">The mask of locked modifiers.</param>
    /// <param name="group">The effective layout index.</param>
    void ForwardModifiers(uint depressed, uint latched, uint locked, uint group);

    /// <summary>Gets a value indicating whether the input method grabs the keyboard now. Key events then go to <see cref="ForwardKey"/>.</summary>
    bool HasKeyboardGrab { get; }

    /// <summary>Occurs when the input method sends new preedit text. An empty text clears the preedit.</summary>
    event Action<PreeditString>? Preedit;

    /// <summary>Occurs when the input method commits text to insert at the cursor.</summary>
    event Action<string>? CommitString;

    /// <summary>Occurs when the input method asks to delete text. The arguments are the byte lengths before and after the cursor.</summary>
    event Action<uint, uint>? DeleteSurroundingText;

    /// <summary>Occurs after a group of preedit, commit and delete events. The text input applies the group at this point.</summary>
    event Action? Done;

    /// <summary>Occurs after <see cref="IsAvailable"/> can have changed.</summary>
    event Action? AvailabilityChanged;
}
