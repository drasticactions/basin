using System.Diagnostics.CodeAnalysis;

namespace Basin.Diagnostics;

/// <summary>
/// Reads the pixels of a buffer for screenshots and tests. These calls allocate and must not run on the frame path.
/// </summary>
public static class BufferCapture
{
    /// <summary>
    /// Reads a buffer that has a CPU mapping as RGBA bytes. It throws <see cref="InvalidOperationException"/> when the buffer has no CPU mapping.
    /// </summary>
    /// <param name="buffer">The buffer to read.</param>
    /// <returns>Tightly packed RGBA bytes, 4 per pixel.</returns>
    public static byte[] ReadRgba(IBuffer buffer) =>
        TryReadRgba(buffer, out var rgba)
            ? rgba
            : throw new InvalidOperationException("Buffer has no CPU mapping.");

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
    /// Reads a buffer that has a CPU mapping as RGBA bytes. Each pixel is read as a 32-bit ARGB or XRGB word, whatever the actual format.
    /// </summary>
    /// <param name="buffer">The buffer to read.</param>
    /// <param name="rgba">Tightly packed RGBA bytes, or <see langword="null"/> on failure.</param>
    /// <returns><see langword="false"/> when the buffer has no CPU mapping.</returns>
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

    /// <summary>
    /// Reads a buffer as RGBA bytes. When the buffer has no CPU mapping, the renderer copies it into a memory buffer first.
    /// </summary>
    /// <param name="buffer">The buffer to read.</param>
    /// <param name="renderer">The renderer that imports the buffer when a direct read is not possible.</param>
    /// <param name="rgba">Tightly packed RGBA bytes, or <see langword="null"/> on failure.</param>
    /// <returns><see langword="false"/> when no CPU mapping exists and the renderer cannot import the buffer.</returns>
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
