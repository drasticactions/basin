namespace Basin.Effects;

/// <summary>
/// Evaluates <see cref="GridStepGlowShader"/> on the CPU in doubles. Tests hold the shader rows and the step map inverse to it.
/// </summary>
public sealed class GridStepGlowField : GridGlowTwin
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GridStepGlowField"/> class.
    /// </summary>
    /// <param name="uniforms">The values that <see cref="GridStepGlowShader.Pack"/> wrote.</param>
    public GridStepGlowField(ReadOnlySpan<PixelShaderUniformValue> uniforms)
        : base(uniforms, GridStepGlowShader.UniformCount, 7, 17)
    {
    }

    private V M0 => U(0);

    private V M1 => U(1);

    private V M2 => U(2);

    private V M3 => U(3);

    private V M4 => U(4);

    private static bool Has(double bits, double bit) => Math.Floor(bits / bit) % 2.0 >= 1.0;

    private double WallWidth(int k)
    {
        var bit = k == 1 ? 1.0 : k == 2 ? 2.0 : k == 3 ? 4.0 : 8.0;
        if (!Has(M4.X, bit))
        {
            return 0.0;
        }

        return k == 1 ? M1.X - M2.X
            : k == 2 ? (M2.X + M2.Z) - (M1.X + M1.Z)
            : k == 3 ? M1.Y - M2.Y
            : (M2.Y + M2.W) - (M1.Y + M1.W);
    }

    private (double Center, double Depth) Bulge(int k) =>
        k == 1 ? (U(5).X, U(5).Y) : k == 2 ? (U(5).Z, U(5).W) : k == 3 ? (U(6).X, U(6).Y) : (U(6).Z, U(6).W);

    /// <summary>
    /// Maps an output pixel on the desktop plane or a shelf back to a canvas point, as the shader classifies it.
    /// </summary>
    /// <param name="px">The horizontal pixel position.</param>
    /// <param name="py">The vertical pixel position.</param>
    /// <param name="shelf"><see langword="true"/> when the pixel is on a shelf.</param>
    /// <param name="canvasX">The horizontal canvas position, or <paramref name="px"/> on failure.</param>
    /// <param name="canvasY">The vertical canvas position, or <paramref name="py"/> on failure.</param>
    /// <returns><see langword="false"/> when the pixel is outside the outer box or on a wall.</returns>
    public bool TryToCanvas(double px, double py, out bool shelf, out double canvasX, out double canvasY)
    {
        shelf = false;
        canvasX = px;
        canvasY = py;
        if (px < M3.X || px >= M3.X + M3.Z || py < M3.Y || py >= M3.Y + M3.W)
        {
            return false;
        }

        var (depth, wall, across, along, horizontal, vertical) = Walls(px, py);
        var plane = (horizontal == 0 && vertical == 0) || depth <= 0;
        if (!plane && (depth < 1 || !OnShelf(wall, across, along, horizontal, vertical)))
        {
            return false;
        }

        shelf = !plane;
        var scale = shelf ? M0.W : M0.Z;
        canvasX = M0.X + ((px - M0.X) / scale);
        canvasY = M0.Y + ((py - M0.Y) / scale);
        return true;
    }

    private (double Depth, int Wall, double Across, double Along, int Horizontal, int Vertical) Walls(double px, double py)
    {
        var across = -1000000.0;
        var along = -1000000.0;
        var horizontal = 0;
        var vertical = 0;
        var width = WallWidth(1);
        if (width > 0 && -(px - M1.X) / width > across)
        {
            across = -(px - M1.X) / width;
            horizontal = 1;
        }

        width = WallWidth(2);
        if (width > 0 && (px - (M1.X + M1.Z)) / width > across)
        {
            across = (px - (M1.X + M1.Z)) / width;
            horizontal = 2;
        }

        width = WallWidth(3);
        if (width > 0 && -(py - M1.Y) / width > along)
        {
            along = -(py - M1.Y) / width;
            vertical = 3;
        }

        width = WallWidth(4);
        if (width > 0 && (py - (M1.Y + M1.W)) / width > along)
        {
            along = (py - (M1.Y + M1.W)) / width;
            vertical = 4;
        }

        return (Math.Max(across, along), across >= along ? horizontal : vertical, across, along, horizontal, vertical);
    }

    private bool OnShelf(int wall, double across, double along, int horizontal, int vertical)
    {
        if (across >= 1 && along >= 1)
        {
            var h = horizontal == 1 ? Has(M4.Y, 1.0) : Has(M4.Y, 2.0);
            var v = vertical == 3 ? Has(M4.Y, 4.0) : Has(M4.Y, 8.0);
            return h && v;
        }

        var bit = wall == 1 ? 1.0 : wall == 2 ? 2.0 : wall == 3 ? 4.0 : 8.0;
        return Has(M4.Y, bit);
    }

    /// <inheritdoc/>
    public override (double R, double G, double B) At(double px, double py)
    {
        var lines = Has(S(6).W, 1.0);
        var shelfLines = Has(S(6).W, 2.0);
        var desktopLines = Has(S(6).W, 4.0);
        var density = Has(S(6).W, 8.0);
        var wallLines = Has(S(6).W, 16.0);
        if (!lines || px < M3.X || px >= M3.X + M3.Z || py < M3.Y || py >= M3.Y + M3.W)
        {
            return (0, 0, 0);
        }

        var (depth, wall, across, along, horizontal, vertical) = Walls(px, py);
        var plane = (horizontal == 0 && vertical == 0) || depth <= 0;
        var cell = S(0).W;
        if (plane || depth >= 1)
        {
            var scale = M0.Z;
            if (!plane)
            {
                if (!OnShelf(wall, across, along, horizontal, vertical) || !shelfLines)
                {
                    return (0, 0, 0);
                }

                scale = M0.W;
            }
            else if (!desktopLines)
            {
                return (0, 0, 0);
            }

            var step = cell * Stride(scale);
            var cx = M0.X + ((px - M0.X) / scale);
            var cy = M0.Y + ((py - M0.Y) / scale);
            var nearestX = Math.Floor((cx / step) + 0.5) * step;
            var nearestY = Math.Floor((cy / step) + 0.5) * step;
            var dColumn = Math.Max(0.0, Math.Abs(((cx - nearestX) * scale) - 0.5) - 0.5);
            var dRow = Math.Max(0.0, Math.Abs(((cy - nearestY) * scale) - 0.5) - 0.5);
            return Glow(px, py, Math.Min(dColumn, dRow), step * scale, dColumn <= dRow, density);
        }

        if (!wallLines || wall == 0)
        {
            return (0, 0, 0);
        }

        var width = WallWidth(wall);
        var s = depth;
        var q = Math.Clamp(Math.Floor((s * 4.0) + 0.5), 1.0, 3.0) / 4.0;
        var dQuarter = Math.Max(0.0, (Math.Abs(s - q) * width) - 0.5);
        var sideWall = wall is 1 or 2;
        var a = sideWall ? py : px;
        var innerStart = sideWall ? M1.Y : M1.X;
        var innerLength = sideWall ? M1.W : M1.Z;
        var outerStart = sideWall ? M2.Y : M2.X;
        var outerLength = sideWall ? M2.W : M2.Z;
        var (bulgeCenter, bulgeDepth) = Bulge(wall);
        var k = Math.Clamp(bulgeDepth, 0.0, 0.7) * 4.0 * s * (1.0 - s);
        var span = ((1.0 - s) * innerLength) + (s * outerLength);
        var spanStart = ((1.0 - s) * innerStart) + (s * outerStart);
        var stretch = 1.0;
        if (k > 0 && span > 0)
        {
            var bent = a;
            for (var i = 0; i < 8; i++)
            {
                var fraction = Math.Clamp((a - spanStart) / span, 0.0, 1.0);
                var envelope = 4.0 * fraction * (1.0 - fraction);
                var slope = 4.0 * (1.0 - (2.0 * fraction)) / span;
                a -= (a + (k * envelope * (bulgeCenter - a)) - bent) / (1.0 + (k * ((slope * (bulgeCenter - a)) - envelope)));
            }

            var at = Math.Clamp((a - spanStart) / span, 0.0, 1.0);
            stretch = 1.0 + (k * ((4.0 * (1.0 - (2.0 * at)) / span * (bulgeCenter - a)) - (4.0 * at * (1.0 - at))));
        }

        var t = (a - spanStart) / span;
        var center = sideWall ? M0.Y : M0.X;
        var canvas = center + ((innerStart + (t * innerLength) - center) / M0.Z);
        var crossStep = cell * Stride(M0.Z);
        var nearest = Math.Floor((canvas / crossStep) + 0.5) * crossStep;
        var fan = innerLength > 0 ? span / innerLength : 1.0;
        var unit = M0.Z * fan * stretch;
        var dCross = Math.Max(0.0, (Math.Abs(canvas - nearest) * unit) - 0.5);
        if (dQuarter <= dCross)
        {
            return Glow(px, py, dQuarter, width / 4.0, sideWall, density);
        }

        return Glow(px, py, dCross, crossStep * unit, !sideWall, density);
    }
}
