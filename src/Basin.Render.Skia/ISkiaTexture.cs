using Pixman;
using SkiaSharp;

namespace Basin.Render.Skia;

public interface ISkiaTexture : ITexture
{
    bool Acquire(out SKImage image);

    void Release();
}
