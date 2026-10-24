using Basin.Sdl;
using SDL3;
using SkiaSharp;

namespace SdlComp.Tests;

internal sealed class FakeSdlDevice : ISdlRenderDevice
{
    private readonly SdlRasterDevice _inner = new();

    public string Name => SdlRenderDevices.Raster;

    public SDL.WindowFlags WindowFlags => _inner.WindowFlags;

    public GRContext? Context => null;

    public nint EglDisplay => 0;

    public FakeSdlLease? Lease { get; private set; }

    public ISdlRenderLease CreateLease(SdlWindow window) => Lease = new FakeSdlLease(new SdlRasterLease(window));

    public void Dispose() => _inner.Dispose();
}
