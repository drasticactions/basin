using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.UI.Skia;

/// <summary>
/// The CPU Skia chrome host. It makes <see cref="SkiaUISurface"/> instances in memory buffers. The caller draws them, so the host has no work to pump.
/// </summary>
public sealed class SkiaUIHost : IUIHost
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private bool _disposed;

    /// <summary>
    /// Gets <see cref="ISkiaUISurface"/>, which every surface of the host implements.
    /// </summary>
    public Type SurfaceContract => typeof(ISkiaUISurface);

    /// <summary>
    /// Gets <see cref="UITargetKind.Memory"/>, the only kind that the host produces.
    /// </summary>
    public UITargetKind Produces => UITargetKind.Memory;

    /// <summary>
    /// Gets <see langword="null"/>, because the host never has work due.
    /// </summary>
    public long? NextDueMillis => null;

    /// <summary>
    /// Never occurs. The host has no work of its own.
    /// </summary>
    public event Action? WakeupRequested
    {
        add
        {
        }

        remove
        {
        }
    }

    /// <summary>
    /// Creates a configured <see cref="SkiaUISurface"/>. The caller owns the surface and must dispose it.
    /// </summary>
    /// <param name="options">The target kind, the logical size and the scale.</param>
    /// <returns>The surface, or <see langword="null"/> when the host is disposed, the kind is not <see cref="UITargetKind.Memory"/>, or the size is not valid.</returns>
    public IUISurface? CreateSurface(in UISurfaceOptions options)
    {
        _thread.Assert();
        if (_disposed || options.Target != UITargetKind.Memory)
        {
            return null;
        }

        var surface = new SkiaUISurface();
        if (!surface.Configure(options.Width, options.Height, options.Scale))
        {
            surface.Dispose();
            return null;
        }

        return surface;
    }

    /// <summary>
    /// Does nothing. The host has no work of its own.
    /// </summary>
    public void Pump()
    {
    }

    /// <summary>
    /// Stops the host from making surfaces. Surfaces that it made stay valid.
    /// </summary>
    public void Dispose()
    {
        _thread.Assert();
        _disposed = true;
    }
}
