using Basin.WindowManager;
using SkiaSharp;
using Wayland;

namespace RetroWm;

internal enum FramePart
{
    SystemBox,
    Title,
    Border,
    Content,
}
