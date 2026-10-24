using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;

namespace Basin.Ipc;

/// <summary>
/// The outcome of a capture call. Which members are set depends on the <see cref="IpcCaptureTarget"/>.
/// </summary>
/// <param name="Width">The width in pixels, after scaling for a path or inline target.</param>
/// <param name="Height">The height in pixels, after scaling for a path or inline target.</param>
/// <param name="Path">The file that the compositor wrote, for a path target.</param>
/// <param name="Png">The PNG bytes, for an inline target.</param>
/// <param name="Fd">The memfd of raw pixels, for a descriptor target. The caller owns it and must dispose it.</param>
/// <param name="Format">The pixel format of the descriptor, for example <c>xrgb8888</c>.</param>
/// <param name="Stride">The bytes per row in the descriptor, or 0 for other targets.</param>
public sealed record IpcCapture(
    int Width, int Height, string? Path, byte[]? Png, SafeFileHandle? Fd, string? Format, int Stride)
{
    /// <summary>
    /// Reads the descriptor's 32-bit little-endian ARGB pixels into tightly packed RGBA. An <c>x</c> format reads as opaque.
    /// </summary>
    /// <returns>A new array of <see cref="Width"/> times <see cref="Height"/> times 4 bytes.</returns>
    /// <exception cref="InvalidOperationException">The capture has no descriptor.</exception>
    /// <exception cref="InvalidDataException">The size or stride cannot hold 32-bit pixels.</exception>
    /// <exception cref="EndOfStreamException">The descriptor ended before every row was read.</exception>
    public byte[] ReadRgba()
    {
        if (Fd is not { IsInvalid: false } fd)
        {
            throw new InvalidOperationException("The capture has no descriptor. Ask for IpcCaptureTarget.Fd.");
        }

        if (Width <= 0 || Height <= 0 || Stride < Width * 4)
        {
            throw new InvalidDataException($"The {Width}x{Height} capture with stride {Stride} cannot be read as 32-bit pixels.");
        }

        var pixels = new byte[Stride * Height];
        var read = 0;
        while (read < pixels.Length)
        {
            var got = RandomAccess.Read(fd, pixels.AsSpan(read), read);
            if (got <= 0)
            {
                throw new EndOfStreamException($"The capture descriptor ended after {read} of {pixels.Length} bytes.");
            }

            read += got;
        }

        var opaque = Format?.StartsWith('x') == true;
        var rgba = new byte[Width * Height * 4];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var pixel = BinaryPrimitives.ReadUInt32LittleEndian(pixels.AsSpan((y * Stride) + (x * 4)));
                var i = ((y * Width) + x) * 4;
                rgba[i] = (byte)(pixel >> 16);
                rgba[i + 1] = (byte)(pixel >> 8);
                rgba[i + 2] = (byte)pixel;
                rgba[i + 3] = opaque ? (byte)0xFF : (byte)(pixel >> 24);
            }
        }

        return rgba;
    }
}
