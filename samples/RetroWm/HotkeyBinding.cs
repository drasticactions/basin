using Basin.Config;

namespace RetroWm;

internal sealed record HotkeyBinding(uint Keysym, Modifiers ModifierMask, WmAction? Action, string[]? Command);
