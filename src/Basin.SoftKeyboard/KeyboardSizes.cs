namespace Basin.SoftKeyboard;

/// <summary>
/// Converts <see cref="KeyboardSize"/> values to and from their short names <c>xs</c>, <c>s</c>, <c>m</c> and <c>l</c>.
/// </summary>
public static class KeyboardSizes
{
    /// <summary>
    /// Reads a short size name. The match is case-sensitive.
    /// </summary>
    /// <param name="text">The name, or <see langword="null"/>.</param>
    /// <param name="fallback">The value to return for an unknown name or for <see langword="null"/>.</param>
    /// <returns>The size that the name gives, or <paramref name="fallback"/>.</returns>
    public static KeyboardSize Parse(string? text, KeyboardSize fallback) => text switch
    {
        "xs" => KeyboardSize.ExtraSmall,
        "s" => KeyboardSize.Small,
        "m" => KeyboardSize.Medium,
        "l" => KeyboardSize.Large,
        _ => fallback,
    };

    /// <summary>
    /// Gives the short name of a size. An undefined value gives <c>m</c>.
    /// </summary>
    /// <param name="size">The size to name.</param>
    /// <returns>The short name.</returns>
    public static string Name(KeyboardSize size) => size switch
    {
        KeyboardSize.ExtraSmall => "xs",
        KeyboardSize.Small => "s",
        KeyboardSize.Large => "l",
        _ => "m",
    };
}
