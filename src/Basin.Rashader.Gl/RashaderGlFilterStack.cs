using Basin.Diagnostics;
using Basin.Render.Gl;
using Silk.NET.OpenGLES;

namespace Basin.Rashader.Gl;

/// <summary>
/// Runs several <see cref="RashaderGlFilter"/> chains in order inside one <c>Record</c> call. Two
/// intermediate images carry the frame between chains.
/// </summary>
public sealed class RashaderGlFilterStack : IGlFilter, IDisposable
{
    private readonly RashaderGlFilter[] _filters;
    private readonly List<RashaderGlFilter> _live = [];
    private readonly uint[] _textures = new uint[2];
    private GlDevice? _device;
    private int _width;
    private int _height;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RashaderGlFilterStack"/> class. The stack takes
    /// ownership of the filters and disposes them with itself.
    /// </summary>
    /// <param name="filters">The chains in the order they run.</param>
    public RashaderGlFilterStack(IReadOnlyList<RashaderGlFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        _filters = [.. filters];
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the chains in the order they run, including chains that have failed.
    /// </summary>
    public IReadOnlyList<RashaderGlFilter> Filters => _filters;

    /// <summary>
    /// Gets a value indicating whether at least one chain in the stack is still live.
    /// </summary>
    public bool IsSupported
    {
        get
        {
            foreach (var filter in _filters)
            {
                if (filter.IsSupported)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the stack needs the whole frame. It is always <see
    /// langword="true"/>.
    /// </summary>
    public bool NeedsFullFrame => true;

    /// <summary>
    /// Gets a value indicating whether any chain in the stack asks for a repaint every frame.
    /// </summary>
    public bool NeedsContinuousRepaint
    {
        get
        {
            foreach (var filter in _filters)
            {
                if (filter.NeedsContinuousRepaint)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Runs every live chain in order. A chain that fails is skipped and its input goes to the next
    /// chain. The intermediate textures follow the target size.
    /// </summary>
    /// <param name="context">The source, the final target, the viewport and the frame options.</param>
    /// <returns><see langword="true"/> when the last live chain wrote the final target.</returns>
    public bool Record(in GlFilterContext context)
    {
        _live.Clear();
        foreach (var filter in _filters)
        {
            if (filter.IsSupported)
            {
                _live.Add(filter);
            }
        }

        if (_live.Count == 0)
        {
            return false;
        }

        if (_live.Count == 1)
        {
            return _live[0].Record(in context);
        }

        EnsureIntermediates(context.Device, context.TargetWidth, context.TargetHeight);

        var source = context.Source;
        var sourceWidth = context.SourceWidth;
        var sourceHeight = context.SourceHeight;
        var next = 0;
        var wrote = false;
        for (var i = 0; i < _live.Count; i++)
        {
            var last = i == _live.Count - 1;
            var target = last ? context.Target : _textures[next];
            var hop = new GlFilterContext
            {
                Device = context.Device,
                Source = source,
                SourceWidth = sourceWidth,
                SourceHeight = sourceHeight,
                Target = target,
                TargetWidth = context.TargetWidth,
                TargetHeight = context.TargetHeight,
                Viewport = last
                    ? context.Viewport
                    : new Box(0, 0, context.TargetWidth, context.TargetHeight),
                Options = context.Options,
            };
            if (!_live[i].Record(in hop))
            {
                continue;
            }

            if (last)
            {
                wrote = true;
                continue;
            }

            source = target;
            sourceWidth = context.TargetWidth;
            sourceHeight = context.TargetHeight;
            next ^= 1;
        }

        return wrote;
    }

    /// <summary>
    /// Frees the intermediate images and disposes every chain. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        BasinCounters.Untrack();
        DropIntermediates();
        foreach (var filter in _filters)
        {
            filter.Dispose();
        }
    }

    private void EnsureIntermediates(GlDevice device, int width, int height)
    {
        if (_device is not null && _width == width && _height == height)
        {
            return;
        }

        DropIntermediates();
        var gl = device.Gl;
        for (var i = 0; i < 2; i++)
        {
            _textures[i] = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, _textures[i]);
            gl.TexStorage2D(TextureTarget.Texture2D, 1, SizedInternalFormat.Rgba8, (uint)width, (uint)height);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 0);
        }

        gl.BindTexture(TextureTarget.Texture2D, 0);
        _device = device;
        _width = width;
        _height = height;
    }

    private void DropIntermediates()
    {
        if (_device is not { } device)
        {
            return;
        }

        for (var i = 0; i < 2; i++)
        {
            device.Gl.DeleteTexture(_textures[i]);
            _textures[i] = 0;
        }

        _device = null;
        _width = 0;
        _height = 0;
    }
}
