using SDL3;
using SkiaSharp;

namespace Basin.Sdl;

/// <summary>
/// The render device for the <see cref="SdlRenderDevices.Raster"/> name. It has no GPU context and no shared state.
/// </summary>
public sealed class SdlRasterDevice : ISdlRenderDevice
{
    /// <inheritdoc/>
    public string Name => SdlRenderDevices.Raster;

    /// <inheritdoc/>
    public SDL.WindowFlags WindowFlags => 0;

    /// <inheritdoc/>
    public GRContext? Context => null;

    /// <inheritdoc/>
    public nint EglDisplay => 0;

    /// <inheritdoc/>
    public ISdlRenderLease CreateLease(SdlWindow window) => new SdlRasterLease(window);

    /// <summary>
    /// Does nothing, because the device holds no resources.
    /// </summary>
    public void Dispose()
    {
    }
}
