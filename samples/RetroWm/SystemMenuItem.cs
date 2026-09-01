using Basin.WindowManager;
using Basin.WindowManager.Skia;
using Basin.WindowManager.Skia.Protocol;
using SkiaSharp;
using Wayland;

namespace RetroWm;

internal enum SystemMenuItem
{
    Move,
    Size,
    Icon,
    Zoom,
    Close,
}
