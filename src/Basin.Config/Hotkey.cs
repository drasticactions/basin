namespace Basin.Config;

public sealed record Hotkey(uint Keysym, Modifiers ModifierMask, string? Action, string[]? Command)
{
    public bool Unbinds => Action is null && Command is null;

    public HotkeyScope When { get; init; }

    public bool AppliesTo(bool focusedFullscreen) => When switch
    {
        HotkeyScope.Windowed => !focusedFullscreen,
        HotkeyScope.Fullscreen => focusedFullscreen,
        _ => true,
    };
}
