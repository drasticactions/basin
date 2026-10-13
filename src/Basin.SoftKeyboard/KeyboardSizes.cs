namespace Basin.SoftKeyboard;

public static class KeyboardSizes
{
    public static KeyboardSize Parse(string? text, KeyboardSize fallback) => text switch
    {
        "xs" => KeyboardSize.ExtraSmall,
        "s" => KeyboardSize.Small,
        "m" => KeyboardSize.Medium,
        "l" => KeyboardSize.Large,
        _ => fallback,
    };

    public static string Name(KeyboardSize size) => size switch
    {
        KeyboardSize.ExtraSmall => "xs",
        KeyboardSize.Small => "s",
        KeyboardSize.Large => "l",
        _ => "m",
    };
}
