using Pixman;

namespace Basin;

/// <summary>
/// Represents a buffer that a renderer imported for drawing. Only the renderer that made it can draw it.
/// </summary>
public interface ITexture : IDisposable
{
    /// <summary>
    /// Gets the width in buffer pixels.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Gets the height in buffer pixels.
    /// </summary>
    int Height { get; }
}
