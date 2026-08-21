using Pixman;

namespace Basin;

public readonly record struct RenderColor(float R, float G, float B, float A)
{
    public static readonly RenderColor Transparent = new(0, 0, 0, 0);

    public static readonly RenderColor Black = new(0, 0, 0, 1);
}
