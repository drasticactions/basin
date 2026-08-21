using Pixman;
using SkiaSharp;

namespace Basin.Render.Skia;

internal interface ISkiaTexture : ITexture
{
    bool Acquire(out SKImage image);

    void Release();
}
