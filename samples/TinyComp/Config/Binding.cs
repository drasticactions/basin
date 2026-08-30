using Basin.Config;

namespace TinyComp;

internal sealed record Binding(uint Keysym, Modifiers ModifierMask, KeyAction? Action, string[]? Command);
