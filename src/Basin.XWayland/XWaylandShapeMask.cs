using Pixman;

namespace Basin.XWayland;

/// <summary>
/// Renders a copy of a window buffer with pixels outside an X Shape region cleared to transparent. It keeps a ring of three ARGB8888 targets.
/// </summary>
public sealed class XWaylandShapeMask : IDisposable
{
    private const int RingSize = 3;

    private readonly IRenderer _renderer;
    private readonly IAllocator? _allocator;
    private readonly IBuffer?[] _ring = new IBuffer?[RingSize];
    private readonly Dictionary<IBuffer, ITexture> _textures = [];
    private int _next;
    private int _width;
    private int _height;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="XWaylandShapeMask"/> class.
    /// </summary>
    /// <param name="renderer">The renderer that draws the masked copy.</param>
    /// <param name="allocator">The allocator for dmabuf targets, or <see langword="null"/> to use memory buffers.</param>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> is <see langword="null"/>.</exception>
    public XWaylandShapeMask(IRenderer renderer, IAllocator? allocator)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
        _allocator = allocator;
    }

    /// <summary>
    /// Draws <paramref name="source"/> clipped to <paramref name="shape"/> into the next target of the ring.
    /// </summary>
    /// <param name="source">The window buffer.</param>
    /// <param name="shape">The region to keep, in buffer pixels.</param>
    /// <returns>The target, which the mask owns and reuses three calls later, or <see langword="null"/> when the source is empty, cannot be imported or the pass fails.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The mask is disposed.</exception>
    public IBuffer? Render(IBuffer source, PixmanRegion32 shape)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(shape);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (source.Width <= 0 || source.Height <= 0)
        {
            return null;
        }

        if (source.Width != _width || source.Height != _height)
        {
            ReleaseRing();
            (_width, _height) = (source.Width, source.Height);
        }

        if (TextureOf(source) is not { } texture)
        {
            return null;
        }

        var target = _ring[_next] ??= Allocate(_width, _height);
        _next = (_next + 1) % RingSize;
        var pass = _renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.Clear(new RenderColor(0f, 0f, 0f, 0f));
        pass.AddTexture(texture, new TextureRenderOptions
        {
            DstBox = new Box(0, 0, _width, _height),
            Clip = shape,
        });
        return pass.Submit() ? target : null;
    }

    /// <summary>
    /// Destroys the targets and the cached textures. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseRing();
        foreach (var texture in _textures.Values)
        {
            texture.Dispose();
        }

        _textures.Clear();
    }

    private ITexture? TextureOf(IBuffer source)
    {
        if (_textures.TryGetValue(source, out var cached))
        {
            if (cached is IRefreshableTexture refreshable)
            {
                refreshable.MarkDirty();
            }

            return cached;
        }

        if (_renderer.ImportTexture(source) is not { } texture)
        {
            return null;
        }

        _textures[source] = texture;
        source.Destroyed += () =>
        {
            if (_textures.Remove(source, out var stale))
            {
                stale.Dispose();
            }
        };
        return texture;
    }

    private IBuffer Allocate(int width, int height)
    {
        if (_allocator is { } allocator)
        {
            var usable = allocator.Formats.Intersect(_renderer.DmabufTextureFormats).Intersect(_renderer.DmabufRenderFormats);
            if (usable.Contains(DrmFormat.Argb8888))
            {
                ulong[] modifiers = [.. usable.ModifiersOf(DrmFormat.Argb8888)];
                if (allocator.Allocate(width, height, DrmFormat.Argb8888, modifiers, BufferUse.Render) is { } buffer)
                {
                    return buffer;
                }
            }
        }

        return new MemoryBuffer(width, height, DrmFormat.Argb8888);
    }

    private void ReleaseRing()
    {
        for (var i = 0; i < _ring.Length; i++)
        {
            if (_ring[i] is BufferBase owned)
            {
                owned.Destroy();
            }

            _ring[i] = null;
        }

        _next = 0;
    }
}
