using SkiaSharp;

namespace Dinghy;

internal sealed record DesktopIcon(ManagedWindow Window, string Title, SKImage? Image);
