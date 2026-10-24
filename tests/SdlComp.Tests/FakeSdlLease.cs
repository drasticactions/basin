using Basin.Sdl;
using SkiaSharp;

namespace SdlComp.Tests;

internal sealed class FakeSdlLease(SdlRasterLease inner) : ISdlRenderLease
{
    private SdlHdrState _requested = SdlHdrState.Sdr;

    public string Name => inner.Name;

    public SdlHdrState Hdr { get; private set; } = SdlHdrState.Sdr;

    public int Begins { get; private set; }

    public int Presents { get; private set; }

    public int Discards { get; private set; }

    public bool TrySetHdr(in SdlHdrState state)
    {
        _requested = state;
        return true;
    }

    public bool Begin(out SKCanvas canvas, out GRContext? context, out int age)
    {
        Hdr = _requested;
        Begins++;
        return inner.Begin(out canvas, out context, out age);
    }

    public void Present()
    {
        Presents++;
        inner.Present();
    }

    public void Discard() => Discards++;

    public void Resize(int pixelWidth, int pixelHeight) => inner.Resize(pixelWidth, pixelHeight);

    public bool ReadPixels(SKImageInfo info, nint pixels, int rowBytes) => inner.ReadPixels(info, pixels, rowBytes);

    public bool MakeCurrent() => inner.MakeCurrent();

    public void Dispose() => inner.Dispose();
}
