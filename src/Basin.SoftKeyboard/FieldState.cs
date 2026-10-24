namespace Basin.SoftKeyboard;

/// <summary>
/// The state of the focused text field, as <see cref="SoftKeyboardTextInput"/> reads it from the text-input requests.
/// </summary>
/// <param name="Active">A value indicating whether a text input is enabled on the focused surface.</param>
/// <param name="Kind">The kind that the content purpose maps to.</param>
/// <param name="AutoCap">A value indicating whether the next key types a capital.</param>
/// <param name="Hint">The text-input v3 content hint bits.</param>
/// <param name="Purpose">The text-input v3 content purpose.</param>
public readonly record struct FieldState(bool Active, FieldKind Kind, bool AutoCap, uint Hint, uint Purpose)
{
    /// <summary>
    /// The text-input v3 <c>auto_capitalization</c> hint bit.
    /// </summary>
    public const uint HintAutoCapitalization = 0x4;
    /// <summary>
    /// The text-input v3 <c>lowercase</c> hint bit.
    /// </summary>
    public const uint HintLowercase = 0x8;
    /// <summary>
    /// The text-input v3 <c>uppercase</c> hint bit.
    /// </summary>
    public const uint HintUppercase = 0x10;

    /// <summary>
    /// Gets the state with no active field.
    /// </summary>
    public static FieldState Inactive => default;

    /// <summary>
    /// Gets the bottom-row variant that the field kind takes.
    /// </summary>
    public BottomRow BottomRow => Kind switch
    {
        FieldKind.Email => BottomRow.Email,
        FieldKind.Url => BottomRow.Url,
        _ => BottomRow.Default,
    };

    /// <summary>
    /// Gets a value indicating whether the number row gives digits without Shift: true for a number or phone field.
    /// </summary>
    public bool NumLock => Kind is FieldKind.Number or FieldKind.Phone;

    /// <summary>
    /// Maps a text-input v3 content purpose to a field kind. An unknown purpose gives <see cref="FieldKind.Text"/>.
    /// </summary>
    /// <param name="purpose">The content purpose.</param>
    /// <returns>The field kind.</returns>
    public static FieldKind KindOf(uint purpose) => purpose switch
    {
        0 or 1 or 7 => FieldKind.Text,
        2 or 3 or 9 => FieldKind.Number,
        4 => FieldKind.Phone,
        5 => FieldKind.Url,
        6 => FieldKind.Email,
        8 => FieldKind.Password,
        10 or 11 or 12 => FieldKind.Date,
        13 => FieldKind.Terminal,
        _ => FieldKind.Text,
    };

    /// <summary>
    /// Decides auto-cap. The <c>lowercase</c> hint turns it off and <c>uppercase</c> turns it on.
    /// Otherwise it is on for a normal, alpha or name field with <c>auto_capitalization</c> when the text before the cursor is empty or ends a sentence or a line.
    /// </summary>
    /// <param name="hint">The content hint bits.</param>
    /// <param name="purpose">The content purpose.</param>
    /// <param name="surrounding">The surrounding text, or <see langword="null"/> when the client sent none.</param>
    /// <param name="cursorBytes">The cursor position as a byte offset into the UTF-8 form of <paramref name="surrounding"/>.</param>
    /// <returns><see langword="true"/> when the next key must type a capital.</returns>
    public static bool AutoCapFor(uint hint, uint purpose, string? surrounding, int cursorBytes)
    {
        if ((hint & HintLowercase) != 0)
        {
            return false;
        }

        if ((hint & HintUppercase) != 0)
        {
            return true;
        }

        if ((hint & HintAutoCapitalization) == 0 || purpose is not (0 or 1 or 7))
        {
            return false;
        }

        if (string.IsNullOrEmpty(surrounding) || cursorBytes <= 0)
        {
            return true;
        }

        var before = BeforeCursor(surrounding, cursorBytes);
        if (before.IsEmpty || before[^1] == '\n')
        {
            return true;
        }

        return before.Length >= 2 && before[^1] == ' ' && before[^2] is '.' or '!' or '?';
    }

    /// <summary>
    /// Gives the text before a UTF-8 byte offset without an allocation. An offset inside a character includes that character.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="cursorBytes">The byte offset into the UTF-8 form of <paramref name="text"/>.</param>
    /// <returns>The part of <paramref name="text"/> before the offset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static ReadOnlySpan<char> BeforeCursor(string text, int cursorBytes)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = 0;
        var index = 0;
        while (index < text.Length && bytes < cursorBytes)
        {
            if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                bytes += 4;
                index += 2;
                continue;
            }

            bytes += text[index] switch
            {
                < '\u0080' => 1,
                < '\u0800' => 2,
                _ => 3,
            };
            index++;
        }

        return text.AsSpan(0, index);
    }
}
