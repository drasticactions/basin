using Basin.Diagnostics;

namespace Basin.Render.Gl;

internal sealed class GlColorLut : IColorLut
{
    private readonly GlRenderer _renderer;
    private bool _disposed;

    public uint TextureId { get; }

    internal GlColorLut(GlRenderer renderer, uint textureId)
    {
        _renderer = renderer;
        TextureId = textureId;
        BasinCounters.Track();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        BasinCounters.Untrack();
        _renderer.ReleaseLut(TextureId);
    }
}
