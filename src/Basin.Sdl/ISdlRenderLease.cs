using SkiaSharp;

namespace Basin.Sdl;

public interface ISdlRenderLease : IDisposable
{
    string Name { get; }

    bool Begin(out SKCanvas canvas, out GRContext? context, out int age);

    void Present();

    void Resize(int pixelWidth, int pixelHeight);

    bool ReadPixels(SKImageInfo info, nint pixels, int rowBytes);

    bool MakeCurrent();

    SdlHdrState Hdr { get; }

    bool TrySetHdr(in SdlHdrState state) => false;
}
