using Basin.Capabilities;

namespace Basin.Video.MediaCodec;

internal static unsafe class PixelRows
{
    internal static bool IsSupported(DrmFormat format) => format switch
    {
        DrmFormat.Xrgb8888 or DrmFormat.Argb8888 => true,
        DrmFormat.Xbgr8888 or DrmFormat.Abgr8888 => true,
        DrmFormat.Xrgb2101010 or DrmFormat.Xbgr2101010 => true,
        _ => false,
    };

    internal static void Copy(
        byte* source, long sourceStride, nint destination, int stride, int width, int height, DrmFormat format)
    {
        for (var row = 0; row < height; row++)
        {
            var from = source + (row * sourceStride);
            var to = (byte*)destination + ((long)row * stride);
            switch (format)
            {
                case DrmFormat.Xrgb8888:
                case DrmFormat.Argb8888:
                    Buffer.MemoryCopy(from, to, (long)width * 4, (long)width * 4);
                    break;

                case DrmFormat.Xbgr8888:
                case DrmFormat.Abgr8888:
                    for (var x = 0; x < width; x++)
                    {
                        to[(x * 4) + 0] = from[(x * 4) + 2];
                        to[(x * 4) + 1] = from[(x * 4) + 1];
                        to[(x * 4) + 2] = from[(x * 4) + 0];
                        to[(x * 4) + 3] = from[(x * 4) + 3];
                    }

                    break;

                case DrmFormat.Xrgb2101010:
                    for (var x = 0; x < width; x++)
                    {
                        var b = (uint)from[(x * 4) + 0];
                        var g = (uint)from[(x * 4) + 1];
                        var r = (uint)from[(x * 4) + 2];
                        ((uint*)to)[x] = (3u << 30) | (Widen(r) << 20) | (Widen(g) << 10) | Widen(b);
                    }

                    break;

                case DrmFormat.Xbgr2101010:
                    for (var x = 0; x < width; x++)
                    {
                        var b = (uint)from[(x * 4) + 0];
                        var g = (uint)from[(x * 4) + 1];
                        var r = (uint)from[(x * 4) + 2];
                        ((uint*)to)[x] = (3u << 30) | (Widen(b) << 20) | (Widen(g) << 10) | Widen(r);
                    }

                    break;

                default:
                    throw new NotSupportedException($"MediaCodec frames are not packed into {format}");
            }
        }
    }

    private static uint Widen(uint eightBit) => (eightBit << 2) | (eightBit >> 6);
}
