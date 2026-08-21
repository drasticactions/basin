using Pixman;

namespace Basin.Render.Pixman;

internal sealed class PixmanColorLut(ColorLut3D lut) : IColorLut
{
    public ColorLut3D Lut { get; } = lut;

    public void Dispose()
    {
    }
}
