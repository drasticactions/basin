using Basin.WindowManager;

namespace Dinghy;

internal sealed record Hotkey(uint Keysym, Modifiers ModifierMask, string[] Command);
