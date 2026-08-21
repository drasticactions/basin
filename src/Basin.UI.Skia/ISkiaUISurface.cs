using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Skia;
using Pixman;
using SkiaSharp;

namespace Basin.UI.Skia;

public interface ISkiaUISurface : IUISurface
{
    SKCanvas BeginDraw();

    void EndDraw();
}
