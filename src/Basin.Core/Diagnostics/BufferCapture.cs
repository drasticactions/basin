using System.Diagnostics.CodeAnalysis;

namespace Basin.Diagnostics;

/// <summary>
/// Reads the pixels of a buffer for screenshots and tests. These calls allocate and must not run on the frame path.
/// </summary>
public static class BufferCapture
{
    /// <summary>
    /// Reads a buffer that has a CPU mapping as RGBA bytes. It throws <see cref="InvalidOperationException"/> when the buffer has no CPU mapping or a format that <see cref="TryReadRgba(IBuffer, out byte[])"/> cannot unpack. Use <see cref="TryReadRgba(IBuffer, IRenderer, out byte[])"/> to read other formats through the renderer copy.
    /// </summary>
    /// <param name="buffer">The buffer to read.</param>
    /// <returns>Tightly packed RGBA bytes, 4 per pixel.</returns>
    public static byte[] ReadRgba(IBuffer buffer) =>
        TryReadRgba(buffer, out var rgba)
            ? rgba
            : throw new InvalidOperationException(
                CanUnpack(buffer.Format)
                    ? "Buffer has no CPU mapping."
                    : $"Buffer format {buffer.Format} cannot be read directly. Read it through a renderer.");

    /// <summary>
    /// Reads a half-float buffer as 16-bit RGB samples. Values are clamped to the range 0 to 1 before the conversion.
    /// </summary>
    /// <param name="buffer">The buffer to read. Its format must be <see cref="DrmFormat.Abgr16161616f"/> or <see cref="DrmFormat.Xbgr16161616f"/>.</param>
    /// <param name="rgb">Tightly packed RGB samples, 3 per pixel, or <see langword="null"/> on failure.</param>
    /// <returns><see langword="false"/> when the format does not match or the buffer has no CPU mapping.</returns>
    public static unsafe bool TryReadRgb16(IBuffer buffer, [NotNullWhen(true)] out ushort[]? rgb)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        rgb = null;
        if (buffer.Format is not (DrmFormat.Abgr16161616f or DrmFormat.Xbgr16161616f) ||
            !buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            return false;
        }

        try
        {
            var result = new ushort[buffer.Width * buffer.Height * 3];
            for (var y = 0; y < buffer.Height; y++)
            {
                var row = (Half*)(view.Data + (y * view.Stride));
                for (var x = 0; x < buffer.Width; x++)
                {
                    for (var c = 0; c < 3; c++)
                    {
                        var value = Math.Clamp((float)row[(x * 4) + c], 0f, 1f);
                        result[(((y * buffer.Width) + x) * 3) + c] = (ushort)MathF.Round(value * 65535f);
                    }
                }
            }

            rgb = result;
            return true;
        }
        finally
        {
            buffer.EndDataAccess();
        }
    }

    /// <summary>
    /// Reads a buffer that has a CPU mapping as RGBA bytes. It unpacks the 8-bit RGB formats in both channel orders, the 10-bit RGB formats, <see cref="DrmFormat.Rgb565"/> and the half-float formats. Half-float values are clamped to the range 0 to 1.
    /// </summary>
    /// <param name="buffer">The buffer to read.</param>
    /// <param name="rgba">Tightly packed RGBA bytes, or <see langword="null"/> on failure.</param>
    /// <returns><see langword="false"/> when the buffer has no CPU mapping, has another format, for example a YUV format, or has a stride too small for its width.</returns>
    public static bool TryReadRgba(IBuffer buffer, [NotNullWhen(true)] out byte[]? rgba)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        rgba = null;
        if (!CanUnpack(buffer.Format) || !buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            return false;
        }

        try
        {
            if (!CanUnpack(view.Format) || view.Stride < buffer.Width * view.Format.BytesPerPixel())
            {
                return false;
            }

            rgba = Unpack(view, buffer.Width, buffer.Height);
            return true;
        }
        finally
        {
            buffer.EndDataAccess();
        }
    }

    /// <summary>
    /// Reads a buffer as RGBA bytes. When the buffer has no CPU mapping, or has a format that <see cref="TryReadRgba(IBuffer, out byte[])"/> cannot unpack, the renderer copies it into a memory buffer first.
    /// </summary>
    /// <param name="buffer">The buffer to read.</param>
    /// <param name="renderer">The renderer that imports the buffer when a direct read is not possible.</param>
    /// <param name="rgba">Tightly packed RGBA bytes, or <see langword="null"/> on failure.</param>
    /// <returns><see langword="false"/> when the direct read fails and the renderer cannot import the buffer.</returns>
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

        var whole = new Box(0, 0, buffer.Width, buffer.Height);
        var copy = new MemoryBuffer(
            buffer.Width, buffer.Height, buffer.Format.HasAlpha() ? DrmFormat.Argb8888 : DrmFormat.Xrgb8888);
        try
        {
            var pass = renderer.BeginBufferPass(copy, new RenderPassOptions());
            pass.AddRect(new RenderColor(0f, 0f, 0f, 0f), whole);
            pass.AddTexture(texture, new TextureRenderOptions { DstBox = whole });
            pass.Submit();
            return TryReadRgba(copy, out rgba);
        }
        finally
        {
            texture.Dispose();
            copy.Destroy();
        }
    }

    /// <summary>
    /// Writes a buffer that has a CPU mapping to a PNG file. It throws <see cref="InvalidOperationException"/> when the buffer has no CPU mapping.
    /// </summary>
    /// <param name="buffer">The buffer to write.</param>
    /// <param name="path">The destination file. An existing file is replaced.</param>
    public static void WritePng(IBuffer buffer, string path) =>
        File.WriteAllBytes(path, PngCodec.Encode(ReadRgba(buffer), buffer.Width, buffer.Height));

    /// <summary>
    /// Writes a buffer to a PNG file, with a renderer copy when the buffer has no CPU mapping.
    /// </summary>
    /// <param name="buffer">The buffer to write.</param>
    /// <param name="renderer">The renderer that imports the buffer when a direct read is not possible.</param>
    /// <param name="path">The destination file. An existing file is replaced.</param>
    /// <returns><see langword="false"/> when the pixels cannot be read. No file is written then.</returns>
    public static bool TryWritePng(IBuffer buffer, IRenderer renderer, string path)
    {
        if (!TryReadRgba(buffer, renderer, out var rgba))
        {
            return false;
        }

        File.WriteAllBytes(path, PngCodec.Encode(rgba, buffer.Width, buffer.Height));
        return true;
    }

    private static bool CanUnpack(DrmFormat format) => format is
        DrmFormat.Argb8888 or DrmFormat.Xrgb8888 or DrmFormat.Abgr8888 or DrmFormat.Xbgr8888 or
        DrmFormat.Argb2101010 or DrmFormat.Xrgb2101010 or DrmFormat.Abgr2101010 or DrmFormat.Xbgr2101010 or
        DrmFormat.Rgb565 or DrmFormat.Abgr16161616f or DrmFormat.Xbgr16161616f;

    private static unsafe byte[] Unpack(in BufferDataView view, int width, int height)
    {
        var format = view.Format;
        var opaque = !format.HasAlpha();
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            var line = view.Data + (y * view.Stride);
            for (var x = 0; x < width; x++)
            {
                var i = ((y * width) + x) * 4;
                byte r, g, b, a;
                switch (format)
                {
                    case DrmFormat.Rgb565:
                        var short565 = ((ushort*)line)[x];
                        var r5 = (short565 >> 11) & 0x1F;
                        var g6 = (short565 >> 5) & 0x3F;
                        var b5 = short565 & 0x1F;
                        r = (byte)((r5 << 3) | (r5 >> 2));
                        g = (byte)((g6 << 2) | (g6 >> 4));
                        b = (byte)((b5 << 3) | (b5 >> 2));
                        a = 0xFF;
                        break;
                    case DrmFormat.Abgr16161616f or DrmFormat.Xbgr16161616f:
                        var half = (Half*)line + (x * 4);
                        r = UnitToByte((float)half[0]);
                        g = UnitToByte((float)half[1]);
                        b = UnitToByte((float)half[2]);
                        a = UnitToByte((float)half[3]);
                        break;
                    case DrmFormat.Argb2101010 or DrmFormat.Xrgb2101010:
                        var argb10 = ((uint*)line)[x];
                        r = (byte)((argb10 >> 22) & 0xFF);
                        g = (byte)((argb10 >> 12) & 0xFF);
                        b = (byte)((argb10 >> 2) & 0xFF);
                        a = (byte)((argb10 >> 30) * 85);
                        break;
                    case DrmFormat.Abgr2101010 or DrmFormat.Xbgr2101010:
                        var abgr10 = ((uint*)line)[x];
                        r = (byte)((abgr10 >> 2) & 0xFF);
                        g = (byte)((abgr10 >> 12) & 0xFF);
                        b = (byte)((abgr10 >> 22) & 0xFF);
                        a = (byte)((abgr10 >> 30) * 85);
                        break;
                    case DrmFormat.Abgr8888 or DrmFormat.Xbgr8888:
                        var abgr = ((uint*)line)[x];
                        r = (byte)abgr;
                        g = (byte)(abgr >> 8);
                        b = (byte)(abgr >> 16);
                        a = (byte)(abgr >> 24);
                        break;
                    default:
                        var argb = ((uint*)line)[x];
                        r = (byte)(argb >> 16);
                        g = (byte)(argb >> 8);
                        b = (byte)argb;
                        a = (byte)(argb >> 24);
                        break;
                }

                rgba[i] = r;
                rgba[i + 1] = g;
                rgba[i + 2] = b;
                rgba[i + 3] = opaque ? (byte)0xFF : a;
            }
        }

        return rgba;
    }

    private static byte UnitToByte(float value) => (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);
}
