using System.Runtime.InteropServices;
using Basin.WindowManager;
using Wayland;

namespace Dinghy;

internal static class ShadowNineSlice
{
    private const int MaxCacheEntries = 8;

    private static readonly List<((int SizePx, int RadiusPx, uint Color) Key, Parts Value)> Cache = [];

    public static void Draw(
        Span<byte> pixels,
        int bufferWidth,
        int frameWidth,
        int frameHeight,
        int shadowSize,
        int cornerRadius,
        uint color,
        int scale)
    {
        if (shadowSize <= 0 || (byte)color == 0)
        {
            return;
        }

        var sPx = shadowSize * scale;
        var fwPx = frameWidth * scale;
        var fhPx = frameHeight * scale;
        if (sPx <= 0 || fwPx <= 0 || fhPx <= 0)
        {
            return;
        }

        var w = bufferWidth;
        var rPx = Math.Clamp(cornerRadius * scale, 0, Math.Max(Math.Min(fwPx, fhPx) / 2, 0));

        var m = (w - fwPx) / 2;
        if (m < sPx)
        {
            return;
        }

        var innerX1 = m + fwPx;
        var innerY1 = m + fhPx;

        var parts = PartsFor(sPx, rPx, color);
        var cs = parts.CornerSize;
        var pixels32 = MemoryMarshal.Cast<byte, uint>(pixels);
        var stride32 = w;

        var rightX0 = innerX1 - rPx;
        var bottomY0 = innerY1 - rPx;
        var cl = m + rPx - 1;

        for (var ty = 0; ty < cs; ty++)
        {
            for (var tx = 0; tx < cs; tx++)
            {
                var argb = parts.Corner[(ty * cs) + tx];
                if (argb >> 24 == 0)
                {
                    continue;
                }

                pixels32[((bottomY0 + ty) * stride32) + rightX0 + tx] = argb;
                pixels32[((cl - ty) * stride32) + rightX0 + tx] = argb;
                pixels32[((bottomY0 + ty) * stride32) + cl - tx] = argb;
                pixels32[((cl - ty) * stride32) + cl - tx] = argb;
            }
        }

        var spanLo = m + rPx;
        var spanHi = innerX1 - rPx;
        for (var i = 0; i < sPx; i++)
        {
            var argb = parts.Edge[i];
            if (argb >> 24 == 0)
            {
                continue;
            }

            pixels32.Slice(((m - sPx + i) * stride32) + spanLo, spanHi - spanLo).Fill(argb);
            pixels32.Slice(((innerY1 + sPx - 1 - i) * stride32) + spanLo, spanHi - spanLo).Fill(argb);
        }

        Span<uint> leftRun = sPx <= 256 ? stackalloc uint[sPx] : new uint[sPx];
        Span<uint> rightRun = sPx <= 256 ? stackalloc uint[sPx] : new uint[sPx];
        for (var i = 0; i < sPx; i++)
        {
            leftRun[i] = parts.Edge[i];
            rightRun[sPx - 1 - i] = parts.Edge[i];
        }

        for (var y = m + rPx; y < innerY1 - rPx; y++)
        {
            leftRun.CopyTo(pixels32.Slice((y * stride32) + m - sPx, sPx));
            rightRun.CopyTo(pixels32.Slice((y * stride32) + innerX1, sPx));
        }
    }

    private static Parts PartsFor(int sPx, int rPx, uint color)
    {
        var key = (sPx, rPx, color);
        foreach (var entry in Cache)
        {
            if (entry.Key == key)
            {
                return entry.Value;
            }
        }

        var parts = Build(sPx, rPx, color);
        Cache.Add((key, parts));
        if (Cache.Count > MaxCacheEntries)
        {
            Cache.RemoveAt(0);
        }

        return parts;
    }

    private static Parts Build(int sPx, int rPx, uint color)
    {
        var baseAlpha = (byte)color;
        var r = (byte)(color >> 24);
        var g = (byte)(color >> 16);
        var b = (byte)(color >> 8);
        var s = (float)sPx;

        uint ArgbFor(float dist)
        {
            if (dist <= 0f || dist > s)
            {
                return 0;
            }

            var falloff = 1f - (dist / s);
            var alpha = (uint)Math.Clamp(MathF.Round(baseAlpha * falloff * falloff), 0f, 255f);
            if (alpha == 0)
            {
                return 0;
            }

            uint Mul(byte channel) => channel * alpha / 255;
            return (alpha << 24) | (Mul(r) << 16) | (Mul(g) << 8) | Mul(b);
        }

        var edge = new uint[sPx];
        for (var i = 0; i < sPx; i++)
        {
            edge[i] = ArgbFor(s - i - 0.5f);
        }

        var cs = sPx + rPx;
        var corner = new uint[cs * cs];
        for (var idx = 0; idx < corner.Length; idx++)
        {
            var tx = (idx % cs) + 0.5f;
            var ty = (idx / cs) + 0.5f;
            corner[idx] = ArgbFor(MathF.Sqrt((tx * tx) + (ty * ty)) - rPx);
        }

        return new Parts(cs, edge, corner);
    }

    private sealed record Parts(int CornerSize, uint[] Edge, uint[] Corner);
}
