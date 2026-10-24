using Basin.Capabilities;
using Basin.Render.Gl;
using Basin.Render.Vulkan;

namespace Basin.Effects;

/// <summary>
/// Keeps one backdrop glass object across a renderer replacement. It forwards to a GL or Vulkan glass, and makes a new one on the new renderer with the same options and shapes.
/// </summary>
public sealed class RebindableBackdropGlass : IBackdropGlass, IGlBackdropEffect, IVulkanBackdropEffect, IRendererBound
{
    private readonly Dictionary<object, FrameBackdropShape> _shapes = [];
    private IBackdropGlass? _inner;
    private GlassOptions _options;
    private bool _disposed;

    private RebindableBackdropGlass(IBackdropGlass inner)
    {
        _inner = inner;
        _options = inner.Options;
    }

    /// <summary>
    /// Makes a glass for a renderer that runs backdrop effects on a GL or Vulkan device.
    /// </summary>
    /// <param name="renderer">The renderer that runs the effect.</param>
    /// <returns>The glass, which the caller disposes, or <see langword="null"/> when the renderer cannot run it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> is <see langword="null"/>.</exception>
    public static RebindableBackdropGlass? For(IRenderer renderer) =>
        BackdropGlass.For(renderer) is { } inner ? new RebindableBackdropGlass(inner) : null;

    /// <summary>
    /// Gets the glass on the current renderer, or <see langword="null"/> between a replacement and the rebuild.
    /// </summary>
    public IBackdropGlass? Inner => _inner;

    /// <inheritdoc/>
    public GlassOptions Options
    {
        get => _inner?.Options ?? _options;
        set
        {
            _options = value;
            if (_inner is { } inner)
            {
                inner.Options = value;
            }
        }
    }

    /// <inheritdoc/>
    public int ExpandSize => _inner?.ExpandSize ?? 0;

    /// <inheritdoc/>
    public void SetShape(object key, in FrameBackdropShape shape)
    {
        _shapes[key] = shape;
        _inner?.SetShape(key, shape);
    }

    /// <inheritdoc/>
    public bool ForgetSurface(object key)
    {
        var known = _shapes.Remove(key);
        return (_inner?.ForgetSurface(key) ?? false) || known;
    }

    /// <inheritdoc/>
    public bool Record(in GlBackdropContext context, out GlBackdropResult result)
    {
        if (_inner is IGlBackdropEffect gl)
        {
            return gl.Record(context, out result);
        }

        result = default;
        return false;
    }

    /// <inheritdoc/>
    public bool Record(in VulkanBackdropContext context, out VulkanBackdropResult result)
    {
        if (_inner is IVulkanBackdropEffect vulkan)
        {
            return vulkan.Record(context, out result);
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Disposes the glass on the old renderer while its context is current.
    /// </summary>
    /// <param name="replacement">The old and the new renderer.</param>
    public void OnRendererReplaced(RendererReplacement replacement)
    {
        _inner?.Dispose();
        _inner = null;
    }

    /// <summary>
    /// Makes the glass on the new renderer and gives it the options and the shapes again. A second call does nothing.
    /// </summary>
    /// <param name="replacement">The old and the new renderer.</param>
    public void OnRendererRebuilt(RendererReplacement replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (_disposed || _inner is not null || BackdropGlass.For(replacement.New.Renderer) is not { } inner)
        {
            return;
        }

        inner.Options = _options;
        foreach (var (key, shape) in _shapes)
        {
            inner.SetShape(key, shape);
        }

        _inner = inner;
    }

    /// <summary>
    /// Disposes the glass on the current renderer. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _inner?.Dispose();
        _inner = null;
        _shapes.Clear();
    }
}
