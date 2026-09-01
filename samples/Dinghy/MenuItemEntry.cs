using Basin.WindowManager;
using Basin.WindowManager.Skia;
using Basin.WindowManager.Skia.Protocol;
using SkiaSharp;
using Wayland;

namespace Dinghy;

internal sealed record MenuItemEntry(ManagedWindow Window, string Title, bool Hidden, bool Active);
