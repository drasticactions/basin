using System.Diagnostics.CodeAnalysis;

namespace Basin.Diagnostics;

public static class BufferCapture
{
    public static byte[] ReadRgba(IBuffer buffer) =>
        TryReadRgba(buffer, out var rgba)
            ? rgba
            : throw new InvalidOperationException("Buffer has no CPU mapping.");

    public static bool TryReadRgba(IBuffer buffer, [NotNullWhen(true)] out byte[]? rgba)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (!buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            rgba = null;
            return false;
        }

        try
        {
            rgba = Unpack(view, buffer.Width, buffer.Height);
            return true;
        }
        finally
        {
            buffer.EndDataAccess();
        }
    }

    public static bool TryReadRgba(IBuffer buffer, IRenderer renderer, [NotNullWhen(true)] out byte[]? rgba)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(renderer);
        if (TryReadRgba(buffer, out rgba))
        {
            return true;
        }

        if (renderer.ImportTexture(buffer) is not { } texture)
        {
            return false;
        }

        var copy = new MemoryBuffer(buffer.Width, buffer.Height, DrmFormat.Xrgb8888);
        try
        {
            var pass = renderer.BeginBufferPass(copy, new RenderPassOptions());
            pass.AddTexture(texture, new TextureRenderOptions
            {
                DstBox = new Box(0, 0, buffer.Width, buffer.Height),
            });
            pass.Submit();
            return TryReadRgba(copy, out rgba);
        }
        finally
        {
            texture.Dispose();
            copy.Destroy();
        }
    }

    public static void WritePng(IBuffer buffer, string path) =>
        File.WriteAllBytes(path, PngCodec.Encode(ReadRgba(buffer), buffer.Width, buffer.Height));

    public static bool TryWritePng(IBuffer buffer, IRenderer renderer, string path)
    {
        if (!TryReadRgba(buffer, renderer, out var rgba))
        {
            return false;
        }

        File.WriteAllBytes(path, PngCodec.Encode(rgba, buffer.Width, buffer.Height));
        return true;
    }

    private static unsafe byte[] Unpack(in BufferDataView view, int width, int height)
    {
        var opaque = !view.Format.HasAlpha();
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var row = (uint*)(view.Data + (y * view.Stride));
            for (var x = 0; x < width; x++)
            {
                var pixel = row[x];
                var i = ((y * width) + x) * 4;
                rgba[i] = (byte)(pixel >> 16);
                rgba[i + 1] = (byte)(pixel >> 8);
                rgba[i + 2] = (byte)pixel;
                rgba[i + 3] = opaque ? (byte)0xFF : (byte)(pixel >> 24);
            }
        }

        return rgba;
    }
}
