using System.Text.RegularExpressions;
using Basin.WindowManager;
using Tomlyn;
using Tomlyn.Model;

namespace RetroWm;

internal sealed record HotkeyBinding(uint Keysym, Modifiers ModifierMask, WmAction? Action, string[]? Command);
