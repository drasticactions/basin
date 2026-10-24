namespace Basin.Effects;

/// <summary>
/// Evaluates <see cref="GridGlowShader"/> on the CPU in doubles. Tests hold the shader rows and the warp inverse to it.
/// </summary>
public sealed class GridGlowField : GridGlowTwin
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GridGlowField"/> class.
    /// </summary>
    /// <param name="uniforms">The values that <see cref="GridGlowShader.Pack"/> wrote.</param>
    public GridGlowField(ReadOnlySpan<PixelShaderUniformValue> uniforms)
        : base(uniforms, GridGlowShader.UniformCount, 8, 18)
    {
    }

    private V W0a => U(0);

    private V W0b => U(1);

    private V W1a => U(2);

    private V W1b => U(3);

    private V W2a => U(4);

    private V W2b => U(5);

    private V W3a => U(6);

    private V W3b => U(7);

    private static bool On(V a) => a.Y > 0 && a.Z > 0;

    private static bool Has(V a, double dir, double x) => On(a) && dir * (x - a.X) > 0;

    private static bool Inside(V a, double dir, double x) => !On(a) || dir * (x - a.X) < 0;

    private static double Dist(V a, V b, double u)
    {
        var p1 = b.X + 1.0;
        return a.Z * ((a.W * u) + ((1.0 - a.W) * (1.0 - Math.Pow(Math.Max(1.0 - u, 0.0), p1)) / p1));
    }

    private static double ScaleU(V a, V b, double u) =>
        u <= 0 ? 1.0 : u >= 1 ? a.W : a.W + ((1.0 - a.W) * Math.Pow(1.0 - u, b.X));

    private static double Scale(V a, V b, double dir, double x) => On(a) ? ScaleU(a, b, dir * (x - a.X) / a.Z) : 1.0;

    private static double ToScreen(V a, V b, double dir, double x)
    {
        if (!On(a))
        {
            return x;
        }

        var u = dir * (x - a.X) / a.Z;
        if (u <= 0)
        {
            return x;
        }

        if (u >= 1)
        {
            return a.X + (dir * (a.Y + (a.W * (u - 1.0) * a.Z)));
        }

        return a.X + (dir * Dist(a, b, u));
    }

    private static double ToCanvas(V a, V b, double dir, double x)
    {
        if (!On(a))
        {
            return x;
        }

        var d = dir * (x - a.X);
        if (d <= 0)
        {
            return x;
        }

        if (d >= a.Y)
        {
            return a.X + (dir * a.Z) + (dir * (d - a.Y) / a.W);
        }

        var u = Math.Clamp(d / a.Z, 0.0, 1.0);
        for (var i = 0; i < 12; i++)
        {
            u = Math.Clamp(u - ((Dist(a, b, u) - d) / (a.Z * ScaleU(a, b, u))), 0.0, 1.0);
        }

        return a.X + (dir * u * a.Z);
    }

    private static double SmoothStep(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return t * t * (3.0 - (2.0 * t));
    }

    private static double FanT(V a, V b, double t)
    {
        if (b.W > 0.5)
        {
            if (t <= 0)
            {
                return 1.0;
            }

            if (t >= 1)
            {
                return a.W;
            }

            var bump = 16.0 * t * t * (1.0 - t) * (1.0 - t);
            return 1.0 + ((a.W - 1.0) * SmoothStep(t)) + (b.Y * bump);
        }

        return 1.0 + (b.Y * SmoothStep(t));
    }

    private static double Fan(V a, V b, double dir, double x)
    {
        if (!On(a) || (b.Y == 0 && b.W < 0.5))
        {
            return 1.0;
        }

        var u = dir * (x - a.X) / a.Z;
        if (u <= 0)
        {
            return 1.0;
        }

        if (u >= 1)
        {
            return b.W > 0.5 ? a.W : 1.0 + b.Y;
        }

        return FanT(a, b, Dist(a, b, u) / a.Y);
    }

    private static double FanScreen(V a, V b, double dir, double x)
    {
        if (!On(a) || (b.Y == 0 && b.W < 0.5))
        {
            return 1.0;
        }

        var d = dir * (x - a.X);
        if (d <= 0)
        {
            return 1.0;
        }

        if (d >= a.Y)
        {
            return b.W > 0.5 ? a.W : 1.0 + b.Y;
        }

        return FanT(a, b, d / a.Y);
    }

    private static double Depth(V a, V b, double dir, double x)
    {
        if (!On(a))
        {
            return 0.0;
        }

        var u = dir * (x - a.X) / a.Z;
        if (u <= 0)
        {
            return 0.0;
        }

        return u >= 1 ? 1.0 : Dist(a, b, u) / a.Y;
    }

    private static double DepthScreen(V a, double dir, double x) => On(a) ? Math.Clamp(dir * (x - a.X) / a.Y, 0.0, 1.0) : 0.0;

    private int SideAt(double x) => Has(W0a, -1, x) ? 1 : Has(W1a, 1, x) ? 2 : 0;

    private int EndAt(double y) => Has(W2a, -1, y) ? 3 : Has(W3a, 1, y) ? 4 : 0;

    private void Warp(int k, out V a, out V b, out double dir)
    {
        a = k == 1 ? W0a : k == 2 ? W1a : k == 3 ? W2a : W3a;
        b = k == 1 ? W0b : k == 2 ? W1b : k == 3 ? W2b : W3b;
        dir = k is 1 or 3 ? -1.0 : 1.0;
    }

    private double RowFan(double center, double fan, double depth)
    {
        fan = Math.Min(fan, S(1).Z);
        if (On(W2a) && center > W2a.X)
        {
            fan = Math.Min(fan, 1.0 + (0.5 * W2a.Y * depth / (center - W2a.X)));
        }

        if (On(W3a) && W3a.X > center)
        {
            fan = Math.Min(fan, 1.0 + (0.5 * W3a.Y * depth / (W3a.X - center)));
        }

        return fan;
    }

    private double ColumnFan(double center, double fan, double depth)
    {
        fan = Math.Min(fan, S(1).W);
        if (On(W0a) && center > W0a.X)
        {
            fan = Math.Min(fan, 1.0 + (0.5 * W0a.Y * depth / (center - W0a.X)));
        }

        if (On(W1a) && W1a.X > center)
        {
            fan = Math.Min(fan, 1.0 + (0.5 * W1a.Y * depth / (W1a.X - center)));
        }

        return fan;
    }

    private double Radius(double level) => S(1).Y > 0.5 ? S(1).X * (1.0 - Math.Clamp(level, 0.0, 1.0)) : S(1).X;

    private V Ring(V sa, V sb, double sd, V ea, V eb, double ed, double depth)
    {
        var sideCanvas = sa.X + (sd * depth * sa.Z);
        var edgeX = ToScreen(sa, sb, sd, sideCanvas);
        var edgeY = sb.Z + ((ea.X - sb.Z) * RowFan(sb.Z, Fan(sa, sb, sd, sideCanvas), Depth(sa, sb, sd, sideCanvas)));
        var endCanvas = ea.X + (ed * depth * ea.Z);
        var rimY = ToScreen(ea, eb, ed, endCanvas);
        var rimX = eb.Z + ((sa.X - eb.Z) * ColumnFan(eb.Z, Fan(ea, eb, ed, endCanvas), Depth(ea, eb, ed, endCanvas)));
        return new V(edgeX, edgeY, rimX, rimY);
    }

    private double Outside(V sa, V sb, double sd, V ea, V eb, double ed, double px, double py, double depth)
    {
        var ring = Ring(sa, sb, sd, ea, eb, ed, depth);
        var spanY = ring.Y - ring.W;
        var spanX = ring.Z - ring.X;
        if (spanX == 0 || spanY == 0)
        {
            return 1.0;
        }

        var toEdge = (py - ring.W) / spanY;
        var toRim = (px - ring.X) / spanX;
        if (toEdge < 0 || toRim < 0)
        {
            return 1.0;
        }

        var radius = Radius(depth);
        if (toEdge >= radius || toRim >= radius)
        {
            return -1.0;
        }

        var dx = toRim - radius;
        var dy = toEdge - radius;
        return (dx * dx) + (dy * dy) - (radius * radius);
    }

    private (double X, double Y) Corner(V sa, V sb, double sd, V ea, V eb, double ed, double px, double py)
    {
        double depth;
        if (S(1).X <= 0)
        {
            var across = sd * (ToCanvas(sa, sb, sd, px) - sa.X) / sa.Z;
            var along = ed * (ToCanvas(ea, eb, ed, py) - ea.X) / ea.Z;
            depth = Math.Max(0.0, Math.Max(across, along));
        }
        else
        {
            var low = 0.0;
            var high = 1.0;
            var grow = true;
            for (var i = 0; i < 6; i++)
            {
                if (grow && Outside(sa, sb, sd, ea, eb, ed, px, py, high) > 0)
                {
                    low = high;
                    high *= 2.0;
                }
                else
                {
                    grow = false;
                }
            }

            for (var i = 0; i < 20; i++)
            {
                var middle = 0.5 * (low + high);
                if (Outside(sa, sb, sd, ea, eb, ed, px, py, middle) > 0)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            depth = high;
        }

        var ring = Ring(sa, sb, sd, ea, eb, ed, depth);
        var spanY = ring.Y - ring.W;
        var spanX = ring.Z - ring.X;
        var toEdge = spanY != 0 ? Math.Clamp((py - ring.W) / spanY, 0.0, 1.0) : 0.0;
        var toRim = spanX != 0 ? Math.Clamp((px - ring.X) / spanX, 0.0, 1.0) : 0.0;
        var radius = Radius(depth);
        var straight = 1.0 - radius;
        double ux;
        double uy;
        if (toRim <= toEdge && toEdge >= radius)
        {
            ux = depth;
            uy = (1.0 - toEdge) * depth;
        }
        else if (toRim >= radius)
        {
            ux = (1.0 - toRim) * depth;
            uy = depth;
        }
        else
        {
            var angle = Math.Atan2(1.0 - (toEdge / radius), 1.0 - (toRim / radius));
            ux = depth * (straight + (radius * Math.Cos(angle)));
            uy = depth * (straight + (radius * Math.Sin(angle)));
        }

        return (sa.X + (sd * ux * sa.Z), ea.X + (ed * uy * ea.Z));
    }

    private bool Flag(int bit) => ((int)S(6).W & bit) != 0;

    /// <summary>
    /// Inverts the canvas warp at a point, as the shader does, with Newton steps on the zone curve and a search in a corner.
    /// </summary>
    /// <param name="px">The horizontal position, with the view zoom already removed.</param>
    /// <param name="py">The vertical position, with the view zoom already removed.</param>
    /// <returns>The canvas point. It is the input point when the map is separable.</returns>
    public (double X, double Y) Unwarp(double px, double py)
    {
        V sa = W0a, sb = W0b, ea = W2a, eb = W2b;
        double sd = -1, ed = -1;
        var side = SideAt(px);
        var end = EndAt(py);
        if (Flag(1))
        {
            var x = px;
            var y = py;
            if (side > 0)
            {
                Warp(side, out sa, out sb, out sd);
                x = ToCanvas(sa, sb, sd, px);
            }

            if (end > 0)
            {
                Warp(end, out ea, out eb, out ed);
                y = ToCanvas(ea, eb, ed, py);
            }

            return (x, y);
        }

        var cy = py;
        if (side > 0)
        {
            Warp(side, out sa, out sb, out sd);
            cy = sb.Z + ((py - sb.Z) / RowFan(sb.Z, FanScreen(sa, sb, sd, px), DepthScreen(sa, sd, px)));
            if (EndAt(cy) == 0)
            {
                return (ToCanvas(sa, sb, sd, px), cy);
            }
        }

        var cx = px;
        if (end > 0)
        {
            Warp(end, out ea, out eb, out ed);
            var fan = ColumnFan(eb.Z, FanScreen(ea, eb, ed, py), DepthScreen(ea, ed, py));
            cx = eb.Z + ((px - eb.Z) / fan);
            if (SideAt(cx) == 0)
            {
                return (cx, ToCanvas(ea, eb, ed, py));
            }
        }

        if (side == 0)
        {
            side = SideAt(cx);
        }

        if (end == 0)
        {
            end = EndAt(cy);
        }

        if (side == 0 || end == 0)
        {
            var x = cx;
            var y = cy;
            if (side > 0)
            {
                Warp(side, out sa, out sb, out sd);
                x = ToCanvas(sa, sb, sd, px);
            }

            if (end > 0)
            {
                Warp(end, out ea, out eb, out ed);
                y = ToCanvas(ea, eb, ed, py);
            }

            return (x, y);
        }

        Warp(side, out sa, out sb, out sd);
        Warp(end, out ea, out eb, out ed);
        return Corner(sa, sb, sd, ea, eb, ed, px, py);
    }

    /// <inheritdoc/>
    public override (double R, double G, double B) At(double px, double py)
    {
        var wx = S(0).Y + ((px - S(0).Y) / S(0).X);
        var wy = S(0).Z + ((py - S(0).Z) / S(0).X);
        var (cx, cy) = Unwarp(wx, wy);
        var separable = Flag(1);
        var desktop = Flag(2);
        var seams = Flag(4);
        var density = Flag(8);
        var side = SideAt(cx);
        var end = EndAt(cy);
        var sideScale = Math.Min(Scale(W0a, W0b, -1, cx), Scale(W1a, W1b, 1, cx));
        var endScale = Math.Min(Scale(W2a, W2b, -1, cy), Scale(W3a, W3b, 1, cy));
        var rowFan = 1.0;
        var shelf = false;
        var shelfScale = 1.0;
        if (side > 0)
        {
            Warp(side, out var a, out var b, out var dir);
            if (!separable)
            {
                rowFan = RowFan(b.Z, Fan(a, b, dir, cx), Depth(a, b, dir, cx));
            }

            shelf = b.W > 0.5 && dir * (cx - a.X) / a.Z >= 1.0;
            shelfScale = a.W;
        }

        var columnFan = 1.0;
        if (end > 0)
        {
            Warp(end, out var a, out var b, out var dir);
            if (!separable)
            {
                columnFan = ColumnFan(b.Z, Fan(a, b, dir, cy), Depth(a, b, dir, cy));
            }
        }

        var cell = S(0).W;
        var strideX = side > 0 ? Stride(sideScale) : 1.0;
        var strideY = end > 0 ? Stride(endScale) : 1.0;
        if (shelf)
        {
            strideY = Math.Max(strideY, Stride(shelfScale));
        }

        var stepX = cell * strideX;
        var stepY = cell * strideY;
        var column = Math.Floor((cx / stepX) + 0.5) * stepX;
        var row = Math.Floor((cy / stepY) + 0.5) * stepY;
        var unitX = S(0).X * sideScale * columnFan;
        var unitY = S(0).X * endScale * rowFan;
        var flatX = Inside(W0a, -1, cx) && Inside(W1a, 1, cx);
        var flatY = Inside(W2a, -1, cy) && Inside(W3a, 1, cy);
        var hideColumn = !desktop && Inside(W0a, -1, column) && Inside(W1a, 1, column) && flatY;
        var hideRow = !desktop && Inside(W2a, -1, row) && Inside(W3a, 1, row) && flatX;
        const double big = 1000000.0;
        var dColumn = hideColumn ? big : Math.Max(0.0, Math.Abs(((cx - column) * unitX) - 0.5) - 0.5);
        var dRow = hideRow ? big : Math.Max(0.0, Math.Abs(((cy - row) * unitY) - 0.5) - 0.5);
        if (seams && !desktop)
        {
            var topSeam = On(W2a) ? W2a.X : S(8).Z;
            var bottomSeam = On(W3a) ? W3a.X : S(8).Z + S(9).X;
            if (wy >= topSeam && wy <= bottomSeam)
            {
                if (On(W0a))
                {
                    dColumn = Math.Min(dColumn, Math.Max(0.0, Math.Abs(((wx - (W0a.X - 1.0)) * S(0).X) - 0.5) - 0.5));
                }

                if (On(W1a))
                {
                    dColumn = Math.Min(dColumn, Math.Max(0.0, Math.Abs(((wx - W1a.X) * S(0).X) - 0.5) - 0.5));
                }
            }
        }

        var vertical = dColumn <= dRow;
        var d = Math.Min(dColumn, dRow);
        if (d >= big)
        {
            return (0, 0, 0);
        }

        var spacingX = cell * strideX * unitX;
        var spacingY = cell * strideY * unitY;
        var spacing = side > 0 && end > 0 ? Math.Min(spacingX, spacingY) : vertical ? spacingX : spacingY;
        return Glow(px, py, d, spacing, vertical, density);
    }
}
