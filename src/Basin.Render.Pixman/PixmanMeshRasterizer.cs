using Pixman;

namespace Basin.Render.Pixman;

internal static unsafe class PixmanMeshRasterizer
{
    public static void Rasterize(
        nint targetData,
        int targetStride,
        int targetWidth,
        int targetHeight,
        nint sourceData,
        int sourceStride,
        int sourceWidth,
        int sourceHeight,
        bool sourceOpaque,
        bool hasTexture,
        ReadOnlySpan<MeshVertex> vertices,
        bool additive,
        PixmanRegion32? clip)
    {
        if (clip is null)
        {
            RasterizeRect(
                targetData, targetStride, sourceData, sourceStride, sourceWidth, sourceHeight, sourceOpaque,
                hasTexture, vertices, additive, 0, 0, targetWidth, targetHeight);
            return;
        }

        foreach (var band in RegionRects.Of(clip))
        {
            var x0 = Math.Max(0, band.X1);
            var y0 = Math.Max(0, band.Y1);
            var x1 = Math.Min(targetWidth, band.X2);
            var y1 = Math.Min(targetHeight, band.Y2);
            if (x1 > x0 && y1 > y0)
            {
                RasterizeRect(
                    targetData, targetStride, sourceData, sourceStride, sourceWidth, sourceHeight, sourceOpaque,
                    hasTexture, vertices, additive, x0, y0, x1, y1);
            }
        }
    }

    private static void RasterizeRect(
        nint targetData,
        int targetStride,
        nint sourceData,
        int sourceStride,
        int sourceWidth,
        int sourceHeight,
        bool sourceOpaque,
        bool hasTexture,
        ReadOnlySpan<MeshVertex> vertices,
        bool additive,
        int clipX0,
        int clipY0,
        int clipX1,
        int clipY1)
    {
        for (var triangle = 0; triangle + 2 < vertices.Length; triangle += 3)
        {
            var a = vertices[triangle];
            var b = vertices[triangle + 1];
            var c = vertices[triangle + 2];

            var ax = Snap(a.X);
            var ay = Snap(a.Y);
            var bx = Snap(b.X);
            var by = Snap(b.Y);
            var cx = Snap(c.X);
            var cy = Snap(c.Y);

            var area = ((bx - ax) * (cy - ay)) - ((by - ay) * (cx - ax));
            if (area == 0)
            {
                continue;
            }

            if (area < 0)
            {
                (b, c) = (c, b);
                (bx, cx) = (cx, bx);
                (by, cy) = (cy, by);
                area = -area;
            }

            var minX = Math.Max(clipX0, (int)(Math.Min(ax, Math.Min(bx, cx)) >> 4));
            var minY = Math.Max(clipY0, (int)(Math.Min(ay, Math.Min(by, cy)) >> 4));
            var maxX = Math.Min(clipX1, (int)((Math.Max(ax, Math.Max(bx, cx)) + 15) >> 4));
            var maxY = Math.Min(clipY1, (int)((Math.Max(ay, Math.Max(by, cy)) + 15) >> 4));
            if (minX >= maxX || minY >= maxY)
            {
                continue;
            }

            var biasA = EdgeIsTopLeft(ax, ay, bx, by) ? 0L : -1L;
            var biasB = EdgeIsTopLeft(bx, by, cx, cy) ? 0L : -1L;
            var biasC = EdgeIsTopLeft(cx, cy, ax, ay) ? 0L : -1L;

            var inverseArea = 1.0 / area;
            for (var y = minY; y < maxY; y++)
            {
                var py = (y << 4) + 8;
                var row = (uint*)(targetData + (y * targetStride));
                for (var x = minX; x < maxX; x++)
                {
                    var px = (x << 4) + 8;
                    var edgeA = ((bx - ax) * (py - ay)) - ((by - ay) * (px - ax));
                    var edgeB = ((cx - bx) * (py - by)) - ((cy - by) * (px - bx));
                    var edgeC = ((ax - cx) * (py - cy)) - ((ay - cy) * (px - cx));
                    if (edgeA + biasA < 0 || edgeB + biasB < 0 || edgeC + biasC < 0)
                    {
                        continue;
                    }

                    var wb = edgeC * inverseArea;
                    var wc = edgeA * inverseArea;
                    var wa = edgeB * inverseArea;

                    var red = (wa * a.Color.R) + (wb * b.Color.R) + (wc * c.Color.R);
                    var green = (wa * a.Color.G) + (wb * b.Color.G) + (wc * c.Color.G);
                    var blue = (wa * a.Color.B) + (wb * b.Color.B) + (wc * c.Color.B);
                    var alpha = (wa * a.Color.A) + (wb * b.Color.A) + (wc * c.Color.A);

                    if (hasTexture)
                    {
                        var u = (wa * a.U) + (wb * b.U) + (wc * c.U);
                        var v = (wa * a.V) + (wb * b.V) + (wc * c.V);
                        SampleBilinear(
                            sourceData, sourceStride, sourceWidth, sourceHeight, sourceOpaque, u, v,
                            out var tr, out var tg, out var tb, out var ta);
                        red *= tr;
                        green *= tg;
                        blue *= tb;
                        alpha *= ta;
                    }

                    var sr = (int)(Math.Clamp(red, 0, 1) * 255.0 + 0.5);
                    var sg = (int)(Math.Clamp(green, 0, 1) * 255.0 + 0.5);
                    var sb = (int)(Math.Clamp(blue, 0, 1) * 255.0 + 0.5);
                    var sa = (int)(Math.Clamp(alpha, 0, 1) * 255.0 + 0.5);

                    var dst = row[x];
                    var dr = (int)((dst >> 16) & 0xFF);
                    var dg = (int)((dst >> 8) & 0xFF);
                    var db = (int)(dst & 0xFF);
                    var da = (int)(dst >> 24);

                    int or, og, ob, oa;
                    if (additive)
                    {
                        or = Math.Min(255, dr + sr);
                        og = Math.Min(255, dg + sg);
                        ob = Math.Min(255, db + sb);
                        oa = Math.Min(255, da + sa);
                    }
                    else
                    {
                        var inverse = 255 - sa;
                        or = sr + (((dr * inverse) + 127) / 255);
                        og = sg + (((dg * inverse) + 127) / 255);
                        ob = sb + (((db * inverse) + 127) / 255);
                        oa = sa + (((da * inverse) + 127) / 255);
                    }

                    row[x] = ((uint)oa << 24) | ((uint)or << 16) | ((uint)og << 8) | (uint)ob;
                }
            }
        }
    }

    private static long Snap(float value) => (long)Math.Round(value * 16.0);

    private static bool EdgeIsTopLeft(long fromX, long fromY, long toX, long toY) =>
        (fromY == toY && toX > fromX) || toY < fromY;

    private static void SampleBilinear(
        nint data,
        int stride,
        int width,
        int height,
        bool opaque,
        double u,
        double v,
        out double red,
        out double green,
        out double blue,
        out double alpha)
    {
        var sx = u - 0.5;
        var sy = v - 0.5;
        var x0 = (int)Math.Floor(sx);
        var y0 = (int)Math.Floor(sy);
        var fx = sx - x0;
        var fy = sy - y0;

        var p00 = Fetch(data, stride, width, height, x0, y0, opaque);
        var p10 = Fetch(data, stride, width, height, x0 + 1, y0, opaque);
        var p01 = Fetch(data, stride, width, height, x0, y0 + 1, opaque);
        var p11 = Fetch(data, stride, width, height, x0 + 1, y0 + 1, opaque);

        var w00 = (1 - fx) * (1 - fy);
        var w10 = fx * (1 - fy);
        var w01 = (1 - fx) * fy;
        var w11 = fx * fy;

        red = ((((p00 >> 16) & 0xFF) * w00) + (((p10 >> 16) & 0xFF) * w10) +
               (((p01 >> 16) & 0xFF) * w01) + (((p11 >> 16) & 0xFF) * w11)) / 255.0;
        green = ((((p00 >> 8) & 0xFF) * w00) + (((p10 >> 8) & 0xFF) * w10) +
                 (((p01 >> 8) & 0xFF) * w01) + (((p11 >> 8) & 0xFF) * w11)) / 255.0;
        blue = (((p00 & 0xFF) * w00) + ((p10 & 0xFF) * w10) +
                ((p01 & 0xFF) * w01) + ((p11 & 0xFF) * w11)) / 255.0;
        alpha = (((p00 >> 24) * w00) + ((p10 >> 24) * w10) +
                 ((p01 >> 24) * w01) + ((p11 >> 24) * w11)) / 255.0;
    }

    private static uint Fetch(nint data, int stride, int width, int height, int x, int y, bool opaque)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        var pixel = *(uint*)(data + (y * stride) + (x * 4));
        return opaque ? pixel | 0xFF000000u : pixel;
    }
}
