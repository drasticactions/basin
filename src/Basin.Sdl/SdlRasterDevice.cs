using SDL3;
using SkiaSharp;

namespace Basin.Sdl;

public sealed class SdlRasterDevice : ISdlRenderDevice
{
    public string Name => SdlRenderDevices.Raster;

    public SDL.WindowFlags WindowFlags => 0;

    public GRContext? Context => null;

    public nint EglDisplay => 0;

    public ISdlRenderLease CreateLease(SdlWindow window) => new SdlRasterLease(window);

    public void Dispose()
    {
    }
}
