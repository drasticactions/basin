namespace Basin.Video.MediaCodec;

internal static unsafe class Yuv420ToBgra
{
    internal static void ConvertPlanar(
        byte* y, long yStride, byte* u, byte* v, long chromaStride, byte* bgra, long bgraStride, int width, int height)
    {
        for (var row = 0; row < height; row++)
        {
            var luma = y + (row * yStride);
            var cb = u + ((row >> 1) * chromaStride);
            var cr = v + ((row >> 1) * chromaStride);
            var to = bgra + (row * bgraStride);
            for (var x = 0; x < width; x++)
            {
                Store(to + (x * 4), luma[x], cb[x >> 1], cr[x >> 1]);
            }
        }
    }

    internal static void ConvertSemiPlanar(
        byte* y, long yStride, byte* uv, long uvStride, bool crFirst, byte* bgra, long bgraStride, int width, int height)
    {
        for (var row = 0; row < height; row++)
        {
            var luma = y + (row * yStride);
            var chroma = uv + ((row >> 1) * uvStride);
            var to = bgra + (row * bgraStride);
            for (var x = 0; x < width; x++)
            {
                var pair = chroma + ((x >> 1) * 2);
                if (crFirst)
                {
                    Store(to + (x * 4), luma[x], pair[1], pair[0]);
                }
                else
                {
                    Store(to + (x * 4), luma[x], pair[0], pair[1]);
                }
            }
        }
    }

    private static void Store(byte* pixel, int y, int cb, int cr)
    {
        var luma = 298 * (y - 16);
        var d = cb - 128;
        var e = cr - 128;
        pixel[0] = Clamp((luma + (516 * d) + 128) >> 8);
        pixel[1] = Clamp((luma - (100 * d) - (208 * e) + 128) >> 8);
        pixel[2] = Clamp((luma + (409 * e) + 128) >> 8);
        pixel[3] = 0xff;
    }

    private static byte Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
