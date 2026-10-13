using Basin.Config;

namespace TinyComp;

internal sealed record Binding(uint Keysym, Modifiers ModifierMask, KeyAction? Action, string[]? Command)
{
    public HotkeyScope When { get; init; }

    public bool AppliesTo(bool focusedFullscreen) => When switch
    {
        HotkeyScope.Windowed => !focusedFullscreen,
        HotkeyScope.Fullscreen => focusedFullscreen,
        _ => true,
    };
}
