using System.Diagnostics;
using Basin.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin;

[Flags]
public enum PresentedFlags : uint
{
    None = 0,
    Vsync = 1,
    HwClock = 2,
    HwCompletion = 4,
    ZeroCopy = 8,
}
