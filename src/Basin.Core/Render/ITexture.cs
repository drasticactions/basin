using Pixman;

namespace Basin;

public interface ITexture : IDisposable
{
    int Width { get; }

    int Height { get; }
}
