using Basin.Capabilities;
using Basin.Render.Gl;
using Basin.Render.Vulkan;

namespace Basin.Effects;

/// <summary>
/// Keeps one backdrop blur object across a renderer replacement. It forwards to a GL or Vulkan blur, and makes a new one on the new renderer with the same options and surfaces.
/// </summary>
public sealed class RebindableBackdropBlur : IBackdropBlur, IGlBackdropEffect, IVulkanBackdropEffect, IRendererBound
{
    private readonly Dictionary<object, BlurSurfaceOptions> _surfaces = [];
    private IBackdropBlur? _inner;
    private BlurOptions _options;
    private BlurCorners _corners;
    private double _opacity;
    private bool _disposed;

    private RebindableBackdropBlur(IBackdropBlur inner)
    {
        _inner = inner;
        _options = inner.Options;
        _corners = inner.Corners;
        _opacity = inner.Opacity;
    }

    /// <summary>
    /// Makes a blur for a renderer that runs backdrop effects on a GL or Vulkan device.
    /// </summary>
    /// <param name="renderer">The renderer that runs the effect.</param>
    /// <returns>The blur, which the caller disposes, or <see langword="null"/> when the renderer cannot run it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> is <see langword="null"/>.</exception>
    public static RebindableBackdropBlur? For(IRenderer renderer) =>
        BackdropBlurs.For(renderer) is { } inner ? new RebindableBackdropBlur(inner) : null;

    /// <summary>
    /// Gets the blur on the current renderer, or <see langword="null"/> between a replacement and the rebuild.
    /// </summary>
    public IBackdropBlur? Inner => _inner;

    /// <inheritdoc/>
    public BackgroundEffects Supported => _inner?.Supported ?? BackgroundEffects.Blur | BackgroundEffects.Contrast;

    /// <inheritdoc/>
    public BlurOptions Options
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
    public BlurCorners Corners
    {
        get => _corners;
        set
        {
            _corners = value;
            if (_inner is { } inner)
            {
                inner.Corners = value;
            }
        }
    }

    /// <inheritdoc/>
    public double Opacity
    {
        get => _opacity;
        set
        {
            _opacity = value;
            if (_inner is { } inner)
            {
                inner.Opacity = value;
            }
        }
    }

    /// <inheritdoc/>
    public int ExpandSize => _inner?.ExpandSize ?? 0;

    /// <inheritdoc/>
    public void SetSurface(object key, in BlurSurfaceOptions options)
    {
        _surfaces[key] = options;
        _inner?.SetSurface(key, options);
    }

    /// <inheritdoc/>
    public bool ForgetSurface(object key)
    {
        var known = _surfaces.Remove(key);
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
    /// Disposes the blur on the old renderer while its context is current.
    /// </summary>
    /// <param name="replacement">The old and the new renderer.</param>
    public void OnRendererReplaced(RendererReplacement replacement)
    {
        _inner?.Dispose();
        _inner = null;
    }

    /// <summary>
    /// Makes the blur on the new renderer and gives it the options and the surfaces again. A second call does nothing.
    /// </summary>
    /// <param name="replacement">The old and the new renderer.</param>
    public void OnRendererRebuilt(RendererReplacement replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (_disposed || _inner is not null || BackdropBlurs.For(replacement.New.Renderer) is not { } inner)
        {
            return;
        }

        inner.Options = _options;
        inner.Corners = _corners;
        inner.Opacity = _opacity;
        foreach (var (key, options) in _surfaces)
        {
            inner.SetSurface(key, options);
        }

        _inner = inner;
    }

    /// <summary>
    /// Disposes the blur on the current renderer. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _inner?.Dispose();
        _inner = null;
        _surfaces.Clear();
    }
}
