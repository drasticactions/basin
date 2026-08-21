using Basin.WindowManager;
using Dinghy.Protocol;
using SkiaSharp;
using Wayland;

namespace Dinghy;

internal sealed record MenuItemEntry(ManagedWindow Window, string Title, bool Hidden, bool Active);
