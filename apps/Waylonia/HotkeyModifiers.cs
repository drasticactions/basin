using Microsoft.Extensions.Logging;

namespace Waylonia;

[Flags]
internal enum HotkeyModifiers
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
    Super = 8,
}
