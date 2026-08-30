namespace Basin.Config;

public sealed record Hotkey(uint Keysym, Modifiers ModifierMask, string? Action, string[]? Command)
{
    public bool Unbinds => Action is null && Command is null;
}
