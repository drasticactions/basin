using System.Runtime.Intrinsics;
using Basin.Capabilities;

namespace Basin.Video.VideoToolbox;

internal static unsafe class PixelRows
{
    private const uint Alpha10 = 3u << 30;

    private static readonly Vector128<byte> SwapRedBlue = Vector128.Create(
        (byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);

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
                    SwapRow(from, to, width);
                    break;

                case DrmFormat.Xrgb2101010:
                    WidenRow(from, (uint*)to, width, redHigh: true);
                    break;

                case DrmFormat.Xbgr2101010:
                    WidenRow(from, (uint*)to, width, redHigh: false);
                    break;

                default:
                    throw new NotSupportedException($"VideoToolbox frames are not packed into {format}");
            }
        }
    }

    private static void SwapRow(byte* from, byte* to, int width)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            for (; x + 4 <= width; x += 4)
            {
                var pixels = Vector128.Load(from + (x * 4));
                Vector128.Shuffle(pixels, SwapRedBlue).Store(to + (x * 4));
            }
        }

        for (; x < width; x++)
        {
            to[(x * 4) + 0] = from[(x * 4) + 2];
            to[(x * 4) + 1] = from[(x * 4) + 1];
            to[(x * 4) + 2] = from[(x * 4) + 0];
            to[(x * 4) + 3] = from[(x * 4) + 3];
        }
    }

    private static void WidenRow(byte* from, uint* to, int width, bool redHigh)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var mask = Vector128.Create(0xffu);
            var alpha = Vector128.Create(Alpha10);
            for (; x + 4 <= width; x += 4)
            {
                var pixels = Vector128.Load((uint*)(from + (x * 4)));
                var b = Widen(pixels & mask);
                var g = Widen(Vector128.ShiftRightLogical(pixels, 8) & mask);
                var r = Widen(Vector128.ShiftRightLogical(pixels, 16) & mask);
                var high = redHigh ? r : b;
                var low = redHigh ? b : r;
                var packed = alpha | Vector128.ShiftLeft(high, 20) | Vector128.ShiftLeft(g, 10) | low;
                packed.Store(to + x);
            }
        }

        for (; x < width; x++)
        {
            var b = (uint)from[(x * 4) + 0];
            var g = (uint)from[(x * 4) + 1];
            var r = (uint)from[(x * 4) + 2];
            var high = redHigh ? r : b;
            var low = redHigh ? b : r;
            to[x] = Alpha10 | (Widen(high) << 20) | (Widen(g) << 10) | Widen(low);
        }
    }

    private static Vector128<uint> Widen(Vector128<uint> eightBit) =>
        Vector128.ShiftLeft(eightBit, 2) | Vector128.ShiftRightLogical(eightBit, 6);

    private static uint Widen(uint eightBit) => (eightBit << 2) | (eightBit >> 6);
}
